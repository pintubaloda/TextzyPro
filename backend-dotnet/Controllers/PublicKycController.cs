using System.Net.Http.Headers;
using System.Diagnostics;
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
        [FromQuery] string sessionId,
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken ct)
    {
        if (!Guid.TryParse((sessionId ?? string.Empty).Trim(), out var id))
            return BadRequest("Invalid sessionId.");
        var row = await db.KycSessions.FirstOrDefaultAsync(x => x.Id == id, ct);
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
        await db.SaveChangesAsync(ct);

        if (result.Ok)
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
                await billingGuard.TryConsumeAsync(row.TenantId, metricKey, credits, ct);
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

}
