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
    KycProviderRouter router) : ControllerBase
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
    public async Task<IActionResult> GetSession(Guid id, [FromQuery] string tenantSlug, [FromQuery] string user, [FromQuery] string pswd, [FromQuery] string apikey, CancellationToken ct)
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

    private async Task<IActionResult> CreateCore(PublicKycCreateRequest request, CancellationToken ct)
    {
        var (tenantId, profile, errorResult) = await ValidatePublicAuthAsync(request, ct);
        if (tenantId == Guid.Empty) return errorResult!;

        var provider = (request.Provider ?? "digilocker").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(provider)) provider = "digilocker";
        if (provider.Length > 40) return BadRequest("provider is too long.");

        var docType = (request.DocType ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(docType)) return BadRequest("docType is required.");
        if (docType.Length > 64) return BadRequest("docType is too long.");

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
        return Ok(new
        {
            sessionId = row.Id,
            provider = row.ProviderCode,
            status = row.Status,
            docType,
            redirectUrl,
            state
        });
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
        public string SuccessRedirectUrl { get; set; } = string.Empty;
        public string FailureRedirectUrl { get; set; } = string.Empty;
        public string WebhookUrl { get; set; } = string.Empty;
    }
}
