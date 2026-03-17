using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;
using Textzy.Api.Services.Kyc;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/public/kyc")]
public class PublicKycSessionsController(
    ControlDbContext controlDb,
    SecretCryptoService crypto,
    BillingGuardService billingGuard,
    IntegrationCatalogBillingService integrationBilling,
    KycProviderRouter router,
    IHttpClientFactory httpClientFactory,
    ILogger<PublicKycSessionsController> logger) : ControllerBase
{
    // NOTE: On some IIS setups, requestFiltering denyStrings may block paths containing "create"
    // (common SQL-injection blocklists). Keep /create for backward compatibility, but provide
    // /start alias to avoid false positives.

    [HttpGet("sessions/create")]
    public async Task<IActionResult> CreateByQuery(CancellationToken ct)
    {
        var q = Request.Query;
        var req = new PublicKycCreateRequest
        {
            TenantSlug = q["tenantSlug"].FirstOrDefault() ?? string.Empty,
            User = q["user"].FirstOrDefault() ?? string.Empty,
            Password = q["pswd"].FirstOrDefault() ?? q["password"].FirstOrDefault() ?? string.Empty,
            ApiKey = q["apikey"].FirstOrDefault() ?? q["apiKey"].FirstOrDefault() ?? string.Empty,
            Provider = q["provider"].FirstOrDefault() ?? "digilocker",
            CustomerRef = q["customerRef"].FirstOrDefault() ?? q["ref"].FirstOrDefault() ?? string.Empty,
            DocType = q["docType"].FirstOrDefault() ?? q["doctype"].FirstOrDefault() ?? string.Empty,
            GstNo = q["gstNo"].FirstOrDefault() ?? q["gst"].FirstOrDefault() ?? string.Empty,
            SuccessRedirectUrl = q["successRedirectUrl"].FirstOrDefault() ?? string.Empty,
            FailureRedirectUrl = q["failureRedirectUrl"].FirstOrDefault() ?? string.Empty,
            WebhookUrl = q["webhookUrl"].FirstOrDefault() ?? string.Empty,
        };
        return await CreateCore(req, ct);
    }

    [HttpGet("sessions/start")]
    public async Task<IActionResult> StartByQuery(CancellationToken ct)
        => await CreateByQuery(ct);

    [HttpPost("sessions/create")]
    public async Task<IActionResult> CreateByPost([FromBody] PublicKycCreateRequest request, CancellationToken ct)
        => await CreateCore(request, ct);

    [HttpPost("sessions/start")]
    public async Task<IActionResult> StartByPost([FromBody] PublicKycCreateRequest request, CancellationToken ct)
        => await CreateCore(request, ct);

    // SMS-style "simple API": POST /api/public/kyc/sessions
    [HttpPost("sessions")]
    public async Task<IActionResult> CreateSession([FromBody] PublicKycCreateRequest request, CancellationToken ct)
        => await CreateCore(request, ct);

    [HttpGet("sessions/{id:guid}")]
    public async Task<IActionResult> GetSession(Guid id, [FromQuery] string tenantSlug, [FromQuery] string user, [FromQuery] string pswd, [FromQuery] string apikey, [FromQuery] bool includeBase64, CancellationToken ct)
    {
        var auth = new PublicKycAuthRequest
        {
            TenantSlug = tenantSlug,
            User = user,
            Password = pswd,
            ApiKey = apikey
        };
        var (tenantId, _, errorResult) = await ValidatePublicAuthAsync(auth, ct);
        if (tenantId == Guid.Empty) return errorResult!;

        var row = await controlDb.KycSessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
        if (row is null) return NotFound("KYC session not found.");

        object? result = null;
        if (!string.IsNullOrWhiteSpace(row.ResultJsonEncrypted))
        {
            var raw = crypto.Decrypt(row.ResultJsonEncrypted);
            try
            {
                result = BuildPublicResult(raw, id, includeBase64);
            }
            catch
            {
                result = new { };
            }
        }

        return Ok(new
        {
            sessionId = row.Id,
            provider = row.ProviderCode,
            status = row.Status,
            customerRef = row.CustomerRef,
            docTypes = ParseStringList(row.RequestedDocTypesJson),
            failureReason = row.FailureReason,
            createdAtUtc = row.CreatedAtUtc,
            updatedAtUtc = row.UpdatedAtUtc,
            completedAtUtc = row.CompletedAtUtc,
            result
        });
    }

    // Download a DigiLocker-issued file (PDF) that was attached into the session result.
    // Caller must provide the same public API credentials as GetSession.
    //
    // Example:
    // GET /api/public/kyc/sessions/{sessionId}/file?tenantSlug=...&user=...&pswd=...&apikey=...&uri=in.gov.pan-PANCR-XXXX
    [HttpGet("sessions/{id:guid}/file")]
    public async Task<IActionResult> GetSessionFile(
        Guid id,
        [FromQuery] string tenantSlug,
        [FromQuery] string user,
        [FromQuery] string pswd,
        [FromQuery] string apikey,
        [FromQuery] string uri,
        [FromQuery] bool includeBase64,
        CancellationToken ct)
    {
        var auth = new PublicKycAuthRequest
        {
            TenantSlug = tenantSlug,
            User = user,
            Password = pswd,
            ApiKey = apikey
        };
        var (tenantId, _, errorResult) = await ValidatePublicAuthAsync(auth, ct);
        if (tenantId == Guid.Empty) return errorResult!;

        if (string.IsNullOrWhiteSpace(uri)) return BadRequest("uri is required.");

        var row = await controlDb.KycSessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
        if (row is null) return NotFound("KYC session not found.");

        if (string.IsNullOrWhiteSpace(row.ResultJsonEncrypted))
            return NotFound("No KYC result recorded for this session.");

        var raw = crypto.Decrypt(row.ResultJsonEncrypted);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        if (!doc.RootElement.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            return NotFound("No files recorded for this session.");

        JsonElement? match = null;
        foreach (var f in files.EnumerateArray())
        {
            if (f.ValueKind != JsonValueKind.Object) continue;
            if (!f.TryGetProperty("uri", out var u) || u.ValueKind != JsonValueKind.String) continue;
            if (!string.Equals(u.GetString(), uri, StringComparison.OrdinalIgnoreCase)) continue;
            match = f;
            break;
        }

        if (match is null) return NotFound("File not found in this session result.");

        var fileBase64 = match.Value.TryGetProperty("fileBase64", out var fb) && fb.ValueKind == JsonValueKind.String ? fb.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(fileBase64)) return NotFound("File content is missing for this session.");

        var mime = match.Value.TryGetProperty("mime", out var mm) && mm.ValueKind == JsonValueKind.String
            ? (mm.GetString() ?? "application/octet-stream")
            : "application/octet-stream";

        var name = match.Value.TryGetProperty("name", out var nn) && nn.ValueKind == JsonValueKind.String
            ? (nn.GetString() ?? string.Empty).Trim()
            : string.Empty;

        var sizeBytes = match.Value.TryGetProperty("sizeBytes", out var sb) && sb.ValueKind == JsonValueKind.Number && sb.TryGetInt32(out var sz)
            ? sz
            : 0;

        if (includeBase64)
        {
            return Ok(new
            {
                uri,
                name,
                mime,
                sizeBytes,
                fileBase64
            });
        }

        byte[] bytes;
        try { bytes = Convert.FromBase64String(fileBase64); }
        catch { return StatusCode(StatusCodes.Status500InternalServerError, "Stored file content is corrupt."); }

        // Safe fallback file name for browsers.
        var fileName = !string.IsNullOrWhiteSpace(name) ? name : uri;
        foreach (var c in Path.GetInvalidFileNameChars()) fileName = fileName.Replace(c, '_');
        if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && mime.Contains("pdf", StringComparison.OrdinalIgnoreCase))
            fileName += ".pdf";

        return File(bytes, mime, fileName);
    }

    private async Task<IActionResult> CreateCore(PublicKycCreateRequest request, CancellationToken ct)
    {
        var (tenantId, profile, errorResult) = await ValidatePublicAuthAsync(request, ct);
        if (tenantId == Guid.Empty) return errorResult!;

        var provider = (request.Provider ?? "digilocker").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(provider)) provider = "digilocker";
        if (provider.Length > 40) return BadRequest("provider is too long.");

        var docType = NormalizeDocType((request.DocType ?? string.Empty).Trim());
        if (string.IsNullOrWhiteSpace(docType) && string.Equals(provider, "gst", StringComparison.OrdinalIgnoreCase))
            docType = "GST";
        if (string.IsNullOrWhiteSpace(docType)) return BadRequest("docType is required.");
        if (docType.Length > 64) return BadRequest("docType is too long.");
        var gstNo = (request.GstNo ?? string.Empty).Trim();
        if (string.Equals(provider, "gst", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(gstNo))
            return BadRequest("gstNo is required for GST verification.");

        var allowed = await LoadAllowedDocTypesAsync(ct);
        if (allowed.Count > 0 && !allowed.Contains(docType))
            return BadRequest($"docType '{docType}' is not allowed. Allowed: {string.Join(", ", allowed)}");

        // Credit pre-check.
        var pluginSlug = $"{provider}-kyc";
        var billingCfg = await integrationBilling.ResolveAsync(pluginSlug, ct);
        var metricKey = string.IsNullOrWhiteSpace(billingCfg.MetricKey) ? "digilockerKyc" : billingCfg.MetricKey.Trim();
        var baseCredits = billingCfg.CreditsPerSuccess > 0 ? billingCfg.CreditsPerSuccess : 3;
        var creditsNeeded = billingCfg.ResolveCredits(docType, baseCredits);

        var current = await billingGuard.GetCurrentUsageAsync(tenantId, metricKey, ct);
        var check = await billingGuard.CheckLimitAsync(tenantId, metricKey, current + creditsNeeded, ct);
        if (!check.Allowed)
            return StatusCode(StatusCodes.Status402PaymentRequired, new { error = check.Message, key = metricKey });

        var available = await billingGuard.GetTotalAvailableUnitsAsync(tenantId, metricKey, ct);
        if (available != int.MaxValue && available < creditsNeeded)
        {
            return StatusCode(StatusCodes.Status402PaymentRequired, new
            {
                error = $"Insufficient credits. Need {creditsNeeded} {metricKey} units, available {available}.",
                key = metricKey
            });
        }

        var webhookUrl = (request.WebhookUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            var defaultWebhook = (profile?.KycWebhookUrl ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(defaultWebhook)) webhookUrl = defaultWebhook;
        }

        var row = new KycSession
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CreatedByUserId = Guid.Empty,
            ProviderCode = provider,
            Status = "created",
            CustomerRef = (request.CustomerRef ?? string.Empty).Trim(),
            GstNumber = gstNo,
            RequestedDocTypesJson = JsonSerializer.Serialize(new[] { docType.ToLowerInvariant() }),
            SuccessRedirectUrl = (request.SuccessRedirectUrl ?? string.Empty).Trim(),
            FailureRedirectUrl = (request.FailureRedirectUrl ?? string.Empty).Trim(),
            WebhookUrl = webhookUrl,
            ResultJsonEncrypted = string.Empty,
            FailureReason = string.Empty,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        controlDb.KycSessions.Add(row);
        await controlDb.SaveChangesAsync(ct);

        var providerImpl = router.Resolve(provider);
        var (redirectUrl, state) = await providerImpl.BuildRedirectAsync(row, ct);

        if (string.Equals(provider, "gst", StringComparison.OrdinalIgnoreCase))
        {
            var latest = await controlDb.KycSessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == row.Id, ct);
            if (latest is not null)
            {
                row = latest;
                if (string.Equals(row.Status, "verified", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var gstPluginSlug = $"{provider}-kyc";
                        var gstBillingCfg = await integrationBilling.ResolveAsync(gstPluginSlug, ct);
                        var gstMetricKey = string.IsNullOrWhiteSpace(gstBillingCfg.MetricKey) ? "digilockerKyc" : gstBillingCfg.MetricKey.Trim();
                        var gstBaseCredits = gstBillingCfg.CreditsPerSuccess > 0 ? gstBillingCfg.CreditsPerSuccess : 1;
                        var gstCredits = gstBillingCfg.ResolveCredits(docType, gstBaseCredits);
                        await billingGuard.TryConsumeAsync(row.TenantId, gstMetricKey, gstCredits, ct);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to consume gst KYC credits for tenant {TenantId} session {SessionId}", row.TenantId, row.Id);
                    }
                }
                await TryWebhookAsync(row, ok: string.Equals(row.Status, "verified", StringComparison.OrdinalIgnoreCase), ct);
            }
        }
        var payload = new
        {
            sessionId = row.Id,
            provider = row.ProviderCode,
            status = row.Status,
            docType,
            redirectUrl,
            state
        };

        // Some IIS/proxy setups have been observed returning 200 with an empty body when using ObjectResult/formatters.
        // Write JSON explicitly to ensure non-empty body + stable content-type.
        return Content(JsonSerializer.Serialize(payload), "application/json; charset=utf-8");
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
            var downloadUrl = BuildPublicDownloadUrl(sessionId, uri);

            if (includeBase64)
            {
                var fileBase64 = GetString(f, "fileBase64");
                list.Add(new
                {
                    uri,
                    doctype,
                    name,
                    mime,
                    sizeBytes,
                    downloadUrl,
                    fileBase64
                });
            }
            else
            {
                list.Add(new
                {
                    uri,
                    doctype,
                    name,
                    mime,
                    sizeBytes,
                    downloadUrl
                });
            }
        }

        return list;
    }

    private string BuildPublicDownloadUrl(Guid sessionId, string uri)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var encodedUri = Uri.EscapeDataString(uri);
        return $"{baseUrl}/api/public/kyc/sessions/{sessionId}/file?uri={encodedUri}";
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

    private static string NormalizeDocType(string raw)
    {
        var s = (raw ?? string.Empty).Trim().ToUpperInvariant();
        if (s == "AADHAR") return "AADHAAR";
        if (s is "DRIVINGLICENSE" or "DRIVING_LICENCE" or "DRIVING-LICENCE") return "DL";
        return s;
    }

    private async Task<HashSet<string>> LoadAllowedDocTypesAsync(CancellationToken ct)
    {
        try
        {
            var encrypted = await controlDb.PlatformSettings.AsNoTracking()
                .Where(x => x.Scope == "kyc" && x.Key == "allowedDocTypes")
                .Select(x => x.ValueEncrypted)
                .FirstOrDefaultAsync(ct);

            if (string.IsNullOrWhiteSpace(encrypted)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var raw = crypto.Decrypt(encrypted);
            var parts = raw.Split(new[] { ',', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => NormalizeDocType(x))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new HashSet<string>(parts, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string GetString(JsonElement obj, string key)
    {
        if (obj.ValueKind != JsonValueKind.Object) return string.Empty;
        if (!obj.TryGetProperty(key, out var v)) return string.Empty;
        return v.ValueKind == JsonValueKind.String ? (v.GetString() ?? string.Empty) : v.ToString();
    }

    private static bool GetBool(JsonElement obj, string key)
    {
        if (obj.ValueKind != JsonValueKind.Object) return false;
        if (!obj.TryGetProperty(key, out var v)) return false;
        if (v.ValueKind == JsonValueKind.True) return true;
        if (v.ValueKind == JsonValueKind.False) return false;
        if (v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString();
            return string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || s == "1" || s == "Y";
        }
        return false;
    }

    private static int GetInt(JsonElement obj, string key)
    {
        if (obj.ValueKind != JsonValueKind.Object) return 0;
        if (!obj.TryGetProperty(key, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var j)) return j;
        return 0;
    }

    private static List<string> GetStringArray(JsonElement obj, string key)
    {
        var list = new List<string>();
        if (obj.ValueKind != JsonValueKind.Object) return list;
        if (!obj.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Array) return list;
        foreach (var it in v.EnumerateArray())
        {
            if (it.ValueKind == JsonValueKind.String)
            {
                var s = it.GetString();
                if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
            }
        }
        return list;
    }

    private async Task<(Guid TenantId, TenantCompanyProfile? Profile, IActionResult? Error)> ValidatePublicAuthAsync(PublicKycAuthRequest request, CancellationToken ct)
    {
        if (!IsHttpsRequest(HttpContext))
            return (Guid.Empty, null, StatusCode(StatusCodes.Status403Forbidden, "HTTPS is required."));

        var tenantSlug = (request.TenantSlug ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(tenantSlug))
            return (Guid.Empty, null, BadRequest("tenantSlug is required."));

        var tenant = await controlDb.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == tenantSlug, ct);
        if (tenant is null)
            return (Guid.Empty, null, NotFound("Tenant not found."));

        var profile = await controlDb.TenantCompanyProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant.Id, ct);
        if (profile is null || !profile.PublicApiEnabled)
            return (Guid.Empty, null, StatusCode(StatusCodes.Status403Forbidden, "Public API integration is disabled."));

        var expectedUser = (profile.ApiUsername ?? string.Empty).Trim();
        var expectedPassword = crypto.Decrypt(profile.ApiPasswordEncrypted).Trim();
        var expectedApiKey = crypto.Decrypt(profile.ApiKeyEncrypted).Trim();
        if (string.IsNullOrWhiteSpace(expectedUser) || string.IsNullOrWhiteSpace(expectedPassword) || string.IsNullOrWhiteSpace(expectedApiKey))
            return (Guid.Empty, null, StatusCode(StatusCodes.Status503ServiceUnavailable, "Public API credentials are not configured."));

        var providedUser = (request.User ?? string.Empty).Trim();
        var providedPassword = (request.Password ?? string.Empty).Trim();
        var providedApiKey = (request.ApiKey ?? string.Empty).Trim();
        if (!SecureEquals(providedUser, expectedUser) ||
            !SecureEquals(providedPassword, expectedPassword) ||
            !SecureEquals(providedApiKey, expectedApiKey))
        {
            return (Guid.Empty, null, Unauthorized("Invalid API credentials."));
        }

        if (!IsIpAllowed(HttpContext.Connection.RemoteIpAddress, profile.ApiIpWhitelist ?? string.Empty))
            return (Guid.Empty, null, StatusCode(StatusCodes.Status403Forbidden, "Client IP not allowed."));

        return (tenant.Id, profile, null);
    }

    private static bool IsHttpsRequest(HttpContext context)
    {
        if (context.Request.IsHttps) return true;
        var xfProto = context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault();
        return string.Equals(xfProto, "https", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SecureEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left ?? string.Empty);
        var b = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static bool IsIpAllowed(IPAddress? remoteIp, string rawWhitelist)
    {
        if (string.IsNullOrWhiteSpace(rawWhitelist)) return true;
        if (remoteIp is null) return false;

        var ip = remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4() : remoteIp;
        var rules = rawWhitelist.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule)) continue;
            if (string.Equals(rule, "*", StringComparison.Ordinal)) return true;
            if (TryMatchCidr(ip, rule)) return true;

            if (IPAddress.TryParse(rule, out var allowed))
            {
                var normalizedAllowed = allowed.IsIPv4MappedToIPv6 ? allowed.MapToIPv4() : allowed;
                if (normalizedAllowed.Equals(ip)) return true;
            }
        }

        return false;
    }

    private static bool TryMatchCidr(IPAddress ip, string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2) return false;
        if (!IPAddress.TryParse(parts[0], out var baseIp)) return false;
        if (!int.TryParse(parts[1], out var prefixLength)) return false;

        var ipBytes = (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).GetAddressBytes();
        var baseBytes = (baseIp.IsIPv4MappedToIPv6 ? baseIp.MapToIPv4() : baseIp).GetAddressBytes();
        if (ipBytes.Length != baseBytes.Length) return false;

        var bits = ipBytes.Length * 8;
        if (prefixLength < 0 || prefixLength > bits) return false;

        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var i = 0; i < fullBytes; i++)
        {
            if (ipBytes[i] != baseBytes[i]) return false;
        }

        if (remainingBits == 0) return true;
        var mask = (byte)(0xFF << (8 - remainingBits));
        return (ipBytes[fullBytes] & mask) == (baseBytes[fullBytes] & mask);
    }

    private static List<string> ParseStringList(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch { return []; }
    }

    public class PublicKycAuthRequest
    {
        public string TenantSlug { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
    }

    public class PublicKycCreateRequest : PublicKycAuthRequest
    {
        public string Provider { get; set; } = "digilocker";
        public string CustomerRef { get; set; } = string.Empty;
        public string DocType { get; set; } = string.Empty;
        public string GstNo { get; set; } = string.Empty;
        public string SuccessRedirectUrl { get; set; } = string.Empty;
        public string FailureRedirectUrl { get; set; } = string.Empty;
        public string WebhookUrl { get; set; } = string.Empty;
    }

    private async Task TryWebhookAsync(Textzy.Api.Models.KycSession session, bool ok, CancellationToken ct)
    {
        var url = (session.WebhookUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            object? result = null;
            if (!string.IsNullOrWhiteSpace(session.ResultJsonEncrypted))
            {
                var raw = crypto.Decrypt(session.ResultJsonEncrypted);
                try { result = JsonSerializer.Deserialize<object>(raw); } catch { result = new { raw }; }
            }

            List<string> requestedDocTypes;
            try { requestedDocTypes = JsonSerializer.Deserialize<List<string>>(session.RequestedDocTypesJson) ?? []; }
            catch { requestedDocTypes = []; }

            var payload = new
            {
                sessionId = session.Id,
                tenantId = session.TenantId,
                provider = session.ProviderCode,
                status = session.Status,
                ok,
                customerRef = session.CustomerRef,
                requestedDocTypes,
                failureReason = session.FailureReason,
                completedAtUtc = session.CompletedAtUtc,
                result
            };
            var payloadJson = JsonSerializer.Serialize(payload);

            var http = httpClientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("Textzy", "1.0"));
            req.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var res = await http.SendAsync(req, ct);
            var responseBody = await res.Content.ReadAsStringAsync(ct);
            sw.Stop();

            controlDb.KycWebhookDeliveries.Add(new KycWebhookDelivery
            {
                Id = Guid.NewGuid(),
                CreatedAtUtc = DateTime.UtcNow,
                TenantId = session.TenantId,
                SessionId = session.Id,
                Provider = (session.ProviderCode ?? string.Empty).Trim().ToLowerInvariant(),
                Url = url,
                Ok = res.IsSuccessStatusCode,
                StatusCode = (int)res.StatusCode,
                DurationMs = (int)Math.Clamp(sw.ElapsedMilliseconds, 0, int.MaxValue),
                RequestJson = payloadJson.Length > 20000 ? payloadJson[..20000] : payloadJson,
                ResponseBody = string.IsNullOrWhiteSpace(responseBody) ? string.Empty : (responseBody.Length > 4000 ? responseBody[..4000] : responseBody),
                Error = string.Empty
            });
            await controlDb.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "KYC webhook failed for session {SessionId}", session.Id);
            try
            {
                controlDb.KycWebhookDeliveries.Add(new KycWebhookDelivery
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTime.UtcNow,
                    TenantId = session.TenantId,
                    SessionId = session.Id,
                    Provider = (session.ProviderCode ?? string.Empty).Trim().ToLowerInvariant(),
                    Url = url,
                    Ok = false,
                    StatusCode = 0,
                    DurationMs = 0,
                    RequestJson = string.Empty,
                    ResponseBody = string.Empty,
                    Error = ex.Message.Length > 1500 ? ex.Message[..1500] : ex.Message
                });
                await controlDb.SaveChangesAsync(ct);
            }
            catch
            {
                // ignore logging failures
            }
        }
    }
}
