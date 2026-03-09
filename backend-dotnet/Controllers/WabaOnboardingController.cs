using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.DTOs;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/waba")]
public class WabaOnboardingController(
    WhatsAppCloudService whatsapp,
    RbacService rbac,
    ControlDbContext db,
    SecretCryptoService crypto,
    AuthContext auth) : ControllerBase
{
    [HttpGet("embedded-config")]
    public async Task<IActionResult> EmbeddedConfig(CancellationToken ct)
    {
        if (!rbac.HasPermission(InboxRead)) return Forbid();

        var rows = await db.PlatformSettings
            .AsNoTracking()
            .Where(x => x.Scope == "waba-master")
            .ToListAsync(ct);

        var values = rows.ToDictionary(x => x.Key, x => crypto.Decrypt(x.ValueEncrypted), StringComparer.OrdinalIgnoreCase);
        var appId = values.TryGetValue("appId", out var aid) ? (aid ?? string.Empty).Trim() : string.Empty;
        var embeddedConfigId =
            values.TryGetValue("embeddedConfigId", out var ecid) ? (ecid ?? string.Empty).Trim()
            : values.TryGetValue("configId", out var cid) ? (cid ?? string.Empty).Trim()
            : string.Empty;

        return Ok(new
        {
            appId,
            embeddedConfigId,
            available = !string.IsNullOrWhiteSpace(appId) && !string.IsNullOrWhiteSpace(embeddedConfigId)
        });
    }

    [HttpPost("onboarding/start")]
    public async Task<IActionResult> Start(CancellationToken ct)
    {
        if (!rbac.HasPermission(InboxWrite)) return Forbid();
        var cfg = await whatsapp.StartOnboardingAsync(ct);
        return Ok(new { state = cfg.OnboardingState, startedAtUtc = cfg.OnboardingStartedAtUtc });
    }

    [HttpGet("onboarding/status")]
    public async Task<IActionResult> OnboardingStatus(CancellationToken ct)
    {
        if (!rbac.HasPermission(InboxRead)) return Forbid();
        return Ok(await whatsapp.GetOnboardingStatusAsync(ct));
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        if (!rbac.HasPermission(InboxRead)) return Forbid();
        var payload = await whatsapp.GetOnboardingStatusAsync(ct);

        return Ok(payload);
    }

    [HttpPost("embedded-signup/exchange")]
    public async Task<IActionResult> Exchange([FromBody] WabaExchangeCodeRequest request, CancellationToken ct)
    {
        if (!rbac.HasPermission(InboxWrite)) return Forbid();

        try
        {
            var cfg = await whatsapp.ExchangeEmbeddedCodeAsync(request.Code, ct);
            return Ok(new
            {
                cfg.WabaId,
                cfg.PhoneNumberId,
                cfg.BusinessAccountName,
                cfg.DisplayPhoneNumber,
                cfg.IsActive,
                cfg.ConnectedAtUtc
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("onboarding/recheck")]
    public async Task<IActionResult> Recheck(CancellationToken ct)
    {
        if (!rbac.HasPermission(InboxWrite)) return Forbid();
        return Ok(await whatsapp.GetOnboardingStatusAsync(ct));
    }

    [HttpPost("onboarding/disconnect")]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        if (!rbac.HasPermission(InboxWrite)) return Forbid();
        try
        {
            var cfg = await whatsapp.DisconnectTenantWabaAsync("Disconnected by tenant admin.", ct);
            return Ok(new { ok = true, cfg.TenantId, cfg.OnboardingState, cfg.IsActive });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("onboarding/reuse-existing")]
    public async Task<IActionResult> ReuseExisting([FromBody] WabaReuseExistingRequest request, CancellationToken ct)
    {
        if (!rbac.HasPermission(InboxWrite)) return Forbid();
        try
        {
            var result = await whatsapp.ReuseExistingFromCodeAsync(request.Code, request.WabaId, request.PhoneNumberId, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("onboarding/map-existing")]
    public async Task<IActionResult> MapExisting([FromBody] WabaMapExistingRequest request, CancellationToken ct)
    {
        if (!rbac.HasPermission(InboxWrite)) return Forbid();

        var targetTenantId = request.TenantId ?? auth.TenantId;
        if (targetTenantId == Guid.Empty) return BadRequest("Project is required.");

        var isSuperAdmin = await db.Users
            .Where(u => u.Id == auth.UserId)
            .Select(u => u.IsSuperAdmin)
            .FirstOrDefaultAsync(ct);
        if (!isSuperAdmin)
        {
            var hasMembership = await db.TenantUsers
                .AnyAsync(x => x.UserId == auth.UserId && x.TenantId == targetTenantId, ct);
            if (!hasMembership) return Forbid();
        }

        try
        {
            var result = await whatsapp.MapExistingWabaAsync(
                targetTenantId,
                request.WabaId,
                request.PhoneNumberId,
                request.AccessToken,
                ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
