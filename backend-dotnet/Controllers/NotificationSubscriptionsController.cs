using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Services;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/notifications")]
public class NotificationSubscriptionsController(
    ControlDbContext db,
    TenancyContext tenancy,
    AuthContext auth) : ControllerBase
{
    public sealed class UpsertSubscriptionRequest
    {
        public string Provider { get; set; } = "webpush";
        public string Endpoint { get; set; } = string.Empty;
        public string P256dh { get; set; } = string.Empty;
        public string Auth { get; set; } = string.Empty;
        public string UserAgent { get; set; } = string.Empty;
    }

    [HttpPost("subscriptions")]
    public async Task<IActionResult> Upsert([FromBody] UpsertSubscriptionRequest req, CancellationToken ct)
    {
        if (auth.UserId == Guid.Empty || tenancy.TenantId == Guid.Empty) return Unauthorized();
        var provider = (req.Provider ?? "webpush").Trim().ToLowerInvariant();
        if (provider is not ("webpush" or "fcm")) provider = "webpush";
        var endpoint = (req.Endpoint ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(endpoint)) return BadRequest(new { error = "Endpoint is required." });

        var row = await db.UserPushSubscriptions
            .FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.UserId == auth.UserId && x.Endpoint == endpoint && x.Provider == provider, ct);

        if (row is null)
        {
            row = new Models.UserPushSubscription
            {
                Id = Guid.NewGuid(),
                TenantId = tenancy.TenantId,
                UserId = auth.UserId,
                Endpoint = endpoint,
                Provider = provider,
                P256dh = provider == "webpush" ? (req.P256dh ?? string.Empty).Trim() : string.Empty,
                Auth = provider == "webpush" ? (req.Auth ?? string.Empty).Trim() : string.Empty,
                UserAgent = (req.UserAgent ?? string.Empty).Trim(),
                IsActive = true,
                LastSeenAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.UserPushSubscriptions.Add(row);
        }
        else
        {
            row.Provider = provider;
            row.P256dh = provider == "webpush" ? (req.P256dh ?? string.Empty).Trim() : string.Empty;
            row.Auth = provider == "webpush" ? (req.Auth ?? string.Empty).Trim() : string.Empty;
            row.UserAgent = (req.UserAgent ?? string.Empty).Trim();
            row.IsActive = true;
            row.LastSeenAtUtc = DateTime.UtcNow;
            row.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return Ok(new { saved = true, row.Id, row.IsActive, row.UpdatedAtUtc });
    }

    [HttpDelete("subscriptions")]
    public async Task<IActionResult> Delete([FromQuery] string endpoint, CancellationToken ct)
    {
        if (auth.UserId == Guid.Empty || tenancy.TenantId == Guid.Empty) return Unauthorized();
        var target = (endpoint ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(target)) return BadRequest(new { error = "endpoint is required" });

        var row = await db.UserPushSubscriptions
            .FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.UserId == auth.UserId && x.Endpoint == target, ct);
        if (row is null) return Ok(new { removed = false });
        db.UserPushSubscriptions.Remove(row);
        await db.SaveChangesAsync(ct);
        return Ok(new { removed = true });
    }

    [HttpGet("subscriptions")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (auth.UserId == Guid.Empty || tenancy.TenantId == Guid.Empty) return Unauthorized();
        var rows = await db.UserPushSubscriptions
            .Where(x => x.TenantId == tenancy.TenantId && x.UserId == auth.UserId && x.IsActive)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.Provider,
                x.Endpoint,
                x.UserAgent,
                x.LastSeenAtUtc,
                x.UpdatedAtUtc
            })
            .ToListAsync(ct);
        return Ok(rows);
    }
}
