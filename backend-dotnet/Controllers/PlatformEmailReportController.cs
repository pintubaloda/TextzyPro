using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/platform/email-report")]
public class PlatformEmailReportController(
    ControlDbContext db,
    AuthContext auth,
    RbacService rbac) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 7, [FromQuery] int take = 100, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        if (days < 1) days = 1;
        if (days > 90) days = 90;
        if (take < 10) take = 10;
        if (take > 300) take = 300;

        var since = DateTime.UtcNow.AddDays(-days);

        var events = await db.WebhookEvents.AsNoTracking()
            .Where(x => x.Provider == "resend" && x.ReceivedAtUtc >= since)
            .OrderByDescending(x => x.ReceivedAtUtc)
            .Take(take)
            .ToListAsync(ct);

        var statusSummary = events
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Status) ? "Unknown" : x.Status)
            .Select(g => new { status = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToList();

        var typeSummary = events
            .GroupBy(x => ExtractType(x.PayloadJson))
            .Select(g => new { type = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToList();

        var otpStats = await db.EmailOtpVerifications.AsNoTracking()
            .Where(x => x.CreatedAtUtc >= since)
            .GroupBy(x => x.Purpose)
            .Select(g => new
            {
                purpose = g.Key,
                total = g.Count(),
                verified = g.Count(x => x.IsVerified),
                expired = g.Count(x => x.Status == "expired")
            })
            .ToListAsync(ct);

        var recent = events.Select(x =>
        {
            var (eventType, to, subject) = ExtractFields(x.PayloadJson);
            return new
            {
                id = x.Id,
                atUtc = x.ReceivedAtUtc,
                status = x.Status,
                eventType,
                to,
                subject
            };
        }).ToList();

        return Ok(new
        {
            days,
            fromUtc = since,
            provider = "resend",
            totals = new
            {
                events = events.Count,
                delivered = events.Count(x => string.Equals(x.Status, "Delivered", StringComparison.OrdinalIgnoreCase)),
                bounced = events.Count(x => string.Equals(x.Status, "Bounced", StringComparison.OrdinalIgnoreCase)),
                complained = events.Count(x => string.Equals(x.Status, "Complained", StringComparison.OrdinalIgnoreCase)),
                opened = events.Count(x => string.Equals(x.Status, "Opened", StringComparison.OrdinalIgnoreCase)),
                clicked = events.Count(x => string.Equals(x.Status, "Clicked", StringComparison.OrdinalIgnoreCase)),
            },
            statusSummary,
            typeSummary,
            otpStats,
            recent
        });
    }

    private static string ExtractType(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty("type", out var t) ? (t.GetString() ?? "unknown") : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private static (string eventType, string to, string subject) ExtractFields(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var eventType = root.TryGetProperty("type", out var t) ? (t.GetString() ?? string.Empty) : string.Empty;
            var data = root.TryGetProperty("data", out var d) ? d : default;
            var to = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("to", out var toEl)
                ? (toEl.GetString() ?? string.Empty)
                : string.Empty;
            var subject = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("subject", out var s)
                ? (s.GetString() ?? string.Empty)
                : string.Empty;
            return (eventType, to, subject);
        }
        catch
        {
            return (string.Empty, string.Empty, string.Empty);
        }
    }
}

