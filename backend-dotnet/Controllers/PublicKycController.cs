using System.Net.Http.Headers;
using System.Diagnostics;
using System.Text;
using System.Linq;
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
public class PublicKycController(
    ControlDbContext db,
    KycProviderRouter router,
    BillingGuardService billingGuard,
    IntegrationCatalogBillingService integrationBilling,
    SecretCryptoService crypto,
    IHttpClientFactory httpClientFactory,
    ILogger<PublicKycController> logger) : ControllerBase
{
    // Example:
    // GET /api/public/kyc/digilocker/callback?sessionId=...&code=...&state=...
    [HttpGet("{provider}/callback")]
    public async Task<IActionResult> Callback(
        string provider,
        [FromQuery] string? sessionId,
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken ct)
    {
        var resolvedId = ResolveSessionIdFromQueryOrState(sessionId, state);
        if (resolvedId == Guid.Empty)
            return BadRequest("Invalid sessionId.");

        var row = await db.KycSessions.FirstOrDefaultAsync(x => x.Id == resolvedId, ct);
        if (row is null) return NotFound("KYC session not found.");
        if (!string.Equals(row.ProviderCode, provider ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            return BadRequest("Provider mismatch.");

        // Provider error (user denied consent, etc.)
        if (!string.IsNullOrWhiteSpace(error))
        {
            row.Status = "failed";
            row.FailureReason = $"provider_error:{error}";
            row.CompletedAtUtc = DateTime.UtcNow;
            row.UpdatedAtUtc = DateTime.UtcNow;
            await ReverseConsumedCreditsIfNeededAsync(row, ct);
            await db.SaveChangesAsync(ct);
            await TryWebhookAsync(row, ok: false, ct);
            return RedirectToOutcome(row, ok: false);
        }

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return BadRequest("Missing code/state.");

        var providerImpl = router.Resolve(provider ?? string.Empty);
        var result = await providerImpl.HandleCallbackAsync(row, code.Trim(), state.Trim(), ct);

        row.Status = result.Status;
        row.FailureReason = result.FailureReason ?? string.Empty;
        row.ResultJsonEncrypted = string.IsNullOrWhiteSpace(result.ResultJson) ? string.Empty : crypto.Encrypt(result.ResultJson);
        row.CompletedAtUtc = DateTime.UtcNow;
        row.UpdatedAtUtc = DateTime.UtcNow;
        if (!result.Ok && string.Equals(row.Status, "failed", StringComparison.OrdinalIgnoreCase))
            await ReverseConsumedCreditsIfNeededAsync(row, ct);
        await db.SaveChangesAsync(ct);

        if (result.Ok && row.CreditsUsed <= 0)
        {
            // Bill credits only on successful verification.
            try
            {
                var pluginSlug = $"{provider}-kyc";
                var billingCfg = await integrationBilling.ResolveAsync(pluginSlug, ct);
                var metricKey = string.IsNullOrWhiteSpace(billingCfg.MetricKey) ? "digilockerKyc" : billingCfg.MetricKey.Trim();
                var baseCredits = billingCfg.CreditsPerSuccess > 0 ? billingCfg.CreditsPerSuccess : 3;
                var operationCode = string.Empty;
                try
                {
                    var list = JsonSerializer.Deserialize<List<string>>(row.RequestedDocTypesJson) ?? [];
                    if (list.Count > 0) operationCode = (list[0] ?? string.Empty).Trim().ToUpperInvariant();
                }
                catch { }
                var credits = billingCfg.ResolveCredits(operationCode, baseCredits);
                var consume = await billingGuard.TryConsumeAsync(row.TenantId, metricKey, credits, ct);
                if (consume.Allowed)
                {
                    row.BillingMetric = metricKey;
                    row.CreditsUsed = credits;
                    row.UpdatedAtUtc = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (Exception ex)
            {
                // Billing should not break the user flow; log and continue.
                logger.LogError(ex, "Failed to consume digilockerKyc for tenant {TenantId} session {SessionId}", row.TenantId, row.Id);
            }
        }

        await TryWebhookAsync(row, ok: result.Ok, ct);
        return RedirectToOutcome(row, ok: result.Ok);
    }

    private async Task ReverseConsumedCreditsIfNeededAsync(KycSession row, CancellationToken ct)
    {
        if (row.CreditsUsed <= 0 || string.IsNullOrWhiteSpace(row.BillingMetric))
            return;

        try
        {
            await billingGuard.AddCreditUnitsAsync(
                row.TenantId,
                row.BillingMetric,
                row.CreditsUsed,
                ct,
                source: "public.kyc.callback",
                service: $"{row.ProviderCode}-kyc",
                referenceId: row.Id.ToString(),
                status: "refunded");
            row.CreditsUsed = 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refund KYC credits for tenant {TenantId} session {SessionId}", row.TenantId, row.Id);
        }
    }

    private static Guid ResolveSessionIdFromQueryOrState(string? sessionId, string? state)
    {
        if (Guid.TryParse((sessionId ?? string.Empty).Trim(), out var fromQuery))
            return fromQuery;

        // v1.<guidN>.<random>
        var raw = (state ?? string.Empty).Trim();
        if (raw.StartsWith("v1.", StringComparison.OrdinalIgnoreCase))
        {
            var parts = raw.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 2)
            {
                var guidN = parts[1];
                if (guidN.Length == 32 && Guid.TryParseExact(guidN, "N", out var fromState))
                    return fromState;
            }
        }
        return Guid.Empty;
    }

    private IActionResult RedirectToOutcome(Textzy.Api.Models.KycSession session, bool ok)
    {
        var raw = ok ? session.SuccessRedirectUrl : session.FailureRedirectUrl;
        var target = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            // Default: show a minimal response for integrations that don't provide redirect URLs.
            return Content(ok ? "KYC verified." : "KYC failed.", "text/plain; charset=utf-8");
        }

        // Append sessionId and status for clients.
        var sep = target.Contains('?') ? "&" : "?";
        return Redirect(target + sep + "sessionId=" + Uri.EscapeDataString(session.Id.ToString()) + "&status=" + Uri.EscapeDataString(session.Status));
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
                try { result = BuildPublicResult(raw, session.Id); } catch { result = new { }; }
            }

            var payload = new
            {
                sessionId = session.Id,
                provider = session.ProviderCode,
                status = session.Status,
                customerRef = session.CustomerRef,
                docTypes = ParseStringList(session.RequestedDocTypesJson),
                failureReason = session.FailureReason,
                createdAtUtc = session.CreatedAtUtc,
                updatedAtUtc = session.UpdatedAtUtc,
                completedAtUtc = session.CompletedAtUtc,
                result
            };
            var payloadJson = JsonSerializer.Serialize(payload);

            var http = httpClientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Textzy", "1.0"));
            req.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

            var sw = Stopwatch.StartNew();
            using var res = await http.SendAsync(req, ct);
            var responseBody = await res.Content.ReadAsStringAsync(ct);
            sw.Stop();

            db.KycWebhookDeliveries.Add(new KycWebhookDelivery
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
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "KYC webhook failed for session {SessionId}", session.Id);
            try
            {
                db.KycWebhookDeliveries.Add(new KycWebhookDelivery
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
                await db.SaveChangesAsync(ct);
            }
            catch
            {
                // ignore logging failures
            }
        }
    }

    private object BuildPublicResult(string rawResultJson, Guid sessionId)
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
        var docs = BuildDocumentLinks(files, sessionId, requestedDocTypes);

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

    private List<object> BuildDocumentLinks(JsonElement files, Guid sessionId, IReadOnlyList<string> requestedDocTypes)
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
            var fileBase64 = GetString(f, "fileBase64");

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
