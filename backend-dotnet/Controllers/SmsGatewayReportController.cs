using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
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
    public async Task<IActionResult> List([FromQuery] bool? isSuccess, [FromQuery] bool includeRaw = false, [FromQuery] int limit = 200, CancellationToken ct = default)
    {
        if (!rbac.HasPermission(TemplatesRead)) return Forbid();
        if (!await IsEnabledForTenantAsync(ct)) return Forbid();

        var tenantId = tenancy.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Missing tenant context.");

        var q = controlDb.SmsGatewayRequestLogs.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (isSuccess.HasValue) q = q.Where(x => x.IsSuccess == isSuccess.Value);

        var rawRows = await q
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

        var rows = rawRows.Select(x =>
        {
            var summary = BuildResponseSummary(x.Provider, x.ProviderMessageId, x.ResponseBody, x.Error);
            return new
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
                x.decodedMessage,
                x.HttpStatusCode,
                responseSummary = summary,
                responseBody = includeRaw ? x.ResponseBody : string.Empty,
                x.IsSuccess,
                x.Error,
                x.DurationMs,
                x.ProviderMessageId
            };
        }).ToList();

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

    private static string BuildResponseSummary(string provider, string? providerMessageId, string? responseBody, string? error)
    {
        var p = (provider ?? string.Empty).Trim().ToLowerInvariant();
        if (p != "tata")
            return Truncate(string.IsNullOrWhiteSpace(responseBody) ? (error ?? string.Empty) : responseBody!, 200);

        var jobId = (providerMessageId ?? string.Empty).Trim();
        int? recipientCnt = null;
        int? totalCnt = null;

        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;
                jobId = FirstNonEmpty(
                    jobId,
                    ReadString(root, "jobId"),
                    ReadString(root, "jobid"),
                    ReadString(root, "campaignId"),
                    ReadString(root, "cusTmId"));
                recipientCnt = ReadInt(root, "recepientCnt") ?? ReadInt(root, "recipientCnt");
                totalCnt = ReadInt(root, "totalCnt");
            }
            catch
            {
                // Ignore parsing failures; fall back to providerMessageId.
            }
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(jobId)) parts.Add($"jobId:{jobId}");
        if (recipientCnt.HasValue || totalCnt.HasValue)
        {
            if (recipientCnt.HasValue && totalCnt.HasValue) parts.Add($"sent:{recipientCnt}/{totalCnt}");
            else if (recipientCnt.HasValue) parts.Add($"sent:{recipientCnt}");
            else parts.Add($"total:{totalCnt}");
        }
        if (parts.Count > 0) return string.Join(" ", parts);
        return Truncate(string.IsNullOrWhiteSpace(responseBody) ? (error ?? string.Empty) : responseBody!, 200);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        }
        return string.Empty;
    }

    private static string? ReadString(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static int? ReadInt(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var p)) return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n)) return n;
        if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var s)) return s;
        return null;
    }

    private static string Truncate(string value, int max)
    {
        var v = value ?? string.Empty;
        if (v.Length <= max) return v;
        return v[..max];
    }
}
