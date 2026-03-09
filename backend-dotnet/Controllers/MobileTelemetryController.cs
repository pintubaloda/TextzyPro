using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
public class MobileTelemetryController(
    ControlDbContext db,
    AuthContext auth,
    RbacService rbac) : ControllerBase
{
    [HttpPost("api/mobile/telemetry")]
    public async Task<IActionResult> Ingest([FromBody] TelemetryIngestRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.EventType)) return BadRequest("eventType is required.");

        Guid? deviceId = null;
        if (!string.IsNullOrWhiteSpace(request.InstallId))
        {
            var hash = HashToken(request.InstallId.Trim().ToLowerInvariant());
            deviceId = await db.UserMobileDevices
                .Where(x => x.UserId == auth.UserId && x.TenantId == auth.TenantId && x.InstallIdHash == hash && x.RevokedAtUtc == null)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(ct);
        }

        db.MobileTelemetryEvents.Add(new MobileTelemetryEvent
        {
            Id = Guid.NewGuid(),
            TenantId = auth.TenantId,
            UserId = auth.UserId,
            DeviceId = deviceId,
            EventType = request.EventType.Trim(),
            DataJson = string.IsNullOrWhiteSpace(request.DataJson) ? "{}" : request.DataJson.Trim(),
            EventAtUtc = request.EventAtUtc ?? DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return Ok(new { accepted = true });
    }

    [HttpGet("api/platform/mobile-telemetry")]
    public async Task<IActionResult> ListForPlatform([FromQuery] int take = 200, [FromQuery] int days = 1, CancellationToken ct = default)
    {
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();
        var safeTake = Math.Clamp(take, 20, 1000);
        var safeDays = Math.Clamp(days, 1, 30);
        var since = DateTime.UtcNow.AddDays(-safeDays);

        var rows = await db.MobileTelemetryEvents
            .Where(x => x.EventAtUtc >= since)
            .OrderByDescending(x => x.EventAtUtc)
            .Take(safeTake)
            .Join(db.Users, e => e.UserId, u => u.Id, (e, u) => new
            {
                e.Id,
                e.TenantId,
                userEmail = u.Email,
                e.DeviceId,
                e.EventType,
                e.DataJson,
                e.EventAtUtc
            })
            .ToListAsync(ct);

        return Ok(rows);
    }

    private static string HashToken(string token)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    public sealed class TelemetryIngestRequest
    {
        public string EventType { get; set; } = string.Empty;
        public string DataJson { get; set; } = "{}";
        public string InstallId { get; set; } = string.Empty;
        public DateTime? EventAtUtc { get; set; }
    }
}
