using System.Text.Json;
using System.Reflection;
using System.Text;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/platform/kyc/reports")]
public class PlatformKycReportsController(ControlDbContext db, AuthContext auth, RbacService rbac, SecretCryptoService crypto) : ControllerBase
{
    [HttpOptions]
    public IActionResult OptionsRoot()
    {
        ApplyCorsHeaders();
        return NoContent();
    }

    [HttpOptions("{id:guid}")]
    public IActionResult OptionsById()
    {
        ApplyCorsHeaders();
        return NoContent();
    }

    [HttpGet("ping")]
    public IActionResult Ping()
    {
        ApplyCorsHeaders();
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "unknown";
        return Content(JsonSerializer.Serialize(new
        {
            ok = true,
            serverTimeUtc = DateTime.UtcNow,
            build = version
        }), "application/json; charset=utf-8");
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string tenantId = "",
        [FromQuery] string tenantSlug = "",
        [FromQuery] string status = "",
        [FromQuery] string q = "",
        [FromQuery] string fromUtc = "",
        [FromQuery] string toUtc = "",
        [FromQuery] bool includeBase64 = false,
        [FromQuery] int take = 100,
        [FromQuery] int skip = 0,
        CancellationToken ct = default)
    {
        ApplyCorsHeaders();
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        take = Math.Clamp(take, 1, 500);
        skip = Math.Max(0, skip);

        var query = db.KycSessions.AsNoTracking().AsQueryable();

        if (Guid.TryParse(tenantId, out var tid) && tid != Guid.Empty)
        {
            query = query.Where(x => x.TenantId == tid);
        }
        else if (!string.IsNullOrWhiteSpace(tenantSlug))
        {
            var slug = tenantSlug.Trim().ToLowerInvariant();
            var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug, ct);
            if (tenant is null)
                return Ok(new { totalCount = 0, items = Array.Empty<object>() });
            query = query.Where(x => x.TenantId == tenant.Id);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim().ToLowerInvariant();
            query = query.Where(x => (x.Status ?? string.Empty).ToLower() == normalizedStatus);
        }

        if (DateTime.TryParse(fromUtc, out var fromDt))
            query = query.Where(x => x.CreatedAtUtc >= fromDt);
        if (DateTime.TryParse(toUtc, out var toDt))
            query = query.Where(x => x.CreatedAtUtc <= toDt);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim().ToLowerInvariant();
            query = query.Where(x =>
                (x.CustomerRef ?? string.Empty).ToLower().Contains(needle) ||
                x.Id.ToString().ToLower().Contains(needle));
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var tenantIds = rows.Select(x => x.TenantId).Distinct().ToList();
        var userIds = rows.Select(x => x.CreatedByUserId).Distinct().ToList();
        var sessionIds = rows.Select(x => x.Id).ToList();

