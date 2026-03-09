using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/analytics/webhook")]
public class TenantWebhookAnalyticsController(
    ControlDbContext controlDb,
    TenancyContext tenancy,
    AuthContext auth,
    RbacService rbac) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 7, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(ApiRead)) return Forbid();
        if (tenancy.TenantId == Guid.Empty) return BadRequest("Tenant context missing.");

        var safeDays = Math.Clamp(days, 1, 90);
        var from = DateTime.UtcNow.Date.AddDays(-safeDays + 1);

        var tenant = await controlDb.Tenants.FirstOrDefaultAsync(x => x.Id == tenancy.TenantId, ct);
        if (tenant is null) return NotFound("Tenant not found.");

        using var tenantDb = SeedData.CreateTenantDbContext(tenant.DataConnectionString);
        var events = tenantDb.MessageEvents.Where(x => x.TenantId == tenancy.TenantId && x.CreatedAtUtc >= from);

        var statusSummaryRows = await events
            .Where(x => x.Direction == "outbound")
            .GroupBy(x => x.State)
            .Select(g => new { key = g.Key, count = g.Count() })
            .ToListAsync(ct);
        var pricingRows = await events
            .Where(x => x.Direction == "outbound" && x.PricingCategory != "")
            .GroupBy(x => x.PricingCategory)
            .Select(g => new { key = g.Key, count = g.Count() })
            .ToListAsync(ct);
        var originRows = await events
            .Where(x => x.Direction == "outbound" && x.ConversationOriginType != "")
            .GroupBy(x => x.ConversationOriginType)
            .Select(g => new { key = g.Key, count = g.Count() })
            .ToListAsync(ct);
        var trendRows = await events
            .Where(x => x.Direction == "outbound")
            .GroupBy(x => new { day = x.CreatedAtUtc.Date, x.State })
            .Select(g => new { g.Key.day, g.Key.State, count = g.Count() })
            .ToListAsync(ct);

        var failureCodeRows = await events
            .Where(x => x.Direction == "outbound" && x.State == "Failed")
            .Select(x => x.RawPayloadJson)
            .ToListAsync(ct);
        var failureCodes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in failureCodeRows)
        {
            var code = ExtractFirstErrorCode(raw);
            if (string.IsNullOrWhiteSpace(code)) code = "unknown";
            failureCodes.TryGetValue(code, out var curr);
            failureCodes[code] = curr + 1;
        }

        return Ok(new
        {
            tenantId = tenancy.TenantId,
            from,
            days = safeDays,
            statusSummary = statusSummaryRows.ToDictionary(x => x.key ?? "unknown", x => x.count),
            pricingSummary = pricingRows.ToDictionary(x => x.key ?? "unknown", x => x.count),
            originSummary = originRows.ToDictionary(x => x.key ?? "unknown", x => x.count),
            failureCodes,
            trend = trendRows
                .OrderBy(x => x.day)
                .ThenBy(x => x.State)
                .Select(x => new { day = x.day.ToString("yyyy-MM-dd"), state = x.State, x.count })
        });
    }

    private static string ExtractFirstErrorCode(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            {
                var e = errors[0];
                if (e.TryGetProperty("code", out var code)) return code.ToString();
            }
            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
