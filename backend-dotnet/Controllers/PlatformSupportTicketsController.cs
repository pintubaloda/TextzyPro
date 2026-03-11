using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/platform/support")]
public class PlatformSupportTicketsController(
    ControlDbContext db,
    AuthContext auth,
    RbacService rbac,
    AuditLogService audit,
    IEmailService emailService,
    ILogger<PlatformSupportTicketsController> logger) : ControllerBase
{
    [HttpGet("tickets")]
    public async Task<IActionResult> List(
        [FromQuery] string status = "",
        [FromQuery] string service = "",
        [FromQuery] string q = "",
        [FromQuery] Guid? tenantId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        var normalizedStatus = SupportTicketCatalog.NormalizeStatus(status);
        var serviceFilter = (service ?? string.Empty).Trim().ToLowerInvariant();
        var search = (q ?? string.Empty).Trim().ToLowerInvariant();
        var safePageSize = Math.Clamp(pageSize, 10, 100);
        var requestedPage = Math.Max(1, page);

        var baseQuery = db.SupportTickets.AsNoTracking().AsQueryable();
        if (tenantId.HasValue && tenantId.Value != Guid.Empty)
            baseQuery = baseQuery.Where(x => x.TenantId == tenantId.Value);
        if (!string.IsNullOrWhiteSpace(normalizedStatus))
            baseQuery = baseQuery.Where(x => x.Status == normalizedStatus);
        if (!string.IsNullOrWhiteSpace(serviceFilter))
        {
            baseQuery = baseQuery.Where(x =>
                x.ServiceKey.ToLower() == serviceFilter ||
                x.ServiceName.ToLower().Contains(serviceFilter));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            baseQuery = baseQuery.Where(x =>
                x.TicketNo.ToLower().Contains(search) ||
                x.Subject.ToLower().Contains(search) ||
                x.ServiceName.ToLower().Contains(search) ||
                x.CompanyName.ToLower().Contains(search) ||
                x.TenantName.ToLower().Contains(search) ||
                x.TenantSlug.ToLower().Contains(search) ||
                x.CreatedByName.ToLower().Contains(search) ||
                x.CreatedByEmail.ToLower().Contains(search) ||
                x.RequesterPhone.Contains(search));
        }

        var totalCount = await baseQuery.CountAsync(ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)safePageSize));
        var safePage = Math.Min(requestedPage, totalPages);
        var skip = (safePage - 1) * safePageSize;

        var messageCounts = db.SupportTicketMessages.AsNoTracking()
            .GroupBy(x => x.TicketId)
            .Select(g => new { TicketId = g.Key, Count = g.Count() });

        var rows = await (
            from ticket in baseQuery
            from messageCount in messageCounts.Where(x => x.TicketId == ticket.Id).DefaultIfEmpty()
            orderby ticket.Status == "open" descending, ticket.LastMessageAtUtc descending
            select new
            {
                ticket.Id,
                ticket.TicketNo,
                ticket.TenantId,
                ticket.TenantName,
                ticket.TenantSlug,
                ticket.CompanyName,
                ticket.CreatedByName,
                ticket.CreatedByEmail,
                ticket.RequesterName,
                ticket.RequesterEmail,
                ticket.RequesterPhone,
                ticket.ServiceKey,
                ticket.ServiceName,
                ticket.Subject,
                ticket.Status,
                ticket.Priority,
                ticket.LastMessagePreview,
                ticket.LastActorType,
                ticket.LastMessageAtUtc,
                ticket.CreatedAtUtc,
                ticket.UpdatedAtUtc,
                MessageCount = messageCount != null ? messageCount.Count : 0
            })
            .Skip(skip)
            .Take(safePageSize)
            .ToListAsync(ct);

        var summary = await db.SupportTickets.AsNoTracking()
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var serviceOptions = await db.SupportTickets.AsNoTracking()
            .Select(x => new { key = x.ServiceKey, name = x.ServiceName })
            .Distinct()
            .OrderBy(x => x.name)
            .ToListAsync(ct);
        var tenantOptions = await db.SupportTickets.AsNoTracking()
            .Select(x => new { x.TenantId, x.TenantName, x.TenantSlug })
            .Distinct()
            .OrderBy(x => x.TenantName)
            .Take(500)
            .ToListAsync(ct);

        return Ok(new
        {
            page = safePage,
            pageSize = safePageSize,
            totalCount,
            totalPages,
            hasPreviousPage = safePage > 1,
            hasNextPage = safePage < totalPages,
            summary = new
            {
                total = summary.Sum(x => x.Count),
                open = summary.Where(x => x.Status == "open").Sum(x => x.Count),
                waitingOnCustomer = summary.Where(x => x.Status == "waiting_on_customer").Sum(x => x.Count),
                closed = summary.Where(x => x.Status == "closed").Sum(x => x.Count),
                urgent = await db.SupportTickets.AsNoTracking().CountAsync(x => x.Priority == "urgent", ct)
            },
            serviceOptions,
            tenantOptions = tenantOptions.Select(x => new
            {
                tenantId = x.TenantId,
                tenantName = x.TenantName,
                tenantSlug = x.TenantSlug
            }),
            items = rows.Select(x => new
            {
                id = x.Id,
                ticketNo = x.TicketNo,
                tenantId = x.TenantId,
                tenantName = x.TenantName,
                tenantSlug = x.TenantSlug,
                companyName = x.CompanyName,
                createdByName = x.CreatedByName,
                createdByEmail = x.CreatedByEmail,
                requesterName = string.IsNullOrWhiteSpace(x.RequesterName) ? x.CreatedByName : x.RequesterName,
                requesterEmail = string.IsNullOrWhiteSpace(x.RequesterEmail) ? x.CreatedByEmail : x.RequesterEmail,
                requesterPhone = x.RequesterPhone,
                serviceKey = x.ServiceKey,
                serviceName = x.ServiceName,
                subject = x.Subject,
                status = x.Status,
                priority = x.Priority,
                lastMessagePreview = x.LastMessagePreview,
                lastActorType = x.LastActorType,
                lastMessageAtUtc = x.LastMessageAtUtc,
                createdAtUtc = x.CreatedAtUtc,
                updatedAtUtc = x.UpdatedAtUtc,
                messageCount = x.MessageCount
            })
        });
    }

    [HttpGet("tickets/{ticketId:guid}")]
    public async Task<IActionResult> Details(Guid ticketId, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        var ticket = await db.SupportTickets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ticketId, ct);
        if (ticket is null) return NotFound("Ticket not found.");
        var messages = await db.SupportTicketMessages.AsNoTracking()
            .Where(x => x.TicketId == ticketId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);
        var relatedTickets = await LoadRelatedTicketsAsync(ticket, ct);

        return Ok(new
        {
            ticket = new
            {
                id = ticket.Id,
                ticketNo = ticket.TicketNo,
                tenantId = ticket.TenantId,
                tenantName = ticket.TenantName,
                tenantSlug = ticket.TenantSlug,
                companyName = ticket.CompanyName,
                createdByName = ticket.CreatedByName,
                createdByEmail = ticket.CreatedByEmail,
                requesterName = string.IsNullOrWhiteSpace(ticket.RequesterName) ? ticket.CreatedByName : ticket.RequesterName,
                requesterEmail = string.IsNullOrWhiteSpace(ticket.RequesterEmail) ? ticket.CreatedByEmail : ticket.RequesterEmail,
                requesterPhone = ticket.RequesterPhone,
                serviceKey = ticket.ServiceKey,
                serviceName = ticket.ServiceName,
                subject = ticket.Subject,
                status = ticket.Status,
                priority = ticket.Priority,
                lastMessagePreview = ticket.LastMessagePreview,
                lastActorType = ticket.LastActorType,
                lastMessageAtUtc = ticket.LastMessageAtUtc,
                closedAtUtc = ticket.ClosedAtUtc,
                reopenedAtUtc = ticket.ReopenedAtUtc,
                createdAtUtc = ticket.CreatedAtUtc,
                updatedAtUtc = ticket.UpdatedAtUtc
            },
            messages = messages.Select(x => new
            {
                id = x.Id,
                authorUserId = x.AuthorUserId,
                authorName = x.AuthorName,
                authorEmail = x.AuthorEmail,
                authorType = x.AuthorType,
                body = x.Body,
                createdAtUtc = x.CreatedAtUtc
            }),
            relatedTickets
        });
    }

    [HttpPost("tickets/{ticketId:guid}/reply")]
    public async Task<IActionResult> Reply(Guid ticketId, [FromBody] ReplyRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsWrite)) return Forbid();

        string body;
        try
        {
            body = InputGuardService.RequireTrimmed(request.Body, "Reply", 4000);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        var ticket = await db.SupportTickets.FirstOrDefaultAsync(x => x.Id == ticketId, ct);
        if (ticket is null) return NotFound("Ticket not found.");

        var now = DateTime.UtcNow;
        var authorName = string.IsNullOrWhiteSpace(auth.FullName) ? auth.Email : auth.FullName;
        var message = new SupportTicketMessage
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            TenantId = ticket.TenantId,
            AuthorUserId = auth.UserId,
            AuthorName = authorName,
            AuthorEmail = auth.Email,
            AuthorType = "platform",
            Body = body,
            CreatedAtUtc = now
        };

        db.SupportTicketMessages.Add(message);
        ticket.Status = "waiting_on_customer";
        ticket.LastActorType = "platform";
        ticket.LastMessagePreview = SupportTicketCatalog.BuildPreview(body);
        ticket.LastMessageAtUtc = now;
        ticket.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("platform.support.ticket.reply", $"ticket={ticket.TicketNo}", ct);
        await TrySendSupportEmailAsync(
            ticket,
            "Platform replied",
            "Your support ticket has a new reply from the Textzy support team.",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Service"] = ticket.ServiceName,
                ["Status"] = "Waiting on customer",
                ["Latest reply"] = SupportTicketCatalog.BuildPreview(body)
            },
            ct);

        var messages = await db.SupportTicketMessages.AsNoTracking()
            .Where(x => x.TicketId == ticket.Id)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);
        var relatedTickets = await LoadRelatedTicketsAsync(ticket, ct);
        return Ok(new
        {
            ticket = new
            {
                id = ticket.Id,
                ticketNo = ticket.TicketNo,
                status = ticket.Status,
                updatedAtUtc = ticket.UpdatedAtUtc,
                lastMessageAtUtc = ticket.LastMessageAtUtc
            },
            messages = messages.Select(x => new
            {
                id = x.Id,
                authorName = x.AuthorName,
                authorEmail = x.AuthorEmail,
                authorType = x.AuthorType,
                body = x.Body,
                createdAtUtc = x.CreatedAtUtc
            }),
            relatedTickets
        });
    }

    [HttpPost("tickets/{ticketId:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid ticketId, [FromBody] UpdateStatusRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsWrite)) return Forbid();

        var nextStatus = SupportTicketCatalog.NormalizeStatus(request.Status);
        if (string.IsNullOrWhiteSpace(nextStatus)) return BadRequest("Valid status is required.");

        var ticket = await db.SupportTickets.FirstOrDefaultAsync(x => x.Id == ticketId, ct);
        if (ticket is null) return NotFound("Ticket not found.");

        var now = DateTime.UtcNow;
        ticket.Status = nextStatus;
        ticket.UpdatedAtUtc = now;
        if (nextStatus == "closed")
        {
            ticket.ClosedAtUtc = now;
            ticket.ClosedByUserId = auth.UserId;
        }
        else
        {
            ticket.ClosedAtUtc = null;
            ticket.ClosedByUserId = null;
            if (nextStatus == "open")
            {
                ticket.ReopenedAtUtc = now;
                ticket.ReopenedByUserId = auth.UserId;
            }
        }

        var note = (request.Message ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(note))
        {
            if (note.Length > 4000) return BadRequest("Message is too long.");
            var authorName = string.IsNullOrWhiteSpace(auth.FullName) ? auth.Email : auth.FullName;
            var message = new SupportTicketMessage
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                TenantId = ticket.TenantId,
                AuthorUserId = auth.UserId,
                AuthorName = authorName,
                AuthorEmail = auth.Email,
                AuthorType = "platform",
                Body = note,
                CreatedAtUtc = now
            };
            db.SupportTicketMessages.Add(message);
            ticket.LastActorType = "platform";
            ticket.LastMessagePreview = SupportTicketCatalog.BuildPreview(note);
            ticket.LastMessageAtUtc = now;
        }

        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("platform.support.ticket.status", $"ticket={ticket.TicketNo}; status={nextStatus}", ct);
        await TrySendSupportEmailAsync(
            ticket,
            nextStatus == "closed" ? "Ticket closed" : nextStatus == "open" ? "Ticket reopened" : "Ticket status updated",
            nextStatus switch
            {
                "closed" => "Your support ticket has been marked as closed by the Textzy support team.",
                "open" => "Your support ticket has been reopened and moved back into the active queue.",
                "waiting_on_customer" => "Your support ticket is waiting on your response.",
                _ => "Your support ticket status has changed."
            },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Service"] = ticket.ServiceName,
                ["Status"] = ticket.Status.Replace("_", " "),
                ["Updated"] = now.ToLocalTime().ToString("g")
            },
            ct);
        return Ok(new
        {
            id = ticket.Id,
            ticketNo = ticket.TicketNo,
            status = ticket.Status,
            closedAtUtc = ticket.ClosedAtUtc,
            reopenedAtUtc = ticket.ReopenedAtUtc,
            updatedAtUtc = ticket.UpdatedAtUtc
        });
    }

    private async Task<IReadOnlyList<object>> LoadRelatedTicketsAsync(SupportTicket ticket, CancellationToken ct)
    {
        var requesterEmail = string.IsNullOrWhiteSpace(ticket.RequesterEmail) ? ticket.CreatedByEmail : ticket.RequesterEmail;
        var requesterPhone = ticket.RequesterPhoneNormalized;
        if (string.IsNullOrWhiteSpace(requesterEmail) && string.IsNullOrWhiteSpace(requesterPhone))
            return Array.Empty<object>();

        var query = db.SupportTickets.AsNoTracking().Where(x => x.Id != ticket.Id);
        if (!string.IsNullOrWhiteSpace(requesterEmail) && !string.IsNullOrWhiteSpace(requesterPhone))
        {
            query = query.Where(x =>
                x.RequesterEmail.ToLower() == requesterEmail.ToLower() ||
                x.CreatedByEmail.ToLower() == requesterEmail.ToLower() ||
                x.RequesterPhoneNormalized == requesterPhone);
        }
        else if (!string.IsNullOrWhiteSpace(requesterEmail))
        {
            query = query.Where(x =>
                x.RequesterEmail.ToLower() == requesterEmail.ToLower() ||
                x.CreatedByEmail.ToLower() == requesterEmail.ToLower());
        }
        else
        {
            query = query.Where(x => x.RequesterPhoneNormalized == requesterPhone);
        }

        var items = await query
            .OrderByDescending(x => x.LastMessageAtUtc)
            .Take(8)
            .Select(x => new
            {
                id = x.Id,
                ticketNo = x.TicketNo,
                subject = x.Subject,
                status = x.Status,
                serviceName = x.ServiceName,
                tenantName = x.TenantName,
                tenantSlug = x.TenantSlug,
                requesterName = string.IsNullOrWhiteSpace(x.RequesterName) ? x.CreatedByName : x.RequesterName,
                requesterPhone = x.RequesterPhone,
                lastMessageAtUtc = x.LastMessageAtUtc
            })
            .ToListAsync(ct);
        return items.Cast<object>().ToList();
    }

    private async Task TrySendSupportEmailAsync(
        SupportTicket ticket,
        string title,
        string description,
        Dictionary<string, string> details,
        CancellationToken ct)
    {
        var recipientEmail = string.IsNullOrWhiteSpace(ticket.RequesterEmail) ? ticket.CreatedByEmail : ticket.RequesterEmail;
        var recipientName = string.IsNullOrWhiteSpace(ticket.RequesterName) ? ticket.CreatedByName : ticket.RequesterName;
        if (string.IsNullOrWhiteSpace(recipientEmail)) return;
        try
        {
            await emailService.SendSupportTicketEventAsync(
                recipientEmail,
                recipientName,
                ticket.CompanyName,
                ticket.TicketNo,
                ticket.Subject,
                title,
                description,
                details,
                ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send platform support email for ticket {TicketNo}", ticket.TicketNo);
        }
    }

    public sealed class ReplyRequest
    {
        public string Body { get; set; } = string.Empty;
    }

    public sealed class UpdateStatusRequest
    {
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
