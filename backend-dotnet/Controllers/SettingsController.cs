using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController(
    ControlDbContext db,
    AuthContext auth,
    TenancyContext tenancy) : ControllerBase
{
    [HttpGet("notifications")]
    public async Task<IActionResult> GetNotifications(CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        var pref = await db.UserNotificationPreferences.FirstOrDefaultAsync(x => x.UserId == auth.UserId, ct);
        if (pref is null)
        {
            pref = new UserNotificationPreference
            {
                Id = Guid.NewGuid(),
                UserId = auth.UserId,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.UserNotificationPreferences.Add(pref);
            await db.SaveChangesAsync(ct);
        }
        return Ok(Map(pref));
    }

    [HttpPut("notifications")]
    public async Task<IActionResult> UpsertNotifications([FromBody] UpsertNotificationPreferenceRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        var pref = await db.UserNotificationPreferences.FirstOrDefaultAsync(x => x.UserId == auth.UserId, ct);
        if (pref is null)
        {
            pref = new UserNotificationPreference
            {
                Id = Guid.NewGuid(),
                UserId = auth.UserId
            };
            db.UserNotificationPreferences.Add(pref);
        }

        pref.DesktopEnabled = request.DesktopEnabled;
        pref.SoundEnabled = request.SoundEnabled;
        pref.SoundStyle = NormalizeSoundStyle(request.SoundStyle);
        pref.SoundVolume = Math.Clamp(request.SoundVolume, 0m, 2m);
        pref.InAppNewMessages = request.InAppNewMessages;
        pref.InAppSystemAlerts = request.InAppSystemAlerts;
        pref.DndUntilUtc = request.DndUntilUtc;
        pref.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return Ok(Map(pref));
    }

    [HttpGet("company")]
    public async Task<IActionResult> GetCompany(CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenancy.TenantId, ct);
        if (tenant is null) return NotFound("Tenant not found.");

        var ownerGroupId = await EnsureOwnerGroupAsync(tenant, auth.UserId, ct);
        var profile = await db.TenantCompanyProfiles.FirstOrDefaultAsync(x => x.TenantId == tenant.Id, ct);
        if (profile is null)
        {
            profile = new TenantCompanyProfile
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                OwnerGroupId = ownerGroupId,
                CompanyName = tenant.Name,
                LegalName = tenant.Name,
                BillingEmail = auth.Email ?? string.Empty,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.TenantCompanyProfiles.Add(profile);
            await db.SaveChangesAsync(ct);
        }

        return Ok(Map(profile));
    }

    [HttpGet("security")]
    public async Task<IActionResult> GetSecurity(CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == auth.UserId && x.IsActive, ct);
        if (user is null) return Unauthorized();

        var recentSessions = await db.SessionTokens.AsNoTracking()
            .Where(x => x.UserId == auth.UserId && x.TenantId == tenancy.TenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(10)
            .ToListAsync(ct);

        return Ok(new
        {
            user = new
            {
                auth.UserId,
                auth.Email,
                name = auth.FullName,
                tenantId = tenancy.TenantId,
                tenantSlug = tenancy.TenantSlug,
                projectName = tenancy.TenantSlug,
                auth.Role
            },
            authenticator = new
            {
                provider = string.IsNullOrWhiteSpace(user.AuthenticatorProvider) ? "" : user.AuthenticatorProvider,
                enabled = user.AuthenticatorEnabledAtUtc.HasValue,
                enrolledAtUtc = user.AuthenticatorEnabledAtUtc
            },
            currentSession = recentSessions
                .Where(x => x.Id == auth.SessionId)
                .Select(x => MapSession(x, true))
                .FirstOrDefault(),
            recentSessions = recentSessions.Select(x => MapSession(x, x.Id == auth.SessionId)).ToList(),
            sessionRetention = new
            {
                note = "IP address, device label, and user agent are captured for web sessions. Lat/long is not collected."
            }
        });
    }

    [HttpPut("company")]
    public async Task<IActionResult> UpsertCompany([FromBody] UpsertCompanyProfileRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenancy.TenantId, ct);
        if (tenant is null) return NotFound("Tenant not found.");

        var ownerGroupId = await EnsureOwnerGroupAsync(tenant, auth.UserId, ct);
        var now = DateTime.UtcNow;
        var profile = await db.TenantCompanyProfiles.FirstOrDefaultAsync(x => x.TenantId == tenant.Id, ct);
        if (profile is null)
        {
            profile = new TenantCompanyProfile
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                CreatedAtUtc = now
            };
            db.TenantCompanyProfiles.Add(profile);
        }

        profile.OwnerGroupId = ownerGroupId;
        profile.CompanyName = (request.CompanyName ?? string.Empty).Trim();
        profile.LegalName = (request.LegalName ?? string.Empty).Trim();
        profile.Industry = (request.Industry ?? string.Empty).Trim();
        profile.Website = (request.Website ?? string.Empty).Trim();
        profile.CompanySize = (request.CompanySize ?? string.Empty).Trim();
        profile.Gstin = (request.Gstin ?? string.Empty).Trim();
        profile.Pan = (request.Pan ?? string.Empty).Trim();
        profile.Address = (request.Address ?? string.Empty).Trim();
        profile.BillingEmail = InputGuardService.ValidateEmailOrEmpty(request.BillingEmail, "Billing email");
        profile.BillingPhone = (request.BillingPhone ?? string.Empty).Trim();
        profile.TaxRatePercent = Math.Clamp(request.TaxRatePercent, 0m, 100m);
        profile.IsTaxExempt = request.IsTaxExempt;
        profile.IsReverseCharge = request.IsReverseCharge;
        profile.IsActive = request.IsActive;
        profile.UpdatedAtUtc = now;

        if (string.IsNullOrWhiteSpace(profile.CompanyName))
            return BadRequest("Company name is required.");
        if (profile.CompanyName.Length > 256)
            return BadRequest("Company name is too long.");
        if (!string.IsNullOrWhiteSpace(profile.Website) && profile.Website.Length > 512)
            return BadRequest("Website is too long.");
        if (profile.Address.Length > 2048)
            return BadRequest("Address is too long.");

        await db.SaveChangesAsync(ct);
        return Ok(Map(profile));
    }

    private async Task<Guid> EnsureOwnerGroupAsync(Tenant tenant, Guid actorUserId, CancellationToken ct)
    {
        if (tenant.OwnerGroupId.HasValue) return tenant.OwnerGroupId.Value;

        var ownerUserId = await db.TenantUsers
            .Where(x => x.TenantId == tenant.Id && x.Role.ToLower() == "owner")
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => x.UserId)
            .FirstOrDefaultAsync(ct);
        if (ownerUserId == Guid.Empty) ownerUserId = actorUserId;

        var existing = await db.TenantOwnerGroups
            .Where(g => g.OwnerUserId == ownerUserId && g.IsActive)
            .OrderBy(g => g.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (existing is null)
        {
            existing = new TenantOwnerGroup
            {
                Id = Guid.NewGuid(),
                OwnerUserId = ownerUserId,
                Name = $"{tenant.Name} Group",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.TenantOwnerGroups.Add(existing);
        }

        tenant.OwnerGroupId = existing.Id;
        await db.SaveChangesAsync(ct);
        return existing.Id;
    }

    private static object Map(TenantCompanyProfile p) => new
    {
        p.TenantId,
        p.OwnerGroupId,
        p.CompanyName,
        p.LegalName,
        p.Industry,
        p.Website,
        p.CompanySize,
        p.Gstin,
        p.Pan,
        p.Address,
        p.BillingEmail,
        p.BillingPhone,
        p.TaxRatePercent,
        p.IsTaxExempt,
        p.IsReverseCharge,
        p.IsActive,
        p.UpdatedAtUtc
    };

    private static object Map(UserNotificationPreference p) => new
    {
        p.DesktopEnabled,
        p.SoundEnabled,
        p.SoundStyle,
        p.SoundVolume,
        p.InAppNewMessages,
        p.InAppSystemAlerts,
        p.DndUntilUtc,
        p.UpdatedAtUtc
    };

    private static string NormalizeSoundStyle(string? v)
    {
        var x = (v ?? string.Empty).Trim().ToLowerInvariant();
        return x switch
        {
            "whatsapp" or "classic" or "soft" or "double" or "chime" or "off" => x,
            _ => "whatsapp"
        };
    }

    private static object MapSession(SessionToken s, bool isCurrent) => new
    {
        s.Id,
        s.CreatedAtUtc,
        s.LastSeenAtUtc,
        s.ExpiresAtUtc,
        s.RevokedAtUtc,
        s.CreatedIpAddress,
        s.LastSeenIpAddress,
        s.UserAgent,
        deviceLabel = string.IsNullOrWhiteSpace(s.DeviceLabel) ? "Unknown device" : s.DeviceLabel,
        twoFactorVerified = s.TwoFactorVerifiedAtUtc.HasValue,
        stepUpVerified = s.StepUpVerifiedAtUtc.HasValue,
        isCurrent,
        isRevoked = s.IsRevoked
    };

    public sealed class UpsertCompanyProfileRequest
    {
        public string CompanyName { get; set; } = string.Empty;
        public string LegalName { get; set; } = string.Empty;
        public string Industry { get; set; } = string.Empty;
        public string Website { get; set; } = string.Empty;
        public string CompanySize { get; set; } = string.Empty;
        public string Gstin { get; set; } = string.Empty;
        public string Pan { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string BillingEmail { get; set; } = string.Empty;
        public string BillingPhone { get; set; } = string.Empty;
        public decimal TaxRatePercent { get; set; } = 18m;
        public bool IsTaxExempt { get; set; }
        public bool IsReverseCharge { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public sealed class UpsertNotificationPreferenceRequest
    {
        public bool DesktopEnabled { get; set; } = true;
        public bool SoundEnabled { get; set; } = true;
        public string SoundStyle { get; set; } = "whatsapp";
        public decimal SoundVolume { get; set; } = 1m;
        public bool InAppNewMessages { get; set; } = true;
        public bool InAppSystemAlerts { get; set; } = true;
        public DateTime? DndUntilUtc { get; set; }
    }
}
