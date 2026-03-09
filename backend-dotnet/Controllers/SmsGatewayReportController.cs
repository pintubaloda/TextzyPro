using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/sms/gateway-report")]
public class SmsGatewayReportController(
    ControlDbContext controlDb,
    TenancyContext tenancy,
    RbacService rbac) : ControllerBase
{
    private const string TenantSmsGatewayReportFeatureKey = "tenant.smsGatewayReport.enabled";

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct = default)
    {
        if (!rbac.HasPermission(TemplatesRead)) return Forbid();
        var enabled = await IsEnabledForTenantAsync(ct);
        return Ok(new { enabled });
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool? isSuccess, [FromQuery] int limit = 200, CancellationToken ct = default)
    {
        if (!rbac.HasPermission(TemplatesRead)) return Forbid();
        if (!await IsEnabledForTenantAsync(ct)) return Forbid();

        var tenantId = tenancy.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Missing tenant context.");

        var q = controlDb.SmsGatewayRequestLogs.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (isSuccess.HasValue) q = q.Where(x => x.IsSuccess == isSuccess.Value);

        var rows = await q
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(x => new
            {
                x.Id,
                x.CreatedAtUtc,
                x.Provider,
                x.Recipient,
                x.Sender,
                x.PeId,
                x.TemplateId,
                x.HttpMethod,
                x.RequestUrlMasked,
                decodedMessage = ExtractDecodedMessage(x.RequestUrlMasked),
                x.HttpStatusCode,
                x.ResponseBody,
                x.IsSuccess,
                x.Error,
                x.DurationMs,
                x.ProviderMessageId
            })
            .ToListAsync(ct);

        return Ok(rows);
    }

    private async Task<bool> IsEnabledForTenantAsync(CancellationToken ct)
    {
        var tenantId = tenancy.TenantId;
        if (tenantId == Guid.Empty) return false;
        return await controlDb.TenantFeatureFlags.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.FeatureKey == TenantSmsGatewayReportFeatureKey)
            .Select(x => x.IsEnabled)
            .FirstOrDefaultAsync(ct);
    }

    private static string ExtractDecodedMessage(string? requestUrl)
    {
        if (string.IsNullOrWhiteSpace(requestUrl)) return string.Empty;
        var qIndex = requestUrl.IndexOf('?');
        if (qIndex < 0 || qIndex >= requestUrl.Length - 1) return string.Empty;
        var query = requestUrl[(qIndex + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in query)
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 0) continue;
            if (!string.Equals(parts[0], "msg", StringComparison.OrdinalIgnoreCase)) continue;
            var raw = parts.Length > 1 ? parts[1] : string.Empty;
            try
            {
                return Uri.UnescapeDataString(raw.Replace('+', ' '));
            }
            catch
            {
                return raw;
            }
        }
        return string.Empty;
    }
}