        var tenants = await db.Tenants.AsNoTracking()
            .Where(x => tenantIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var users = await db.Users.AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var latestDeliveryMeta = await db.KycWebhookDeliveries.AsNoTracking()
            .Where(x => sessionIds.Contains(x.SessionId))
            .GroupBy(x => x.SessionId)
            .Select(g => new { SessionId = g.Key, LatestAt = g.Max(x => x.CreatedAtUtc) })
            .ToListAsync(ct);

        var latestMap = new Dictionary<Guid, DateTime>();
        foreach (var item in latestDeliveryMeta)
            latestMap[item.SessionId] = item.LatestAt;

        var latestDeliveries = await db.KycWebhookDeliveries.AsNoTracking()
            .Where(x => sessionIds.Contains(x.SessionId) && latestMap.ContainsKey(x.SessionId) && x.CreatedAtUtc == latestMap[x.SessionId])
            .ToListAsync(ct);

        var deliveryMap = latestDeliveries.ToDictionary(x => x.SessionId, x => x);

        var items = rows.Select(row =>
        {
            object? result = null;
            string rawResult = string.Empty;
            if (!string.IsNullOrWhiteSpace(row.ResultJsonEncrypted))
            {
                rawResult = crypto.Decrypt(row.ResultJsonEncrypted);
                try { result = BuildPublicResult(rawResult, row.Id, includeBase64); } catch { result = new { }; }
            }

            tenants.TryGetValue(row.TenantId, out var tenant);
            users.TryGetValue(row.CreatedByUserId, out var user);
            deliveryMap.TryGetValue(row.Id, out var delivery);

            return new
            {
                sessionId = row.Id,
                tenantId = row.TenantId,
                tenantSlug = tenant?.Slug ?? string.Empty,
                tenantName = tenant?.Name ?? tenant?.Slug ?? string.Empty,
                userId = row.CreatedByUserId,
                userEmail = user?.Email ?? string.Empty,
                provider = row.ProviderCode,
                status = row.Status,
                customerRef = row.CustomerRef,
                docTypes = ParseStringList(row.RequestedDocTypesJson),
                failureReason = row.FailureReason,
                createdAtUtc = row.CreatedAtUtc,
                updatedAtUtc = row.UpdatedAtUtc,
                completedAtUtc = row.CompletedAtUtc,
                billingMetric = row.BillingMetric,
                creditsUsed = row.CreditsUsed,
                result,
                rawResultJson = includeBase64 ? rawResult : string.Empty,
                webhook = delivery is null
                    ? null
                    : new
                    {
                        delivery.Id,
                        delivery.Url,
                        delivery.Ok,
                        delivery.StatusCode,
                        delivery.DurationMs,
                        delivery.CreatedAtUtc
                    }
            };
        }).ToList();

        var payload = new
        {
            totalCount = total,
            items
        };

        // Defensive: avoid rare IIS/proxy cases returning 200 with empty body.
        var build = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "unknown";
        Response.Headers["X-Textzy-Build"] = build;
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        Response.Headers["X-Textzy-Body-Len"] = bytes.Length.ToString();
        Response.ContentType = "application/json; charset=utf-8";
        Response.ContentLength = bytes.Length;
        await Response.Body.WriteAsync(bytes, ct);
        await Response.Body.FlushAsync(ct);
        return new EmptyResult();
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        [FromQuery] bool includeBase64 = true,
        CancellationToken ct = default)
    {
        ApplyCorsHeaders();
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        var row = await db.KycSessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return NotFound();

        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == row.TenantId, ct);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == row.CreatedByUserId, ct);
        var delivery = await db.KycWebhookDeliveries.AsNoTracking()
            .Where(x => x.SessionId == row.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        object? result = null;
        string rawResult = string.Empty;
        if (!string.IsNullOrWhiteSpace(row.ResultJsonEncrypted))
        {
            rawResult = crypto.Decrypt(row.ResultJsonEncrypted);
            try { result = BuildPublicResult(rawResult, row.Id, includeBase64); } catch { result = new { }; }
        }

        return Ok(new
        {
            sessionId = row.Id,
            tenantId = row.TenantId,
            tenantSlug = tenant?.Slug ?? string.Empty,
            tenantName = tenant?.Name ?? tenant?.Slug ?? string.Empty,
            userId = row.CreatedByUserId,
            userEmail = user?.Email ?? string.Empty,
            provider = row.ProviderCode,
            status = row.Status,
            customerRef = row.CustomerRef,
            docTypes = ParseStringList(row.RequestedDocTypesJson),
            failureReason = row.FailureReason,
            createdAtUtc = row.CreatedAtUtc,
            updatedAtUtc = row.UpdatedAtUtc,
            completedAtUtc = row.CompletedAtUtc,
            billingMetric = row.BillingMetric,
            creditsUsed = row.CreditsUsed,
            result,
            rawResultJson = includeBase64 ? rawResult : string.Empty,
            webhook = delivery is null
                ? null
                : new
                {
                    delivery.Id,
                    delivery.Url,
                    delivery.Ok,
                    delivery.StatusCode,
                    delivery.DurationMs,
                    delivery.CreatedAtUtc
                }
        });
    }

    private object BuildPublicResult(string rawResultJson, Guid sessionId, bool includeBase64)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawResultJson) ? "{}" : rawResultJson);
        var root = doc.RootElement;

        var provider = GetString(root, "provider");
        if (string.Equals(provider, "gst", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                provider,
                fetchedAtUtc = GetString(root, "fetchedAtUtc"),
                gstNo = GetString(root, "gstNo"),
                error = GetBool(root, "error"),
                message = GetString(root, "message"),
                taxpayerInfo = root.TryGetProperty("taxpayerInfo", out var tp) ? JsonSerializer.Deserialize<object>(tp.GetRawText()) : null,
                filing = root.TryGetProperty("filing", out var fl) ? JsonSerializer.Deserialize<object>(fl.GetRawText()) : null,
                compliance = root.TryGetProperty("compliance", out var cp) ? JsonSerializer.Deserialize<object>(cp.GetRawText()) : null
            };
        }

        var fetchedAtUtc = GetString(root, "fetchedAtUtc");
        var requestedDocTypes = GetStringArray(root, "requestedDocTypes");
        var documentTypes = GetStringArray(root, "documentTypes");

        var collected = root.TryGetProperty("collected", out var c) && c.ValueKind == JsonValueKind.Object ? c : default;
        var name = GetString(collected, "name");
        var dob = GetString(collected, "dob");
        var gender = GetString(collected, "gender");
        var email = GetString(collected, "email");
        var mobile = GetString(collected, "mobile");
        var address = GetString(collected, "address");
        var aadhaarNumber = GetString(collected, "aadhaarNumber");
        var aadhaarVerified = GetBool(collected, "aadhaarVerified");
        var pan = GetString(collected, "pan");
        var drivingLicense = GetString(collected, "drivingLicense");

        var userDetails = root.TryGetProperty("userDetails", out var ud) && ud.ValueKind == JsonValueKind.Object ? ud : default;
        var digilockerId = GetString(userDetails, "digilockerid");

        var files = root.TryGetProperty("files", out var f) && f.ValueKind == JsonValueKind.Array ? f : default;
        var docs = BuildDocumentLinks(files, sessionId, requestedDocTypes, includeBase64);

        return new
        {
            provider = provider,
            fetchedAtUtc,
            requestedDocTypes,
            documentTypes,
            user = new
            {
                digilockerId,
                name,
                dob,
                gender,
                email,
                mobile,
                address,
                aadhaarNo = string.IsNullOrWhiteSpace(aadhaarNumber) ? string.Empty : aadhaarNumber,
                aadhaarVerified
            },
            panNo = pan,
            aadhaarNo = string.IsNullOrWhiteSpace(aadhaarNumber) ? string.Empty : aadhaarNumber,
            dlNo = drivingLicense,
            documents = docs
        };
    }

    private void ApplyCorsHeaders()
    {
        var origin = Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin)) return;
        if (!string.Equals(origin, "https://textzy.in", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(origin, "https://www.textzy.in", StringComparison.OrdinalIgnoreCase))
            return;
        Response.Headers["Access-Control-Allow-Origin"] = origin;
        Response.Headers["Access-Control-Allow-Credentials"] = "true";
        Response.Headers["Vary"] = "Origin";
        var requestedHeaders = Request.Headers["Access-Control-Request-Headers"].ToString();
        Response.Headers["Access-Control-Allow-Headers"] =
            string.IsNullOrWhiteSpace(requestedHeaders)
                ? "Authorization, X-Access-Token, X-CSRF-Token, X-Tenant-Slug, X-Requested-With, Idempotency-Key, Content-Type"
                : requestedHeaders;
        Response.Headers["Access-Control-Allow-Methods"] = "GET,OPTIONS";
        Response.Headers["Access-Control-Expose-Headers"] = "Authorization, X-Access-Token, X-CSRF-Token, X-Textzy-Build, X-Textzy-Body-Len";
    }

    private List<object> BuildDocumentLinks(JsonElement files, Guid sessionId, IReadOnlyList<string> requestedDocTypes, bool includeBase64)
    {
        var list = new List<object>();
        if (files.ValueKind != JsonValueKind.Array) return list;

        var want = MapRequestedToDoctype(requestedDocTypes);

        foreach (var f in files.EnumerateArray())
        {
            if (f.ValueKind != JsonValueKind.Object) continue;
            var uri = GetString(f, "uri");
            if (string.IsNullOrWhiteSpace(uri)) continue;
            var doctype = GetString(f, "doctype");
            if (!string.IsNullOrWhiteSpace(want))
            {
                var ok = doctype.Equals(want, StringComparison.OrdinalIgnoreCase);
                if (!ok && want.Equals("ADHAR", StringComparison.OrdinalIgnoreCase))
                    ok = doctype.Equals("AADHAAR_REPORT", StringComparison.OrdinalIgnoreCase);
                if (!ok) continue;
            }

            var name = GetString(f, "name");
            var mime = GetString(f, "mime");
            var sizeBytes = GetInt(f, "sizeBytes");
            var fileBase64 = includeBase64 ? GetString(f, "fileBase64") : string.Empty;

            list.Add(new
            {
                uri,
                doctype,
                name,
                mime,
                sizeBytes,
                fileBase64
            });
        }

        return list;
    }

    private static string MapRequestedToDoctype(IReadOnlyList<string> requestedDocTypes)
    {
        if (requestedDocTypes == null || requestedDocTypes.Count == 0) return string.Empty;
        var req = NormalizeDocType((requestedDocTypes[0] ?? string.Empty).Trim());
        if (req == "PAN") return "PANCR";
        if (req is "DL" or "DRIVING_LICENCE" or "DRIVINGLICENSE" or "DRIVING-LICENCE") return "DRVLC";
        if (req is "AADHAAR" or "AADHAR") return "ADHAR";
        return string.Empty;
    }

    private static string NormalizeDocType(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var v = value.Trim().ToUpperInvariant();
        if (v == "AADHAR") return "AADHAAR";
        if (v is "DRIVINGLICENSE" or "DRIVING_LICENCE" or "DRIVING-LICENCE") return "DL";
        return v;
    }

    private static string GetString(JsonElement element, string key)
    {
        if (element.ValueKind != JsonValueKind.Object) return string.Empty;
        if (!element.TryGetProperty(key, out var prop)) return string.Empty;
        return prop.ValueKind == JsonValueKind.String ? (prop.GetString() ?? string.Empty) : string.Empty;
    }

    private static int GetInt(JsonElement element, string key)
    {
        if (element.ValueKind != JsonValueKind.Object) return 0;
        if (!element.TryGetProperty(key, out var prop)) return 0;
        return prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var v) ? v : 0;
    }

    private static bool GetBool(JsonElement element, string key)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        if (!element.TryGetProperty(key, out var prop)) return false;
        if (prop.ValueKind == JsonValueKind.True) return true;
        if (prop.ValueKind == JsonValueKind.False) return false;
        if (prop.ValueKind == JsonValueKind.String && bool.TryParse(prop.GetString(), out var b)) return b;
        return false;
    }

    private static List<string> GetStringArray(JsonElement element, string key)
    {
        if (element.ValueKind != JsonValueKind.Object) return [];
        if (!element.TryGetProperty(key, out var prop) || prop.ValueKind != JsonValueKind.Array) return [];
        return prop.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString() ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private static List<string> ParseStringList(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; } catch { return []; }
    }
}
