using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Textzy.Api.Data;
using Textzy.Api.DTOs;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/inbox")]
public class InboxController(
    TenantDbContext db,
    TenancyContext tenancy,
    AuthContext auth,
    RbacService rbac,
    IHubContext<InboxHub> hub,
    IDistributedCache cache) : ControllerBase
{
    private string ConversationCacheKey(int take, string q, string cursor)
        => $"inbox:conv:v2:{tenancy.TenantId}:{take}:{q}:{cursor}";

    private async Task InvalidateConversationCacheAsync(CancellationToken ct)
    {
        try
        {
            await cache.RemoveAsync($"inbox:conv:v2:{tenancy.TenantId}:100::", ct);
            await cache.RemoveAsync($"inbox:conv:v2:{tenancy.TenantId}:200::", ct);
            await cache.RemoveAsync($"inbox:conv:v2:{tenancy.TenantId}:300::", ct);
        }
        catch
        {
            // no-op
        }
    }

    private async Task AddSystemConversationMessageAsync(Models.Conversation c, string text, CancellationToken ct)
    {
        var body = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(body)) return;

        var msg = new Models.Message
        {
            Id = Guid.NewGuid(),
            TenantId = tenancy.TenantId,
            Channel = Models.ChannelType.WhatsApp,
            Recipient = c.CustomerPhone,
            Body = body,
            MessageType = "system_event",
            Status = "Sent",
            QueueProvider = "system",
            ProviderMessageId = string.Empty,
            IdempotencyKey = $"sys:{c.Id}:{Guid.NewGuid():N}",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Messages.Add(msg);
        c.LastMessageAtUtc = msg.CreatedAtUtc;
        await db.SaveChangesAsync(ct);

        await hub.Clients.Group($"tenant:{tenancy.TenantSlug}").SendAsync("message.sent", new
        {
            id = msg.Id,
            recipient = msg.Recipient,
            body = msg.Body,
            status = msg.Status,
            createdAtUtc = msg.CreatedAtUtc
        }, ct);
    }

    [HttpGet("conversations")]
    public async Task<IActionResult> Conversations([FromQuery] string? q = null, [FromQuery] int take = 100, [FromQuery] string? cursor = null, CancellationToken ct = default)
    {
        if (!rbac.HasPermission(InboxRead)) return Forbid();
        var safeTake = Math.Clamp(take, 10, 300);
        var cursorNorm = (cursor ?? string.Empty).Trim();
        var qNorm = (q ?? string.Empty).Trim().ToLowerInvariant();
        var cacheKey = ConversationCacheKey(safeTake, qNorm, cursorNorm);
        var cached = await cache.GetStringAsync(cacheKey, ct);
        if (!string.IsNullOrWhiteSpace(cached))
        {
            Response.Headers["X-Next-Cursor"] = cursorNorm;
            return Content(cached, "application/json");
        }

        var query = db.Conversations.AsNoTracking().Where(x => x.TenantId == tenancy.TenantId);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x => x.CustomerPhone.Contains(q) || x.CustomerName.Contains(q));
        if (DateTime.TryParse(cursorNorm, out var beforeUtc))
            query = query.Where(x => x.LastMessageAtUtc < beforeUtc.ToUniversalTime());
        var items = query.OrderByDescending(x => x.LastMessageAtUtc).Take(safeTake).ToList();
        var phones = items.Select(x => x.CustomerPhone).Distinct().ToList();
        var windows = db.ConversationWindows
            .Where(x => x.TenantId == tenancy.TenantId && phones.Contains(x.Recipient))
            .ToDictionary(x => x.Recipient, x => x.LastInboundAtUtc);

        var now = DateTime.UtcNow;
        var data = items.Select(c =>
        {
            var hasInbound = windows.TryGetValue(c.CustomerPhone, out var lastInboundAt);
            var hoursSinceInbound = hasInbound ? (now - lastInboundAt).TotalHours : 999d;
            var canReply = hasInbound && hoursSinceInbound <= 24d;
            return new
            {
                c.Id,
                c.CustomerPhone,
                c.CustomerName,
                c.Status,
                c.AssignedUserId,
                c.AssignedUserName,
                c.LabelsCsv,
                c.LastMessageAtUtc,
                c.CreatedAtUtc,
                lastInboundAtUtc = hasInbound ? lastInboundAt : (DateTime?)null,
                canReply,
                hoursSinceInbound
            };
        }).ToList();

        var nextCursor = data.Count == safeTake ? data.Last().LastMessageAtUtc.ToString("O") : string.Empty;
        if (!string.IsNullOrWhiteSpace(nextCursor)) Response.Headers["X-Next-Cursor"] = nextCursor;
        var json = JsonSerializer.Serialize(data);
        await cache.SetStringAsync(cacheKey, json, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(15)
        }, ct);
        return Content(json, "application/json");
    }

    [HttpPost("conversations/{id:guid}/assign")]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignConversationRequest req, CancellationToken ct)
    {
        if (!rbac.HasPermission(InboxWrite)) return Forbid();
        var c = await db.Conversations.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenancy.TenantId, ct);
        if (c is null) return NotFound();
        c.AssignedUserId = req.UserId;
        c.AssignedUserName = req.UserName;
        await db.SaveChangesAsync(ct);
        await InvalidateConversationCacheAsync(ct);
        var actor = !string.IsNullOrWhiteSpace(auth.FullName)
            ? auth.FullName.Trim()
            : (string.IsNullOrWhiteSpace(auth.Email) ? "Agent" : auth.Email);
        var target = string.IsNullOrWhiteSpace(req.UserName) ? "Unassigned" : req.UserName.Trim();
        await AddSystemConversationMessageAsync(c, $"Conversation assigned to {target} by {actor}.", ct);
        await hub.Clients.Group($"tenant:{tenancy.TenantSlug}").SendAsync("conversation.assigned", new { c.Id, c.AssignedUserId, c.AssignedUserName }, ct);
        return Ok(c);
    }

    [HttpPost("conversations/{id:guid}/transfer")]
    public async Task<IActionResult> Transfer(Guid id, [FromBody] TransferConversationRequest req, CancellationToken ct)
    {
        if (!rbac.HasPermission(InboxWrite)) return Forbid();
        var c = await db.Conversations.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenancy.TenantId, ct);
        if (c is null) return NotFound();
        c.AssignedUserId = req.UserId;
        c.AssignedUserName = req.UserName;
        c.Status = "Open";
        await db.SaveChangesAsync(ct);
        await InvalidateConversationCacheAsync(ct);
        var actor = !string.IsNullOrWhiteSpace(auth.FullName)
            ? auth.FullName.Trim()
            : (string.IsNullOrWhiteSpace(auth.Email) ? "Agent" : auth.Email);
        var target = string.IsNullOrWhiteSpace(req.UserName) ? "Unassigned" : req.UserName.Trim();
        await AddSystemConversationMessageAsync(c, $"Conversation transferred to {target} by {actor}.", ct);
        await hub.Clients.Group($"tenant:{tenancy.TenantSlug}").SendAsync("conversation.transferred", new { c.Id, c.AssignedUserId, c.AssignedUserName }, ct);
        return Ok(c);
    }

    [HttpPost("conversations/{id:guid}/labels")]
    public async Task<IActionResult> Labels(Guid id, [FromBody] UpdateConversationLabelsRequest req, CancellationToken ct)
    {
        if (!rbac.HasPermission(InboxWrite)) return Forbid();
        var c = await db.Conversations.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenancy.TenantId, ct);
        if (c is null) return NotFound();
        c.LabelsCsv = string.Join(",", (req.Labels ?? []).Select(x => (x ?? string.Empty).Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase));
        await db.SaveChangesAsync(ct);
        await InvalidateConversationCacheAsync(ct);
        await hub.Clients.Group($"tenant:{tenancy.TenantSlug}").SendAsync("conversation.labels", new { c.Id, c.LabelsCsv }, ct);
        return Ok(c);
    }

    [HttpPost("conversations/{id:guid}/notes")]
    public async Task<IActionResult> AddNote(Guid id, [FromBody] AddConversationNoteRequest req, CancellationToken ct)
    {
        if (!rbac.HasPermission(InboxWrite)) return Forbid();
        var c = await db.Conversations.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenancy.TenantId, ct);
        if (c is null) return NotFound();

        var note = new Textzy.Api.Models.ConversationNote
        {
            Id = Guid.NewGuid(),
            TenantId = tenancy.TenantId,
            ConversationId = id,
            Body = req.Body,
            CreatedByUserId = auth.UserId,
            CreatedByName = auth.Email
        };
        db.ConversationNotes.Add(note);
        await db.SaveChangesAsync(ct);
        await hub.Clients.Group($"tenant:{tenancy.TenantSlug}").SendAsync("conversation.note", note, ct);
        return Ok(note);
    }

    [HttpGet("conversations/{id:guid}/notes")]
    public IActionResult Notes(Guid id, [FromQuery] int take = 50)
    {
        if (!rbac.HasPermission(InboxRead)) return Forbid();
        var safeTake = Math.Clamp(take, 10, 200);
        return Ok(db.ConversationNotes.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId && x.ConversationId == id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(safeTake)
            .ToList());
    }

    [HttpGet("conversations/{id:guid}/messages")]
    public IActionResult ConversationMessages(Guid id, [FromQuery] int take = 80, [FromQuery] string? cursor = null)
    {
        if (!rbac.HasPermission(InboxRead)) return Forbid();
        var safeTake = Math.Clamp(take, 20, 300);
        var c = db.Conversations.AsNoTracking().FirstOrDefault(x => x.Id == id && x.TenantId == tenancy.TenantId);
        if (c is null) return NotFound();

        var query = db.Messages
            .AsNoTracking()
            .Where(m => m.TenantId == tenancy.TenantId && m.Recipient == c.CustomerPhone && m.Channel == Models.ChannelType.WhatsApp);
        if (DateTime.TryParse((cursor ?? string.Empty).Trim(), out var beforeUtc))
            query = query.Where(m => m.CreatedAtUtc < beforeUtc.ToUniversalTime());

        var items = query.OrderByDescending(m => m.CreatedAtUtc).Take(safeTake).ToList();
        if (items.Count == safeTake) Response.Headers["X-Next-Cursor"] = items.Last().CreatedAtUtc.ToString("O");
        items.Reverse();
        return Ok(items);
    }

    [HttpPost("typing")]
    public async Task<IActionResult> Typing([FromBody] TypingEventRequest req, CancellationToken ct)
    {
        if (!rbac.HasPermission(InboxWrite)) return Forbid();
        var displayName = !string.IsNullOrWhiteSpace(auth.FullName)
            ? auth.FullName.Trim()
            : (string.IsNullOrWhiteSpace(auth.Email) ? "Agent" : auth.Email);
        await hub.Clients.Group($"tenant:{tenancy.TenantSlug}").SendAsync("conversation.typing", new
        {
            req.ConversationId,
            user = auth.Email,
            userName = displayName,
            req.IsTyping
        }, ct);
        return Ok();
    }

    [HttpGet("sla")]
    public IActionResult Sla([FromQuery] int thresholdMinutes = 15)
    {
        if (!rbac.HasPermission(InboxRead)) return Forbid();
        var threshold = DateTime.UtcNow.AddMinutes(-Math.Abs(thresholdMinutes));
        var breached = db.Conversations
            .Where(x => x.TenantId == tenancy.TenantId && x.Status == "Open" && x.LastMessageAtUtc <= threshold)
            .OrderBy(x => x.LastMessageAtUtc)
            .ToList();
        return Ok(new { thresholdMinutes = Math.Abs(thresholdMinutes), breachedCount = breached.Count, items = breached });
    }
}
