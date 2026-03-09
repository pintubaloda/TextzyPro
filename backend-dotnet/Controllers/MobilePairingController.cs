using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/public/mobile/pair")]
public class MobilePairingController(
    ControlDbContext db,
    SessionService sessions,
    AuthCookieService authCookie,
    SecretCryptoService crypto) : ControllerBase
{
    [HttpPost("exchange")]
    public async Task<IActionResult> Exchange([FromBody] PairExchangeRequest request, CancellationToken ct)
    {
        if (!IsSecureRequest()) return StatusCode(StatusCodes.Status400BadRequest, "HTTPS is required.");

        var token = (request.PairingToken ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest("Pairing token is required.");
        var installId = (request.InstallId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(installId))
            return BadRequest("Install ID is required.");

        var now = DateTime.UtcNow;
        var tokenHash = HashToken(token);
        var pair = await db.MobilePairingRequests
            .Where(x => x.PairingTokenHash == tokenHash)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (pair is null || pair.ConsumedAtUtc is not null || pair.ExpiresAtUtc <= now)
            return BadRequest("Pairing code expired or invalid.");

        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == pair.UserId && x.IsActive, ct);
        if (user is null) return BadRequest("User is not active.");

        var tenant = await db.Tenants.FirstOrDefaultAsync(x => x.Id == pair.TenantId, ct);
        if (tenant is null) return BadRequest("Tenant not found.");

        var settings = await ReadMobileAppSettingsAsync(ct);
        var maxDevicesPerUser = ParseInt(GetSetting(settings, "maxDevicesPerUser", "3"), 3, 1, 20);

        var activeDeviceCount = await db.UserMobileDevices
            .CountAsync(x => x.UserId == pair.UserId && x.TenantId == pair.TenantId && x.RevokedAtUtc == null, ct);
        if (activeDeviceCount >= maxDevicesPerUser)
            return BadRequest($"Device limit reached ({maxDevicesPerUser}). Remove a device first.");

        var installIdHash = HashToken(installId.ToLowerInvariant());
        var device = await db.UserMobileDevices
            .FirstOrDefaultAsync(x =>
                x.UserId == pair.UserId &&
                x.TenantId == pair.TenantId &&
                x.InstallIdHash == installIdHash, ct);

        var metadataJson = JsonSerializer.Serialize(new
        {
            request.DevicePlatform,
            request.DeviceName,
            request.DeviceModel,
            request.AppVersion,
            request.OsVersion,
            ip = GetClientIp(),
            forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault() ?? string.Empty,
            location = (request.LocationLat.HasValue && request.LocationLng.HasValue)
                ? new
                {
                    lat = request.LocationLat.Value,
                    lng = request.LocationLng.Value,
                    accuracyMeters = request.LocationAccuracyMeters,
                    capturedAtUtc = request.LocationCapturedAtUtc
                }
                : null
        });

        if (device is null)
        {
            device = new UserMobileDevice
            {
                Id = Guid.NewGuid(),
                UserId = pair.UserId,
                TenantId = pair.TenantId,
                DeviceName = InputOrDefault(request.DeviceName, "Mobile App"),
                Platform = InputOrDefault(request.DevicePlatform, "unknown"),
                AppVersion = InputOrDefault(request.AppVersion, string.Empty),
                InstallIdHash = installIdHash,
                MetadataJson = metadataJson,
                CreatedAtUtc = now,
                LastSeenAtUtc = now,
                RevokedAtUtc = null
            };
            db.UserMobileDevices.Add(device);
        }
        else
        {
            device.DeviceName = InputOrDefault(request.DeviceName, device.DeviceName);
            device.Platform = InputOrDefault(request.DevicePlatform, device.Platform);
            device.AppVersion = InputOrDefault(request.AppVersion, device.AppVersion);
            device.MetadataJson = metadataJson;
            device.LastSeenAtUtc = now;
            device.RevokedAtUtc = null;
        }

        pair.ConsumedAtUtc = now;
        pair.ConsumedDeviceId = device.Id;

        var membership = await db.TenantUsers
            .FirstOrDefaultAsync(x => x.UserId == pair.UserId && x.TenantId == pair.TenantId, ct);
        var role = user.IsSuperAdmin ? "super_admin" : (membership?.Role ?? "agent");
        string accessToken;
        try
        {
            accessToken = await sessions.CreateSessionAsync(pair.UserId, pair.TenantId, ct);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
        }
        authCookie.SetToken(HttpContext, accessToken);
        var csrf = authCookie.EnsureCsrfToken(HttpContext);

        Response.Headers["Authorization"] = $"Bearer {accessToken}";
        Response.Headers["X-Access-Token"] = accessToken;
        Response.Headers["X-CSRF-Token"] = csrf;

        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            accessToken,
            csrfToken = csrf,
            tenantSlug = tenant.Slug,
            role,
            user = new
            {
                id = user.Id,
                email = user.Email,
                fullName = user.FullName
            },
            device = new
            {
                id = device.Id,
                device.DeviceName,
                device.Platform,
                device.AppVersion
            }
        });
    }

    private static string InputOrDefault(string? value, string fallback)
    {
        var clean = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
    }

    private async Task<Dictionary<string, string>> ReadMobileAppSettingsAsync(CancellationToken ct)
    {
        var entries = await db.PlatformSettings
            .Where(x => x.Scope == "mobile-app")
            .ToListAsync(ct);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
            values[e.Key] = crypto.Decrypt(e.ValueEncrypted);
        return values;
    }

    private static int ParseInt(string raw, int fallback, int min, int max)
    {
        if (!int.TryParse(raw, out var value)) value = fallback;
        if (value < min) value = min;
        if (value > max) value = max;
        return value;
    }

    private static string GetSetting(Dictionary<string, string> values, string key, string fallback)
    {
        return values.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw)
            ? raw.Trim()
            : fallback;
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private string GetClientIp()
    {
        var forwarded = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            return forwarded.Split(',')[0].Trim();
        }
        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
    }

    private bool IsSecureRequest()
    {
        if (Request.IsHttps) return true;
        if (string.Equals(Request.Headers["X-Forwarded-Proto"].FirstOrDefault(), "https", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    public sealed class PairExchangeRequest
    {
        public string PairingToken { get; set; } = string.Empty;
        public string InstallId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string DevicePlatform { get; set; } = string.Empty;
        public string DeviceModel { get; set; } = string.Empty;
        public string OsVersion { get; set; } = string.Empty;
        public string AppVersion { get; set; } = string.Empty;
        public double? LocationLat { get; set; }
        public double? LocationLng { get; set; }
        public double? LocationAccuracyMeters { get; set; }
        public DateTime? LocationCapturedAtUtc { get; set; }
    }
}
