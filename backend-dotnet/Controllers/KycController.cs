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

        // Pre-check credits/limits (non-destructive). Actual consumption happens on verified callback.
        var current = await billingGuard.GetCurrentUsageAsync(tenancy.TenantId, "digilockerKyc", ct);
        var check = await billingGuard.CheckLimitAsync(tenancy.TenantId, "digilockerKyc", current + 1, ct);
        if (!check.Allowed)
            return StatusCode(StatusCodes.Status402PaymentRequired, new { error = check.Message, key = "digilockerKyc" });

        var row = new KycSession
        {
            Id = Guid.NewGuid(),
            TenantId = tenancy.TenantId,
            CreatedByUserId = auth.UserId,
            ProviderCode = provider,
            Status = "created",
            CustomerRef = (request.CustomerRef ?? string.Empty).Trim(),
            RequestedDocTypesJson = KycSession.NormalizeDocTypes(request.DocTypes),
            SuccessRedirectUrl = (request.SuccessRedirectUrl ?? string.Empty).Trim(),
            FailureRedirectUrl = (request.FailureRedirectUrl ?? string.Empty).Trim(),
            WebhookUrl = (request.WebhookUrl ?? string.Empty).Trim(),
            ResultJsonEncrypted = string.Empty,
            FailureReason = string.Empty,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.KycSessions.Add(row);
        await db.SaveChangesAsync(ct);

        var providerImpl = router.Resolve(provider);
        var (redirectUrl, state) = await providerImpl.BuildRedirectAsync(row, ct);
        await audit.WriteAsync("kyc.session.create", $"provider={provider}; tenant={tenancy.TenantSlug}; session={row.Id}", ct);

        return Ok(new
        {
            sessionId = row.Id,
            provider = row.ProviderCode,
            status = row.Status,
            redirectUrl,
            state
        });
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
    public async Task<IActionResult> List([FromQuery] int take = 50, CancellationToken ct = default)
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

        return Ok(rows.Select(x => new
        {
            sessionId = x.Id,
            provider = x.ProviderCode,
            status = x.Status,
            customerRef = x.CustomerRef,
            docTypes = ParseStringList(x.RequestedDocTypesJson),
            failureReason = x.FailureReason,
            createdAtUtc = x.CreatedAtUtc,
            updatedAtUtc = x.UpdatedAtUtc,
            completedAtUtc = x.CompletedAtUtc
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
        public string SuccessRedirectUrl { get; set; } = string.Empty;
        public string FailureRedirectUrl { get; set; } = string.Empty;
        public string WebhookUrl { get; set; } = string.Empty;
    }
}

