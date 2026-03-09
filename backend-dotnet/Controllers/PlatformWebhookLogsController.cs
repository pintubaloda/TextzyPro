using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/platform/webhook-logs")]
public class PlatformWebhookLogsController(ControlDbContext db, AuthContext auth, RbacService rbac) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string provider = "", [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();
        limit = Math.Clamp(limit, 1, 500);

        var auditQuery = db.AuditLogs.Where(x =>
            x.Action.Contains("webhook") ||
            x.Action.StartsWith("waba.workflow."));
        if (!string.IsNullOrWhiteSpace(provider))
            auditQuery = auditQuery.Where(x => x.Details.Contains($"provider={provider}"));

        var auditRows = await auditQuery
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(limit)
            .Select(x => new
            {
                source = "audit",
                x.Id,
                provider = ParseProviderFromAudit(x.Details),
                action = x.Action,
                details = x.Details,
                status = string.Empty,
                retryCount = 0,
                x.CreatedAtUtc
            })
            .ToListAsync(ct);

        var eventQuery = db.WebhookEvents.AsQueryable();
        if (!string.IsNullOrWhiteSpace(provider))
            eventQuery = eventQuery.Where(x => x.Provider == provider);

        var eventRows = await eventQuery
            .OrderByDescending(x => x.ReceivedAtUtc)
            .Take(limit)
            .Select(x => new
            {
                source = "event",
                x.Id,
                provider = x.Provider,
                action = "webhook.event",
                details = x.LastError != "" ? x.LastError : x.EventKey,
                status = x.Status,
                retryCount = x.RetryCount,
                CreatedAtUtc = x.ReceivedAtUtc
            })
            .ToListAsync(ct);

        var merged = auditRows
            .Concat(eventRows)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(limit)
            .ToList();

        return Ok(merged.Select(x => new
        {
            x.source,
            x.Id,
            x.provider,
            x.action,
            x.details,
            x.status,
            x.retryCount,
            x.CreatedAtUtc
        }));
    }

    private static string ParseProviderFromAudit(string details)
    {
        if (string.IsNullOrWhiteSpace(details)) return "";
        var marker = "provider=";
        var idx = details.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "";
        var tail = details[(idx + marker.Length)..];
        var end = tail.IndexOfAny(new[] { ' ', ';', ',', '|' });
        return (end >= 0 ? tail[..end] : tail).Trim();
    }
}
