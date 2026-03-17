using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;
using Textzy.Api.Services.Kyc;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/kyc")]
public class KycController(
    ControlDbContext db,
    AuthContext auth,
    TenancyContext tenancy,
    RbacService rbac,
    BillingGuardService billingGuard,
    IntegrationCatalogBillingService integrationBilling,
    SecretCryptoService crypto,
    KycProviderRouter router,
    AuditLogService audit) : ControllerBase
{
    [HttpPost("sessions")]
    public async Task<IActionResult> CreateSession([FromBody] CreateKycSessionRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();

        // API clients use "api_client" role with ApiWrite; browser users need explicit permission.
        var isApiClient = string.Equals(auth.Role, "api_client", StringComparison.OrdinalIgnoreCase);
        if (!isApiClient && !rbac.HasPermission(ApiWrite)) return Forbid();

        var provider = string.IsNullOrWhiteSpace(request.Provider) ? "digilocker" : request.Provider.Trim().ToLowerInvariant();
        if (provider.Length > 40) return BadRequest("provider is too long.");
        if (request.DocTypes.Count > 25) return BadRequest("Too many docTypes.");
        // Billing is per docType/scope; keep sessions predictable.
        if (request.DocTypes.Count > 1) return BadRequest("Only one docType is supported per KYC session.");
        var gstNo = (request.GstNo ?? string.Empty).Trim();
        if (string.Equals(provider, "gst", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(gstNo))
            return BadRequest("gstNo is required for GST verification.");

        // Pre-check credits/limits (non-destructive). Actual consumption happens only on verified callback.
        var pluginSlug = $"{provider}-kyc";
        var billingCfg = await integrationBilling.ResolveAsync(pluginSlug, ct);
        var metricKey = string.IsNullOrWhiteSpace(billingCfg.MetricKey) ? "digilockerKyc" : billingCfg.MetricKey.Trim();
        var baseCredits = billingCfg.CreditsPerSuccess > 0 ? billingCfg.CreditsPerSuccess : 3;
        var operationCode = request.DocTypes.Count == 1 ? (request.DocTypes[0] ?? string.Empty).Trim().ToUpperInvariant() : string.Empty;
        var creditsNeeded = billingCfg.ResolveCredits(operationCode, baseCredits);

        var current = await billingGuard.GetCurrentUsageAsync(tenancy.TenantId, metricKey, ct);
        var check = await billingGuard.CheckLimitAsync(tenancy.TenantId, metricKey, current + creditsNeeded, ct);
        if (!check.Allowed)
            return StatusCode(StatusCodes.Status402PaymentRequired, new { error = check.Message, key = metricKey });

        var available = await billingGuard.GetTotalAvailableUnitsAsync(tenancy.TenantId, metricKey, ct);
        if (available != int.MaxValue && available < creditsNeeded)
        {
            return StatusCode(StatusCodes.Status402PaymentRequired, new
            {
                error = $"Insufficient credits. Need {creditsNeeded} {metricKey} units, available {available}.",
                key = metricKey
            });
        }

        var row = new KycSession
        {
            Id = Guid.NewGuid(),
            TenantId = tenancy.TenantId,
            CreatedByUserId = auth.UserId,
            ProviderCode = provider,
            Status = "created",
            CustomerRef = (request.CustomerRef ?? string.Empty).Trim(),
            GstNumber = gstNo,
            RequestedDocTypesJson = KycSession.NormalizeDocTypes(request.DocTypes),
            SuccessRedirectUrl = (request.SuccessRedirectUrl ?? string.Empty).Trim(),
            FailureRedirectUrl = (request.FailureRedirectUrl ?? string.Empty).Trim(),
            WebhookUrl = (request.WebhookUrl ?? string.Empty).Trim(),
            ResultJsonEncrypted = string.Empty,
            FailureReason = string.Empty,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        // If caller doesn't pass webhookUrl, use tenant-level default from company profile (simple mode).
        if (string.IsNullOrWhiteSpace(row.WebhookUrl))
        {
            var profile = await db.TenantCompanyProfiles.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId, ct);
            var defaultWebhook = (profile?.KycWebhookUrl ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(defaultWebhook))
                row.WebhookUrl = defaultWebhook;
        }
        db.KycSessions.Add(row);
        await db.SaveChangesAsync(ct);

        var providerImpl = router.Resolve(provider);
        var (redirectUrl, state) = await providerImpl.BuildRedirectAsync(row, ct);
        await audit.WriteAsync("kyc.session.create", $"provider={provider}; tenant={tenancy.TenantSlug}; session={row.Id}", ct);

        var payload = new
        {
            sessionId = row.Id,
            provider = row.ProviderCode,
            status = row.Status,
            redirectUrl,
            state
        };

        // Defensive: avoid rare formatter/proxy issues where a 200 response body is dropped.
        return Content(JsonSerializer.Serialize(payload), "application/json; charset=utf-8");
    }

    [HttpGet("sessions/{id:guid}")]
    public async Task<IActionResult> GetSession(Guid id, CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();

        var isApiClient = string.Equals(auth.Role, "api_client", StringComparison.OrdinalIgnoreCase);
        if (!isApiClient && !rbac.HasPermission(ApiRead)) return Forbid();

        var row = await db.KycSessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenancy.TenantId, ct);
        if (row is null) return NotFound();

        object? result = null;
        if (!string.IsNullOrWhiteSpace(row.ResultJsonEncrypted))
        {
            var raw = crypto.Decrypt(row.ResultJsonEncrypted);
            try { result = JsonSerializer.Deserialize<object>(raw); } catch { result = new { raw }; }
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

    [HttpGet("sessions")]
    public async Task<IActionResult> List([FromQuery] int take = 50, [FromQuery] bool includeParsed = false, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();
        if (!rbac.HasPermission(ApiRead)) return Forbid();
        if (take < 1) take = 1;
        if (take > 200) take = 200;

        var rows = await db.KycSessions
            .AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(take)
            .ToListAsync(ct);

        return Ok(rows.Select(x =>
        {
            object? collected = null;
            if (includeParsed && !string.IsNullOrWhiteSpace(x.ResultJsonEncrypted))
            {
                try
                {
                    var raw = crypto.Decrypt(x.ResultJsonEncrypted);
                    using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
                    if (doc.RootElement.TryGetProperty("collected", out var c) && c.ValueKind == JsonValueKind.Object)
                    {
                        collected = JsonSerializer.Deserialize<object>(c.GetRawText());
                    }
                }
                catch
                {
                    collected = null;
                }
            }

            return new
            {
                sessionId = x.Id,
                provider = x.ProviderCode,
                status = x.Status,
                customerRef = x.CustomerRef,
                docTypes = ParseStringList(x.RequestedDocTypesJson),
                failureReason = x.FailureReason,
                createdAtUtc = x.CreatedAtUtc,
                updatedAtUtc = x.UpdatedAtUtc,
                completedAtUtc = x.CompletedAtUtc,
                collected
            };
        }));
    }

    private static List<string> ParseStringList(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; } catch { return []; }
    }

    public sealed class CreateKycSessionRequest
    {
        public string Provider { get; set; } = "digilocker";
        public string CustomerRef { get; set; } = string.Empty;
        public List<string> DocTypes { get; set; } = [];
        public string GstNo { get; set; } = string.Empty;
        public string SuccessRedirectUrl { get; set; } = string.Empty;
        public string FailureRedirectUrl { get; set; } = string.Empty;
        public string WebhookUrl { get; set; } = string.Empty;
    }
}
