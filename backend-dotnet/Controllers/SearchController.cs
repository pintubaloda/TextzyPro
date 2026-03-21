using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/search")]
public sealed class SearchController(
    ControlDbContext controlDb,
    TenantDbContext tenantDb,
    TenancyContext tenancy,
    AuthContext auth,
    RbacService rbac) : ControllerBase
{
    private sealed record SearchHit(
        string Kind,
        string Id,
        string Title,
        string Subtitle,
        DateTime AtUtc,
        string Url,
        string? ProviderMessageId);

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string q = "", [FromQuery] int take = 25, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();
        if (!rbac.HasPermission(ApiRead) && !rbac.HasPermission(InboxRead) && !rbac.HasPermission(BillingRead))
            return Forbid();

        var query = (q ?? string.Empty).Trim();
        if (query.Length < 2) return Ok(new { q = query, take = 0, results = Array.Empty<object>() });
        var safeTake = Math.Clamp(take, 5, 100);
        var like = query.ToLowerInvariant();
        var recentFromUtc = DateTime.UtcNow.AddDays(-90);

        var hits = new List<SearchHit>(safeTake * 2);

        // Conversations
        var conversations = await tenantDb.Conversations
            .AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId
                        && (x.CustomerPhone.ToLower().Contains(like) || x.CustomerName.ToLower().Contains(like)))
            .OrderByDescending(x => x.LastMessageAtUtc)
            .Take(Math.Min(20, safeTake))
            .Select(x => new SearchHit(
                "conversation",
                x.Id.ToString(),
                string.IsNullOrWhiteSpace(x.CustomerName) ? x.CustomerPhone : x.CustomerName,
                x.CustomerPhone,
                x.LastMessageAtUtc,
                $"/dashboard/inbox?conversationId={Uri.EscapeDataString(x.Id.ToString())}",
                null))
            .ToListAsync(ct);
        hits.AddRange(conversations);

        // Messages (provider ID or body)
        var messages = await tenantDb.Messages
            .AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId
                        && x.CreatedAtUtc >= recentFromUtc
                        && (x.ProviderMessageId.ToLower().Contains(like) ||
                            x.Recipient.ToLower().Contains(like) ||
                            (x.Body ?? string.Empty).ToLower().Contains(like)))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(Math.Min(25, safeTake))
            .Select(x => new SearchHit(
                "message",
                x.Id.ToString(),
                string.IsNullOrWhiteSpace(x.ProviderMessageId) ? x.Id.ToString() : x.ProviderMessageId,
                x.Recipient + " · " + x.Status + " · " + x.MessageType,
                x.CreatedAtUtc,
                $"/dashboard/inbox?q={Uri.EscapeDataString(x.Recipient)}",
                x.ProviderMessageId))
            .ToListAsync(ct);
        hits.AddRange(messages);

        // Webhook events (meta)
        var webhooks = await controlDb.WebhookEvents
            .AsNoTracking()
            .Where(x => x.Provider == "meta"
                        && x.ReceivedAtUtc >= recentFromUtc
                        && x.TenantId == tenancy.TenantId
                        && (x.EventKey.ToLower().Contains(like) || x.PhoneNumberId.ToLower().Contains(like)))
            .OrderByDescending(x => x.ReceivedAtUtc)
            .Take(Math.Min(20, safeTake))
            .Select(x => new SearchHit(
                "webhook",
                x.Id.ToString(),
                x.EventKey,
                x.Status + " · " + x.PhoneNumberId,
                x.ReceivedAtUtc,
                "/dashboard/health",
                null))
            .ToListAsync(ct);
        hits.AddRange(webhooks);

        // KYC sessions
        var kyc = await controlDb.KycSessions
            .AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId
                        && x.CreatedAtUtc >= recentFromUtc
                        && (x.Id.ToString().ToLower().Contains(like) ||
                            (x.CustomerRef ?? string.Empty).ToLower().Contains(like) ||
                            (x.GstNumber ?? string.Empty).ToLower().Contains(like) ||
                            (x.ProviderCode ?? string.Empty).ToLower().Contains(like)))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(Math.Min(20, safeTake))
            .Select(x => new SearchHit(
                "kyc",
                x.Id.ToString(),
                x.ProviderCode + " · " + x.Status,
                string.IsNullOrWhiteSpace(x.CustomerRef) ? x.Id.ToString() : x.CustomerRef,
                x.CreatedAtUtc,
                $"/dashboard/kyc-reports?sessionId={Uri.EscapeDataString(x.Id.ToString())}",
                null))
            .ToListAsync(ct);
        hits.AddRange(kyc);

        // Invoices
        var invoices = await controlDb.BillingInvoices
            .AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId
                        && x.CreatedAtUtc >= recentFromUtc
                        && (x.InvoiceNo.ToLower().Contains(like) ||
                            (x.ReferenceNo ?? string.Empty).ToLower().Contains(like) ||
                            x.Id.ToString().ToLower().Contains(like)))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(Math.Min(20, safeTake))
            .Select(x => new SearchHit(
                "invoice",
                x.Id.ToString(),
                x.InvoiceNo,
                x.Status,
                x.CreatedAtUtc,
                $"/dashboard/billing?invoiceId={Uri.EscapeDataString(x.Id.ToString())}",
                null))
            .ToListAsync(ct);
        hits.AddRange(invoices);

        var trimmed = hits
            .OrderByDescending(x => x.AtUtc)
            .Take(safeTake)
            .ToList();

        return Ok(new { q = query, take = safeTake, results = trimmed });
    }
}
