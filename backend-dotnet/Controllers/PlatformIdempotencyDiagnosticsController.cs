using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/platform/idempotency-diagnostics")]
public class PlatformIdempotencyDiagnosticsController(
    ControlDbContext controlDb,
    AuthContext auth,
    RbacService rbac) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid tenantId,
        [FromQuery] string status = "",
        [FromQuery] int staleMinutes = 30,
        [FromQuery] int limit = 300,
        CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();
        if (tenantId == Guid.Empty) return BadRequest("tenantId is required.");

        limit = Math.Clamp(limit, 1, 1000);
        staleMinutes = Math.Clamp(staleMinutes, 1, 24 * 60);
        var staleBefore = DateTime.UtcNow.AddMinutes(-staleMinutes);
        var now = DateTime.UtcNow;

        var tenant = await controlDb.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId, ct);
        if (tenant is null) return NotFound("Tenant not found.");

        using var db = SeedData.CreateTenantDbContext(tenant.DataConnectionString);
        var query = db.IdempotencyKeys.Where(x => x.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);

        var rows = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(limit)
            .Select(x => new
            {
                x.Id,
                x.TenantId,
                tenantSlug = tenant.Slug,
                x.Key,
                x.MessageId,
                x.Status,
                x.CreatedAtUtc,
                x.ExpiresAtUtc,
                stale = x.Status == "reserved" && (x.ExpiresAtUtc < now || x.CreatedAtUtc < staleBefore)
            })
            .ToListAsync(ct);

        var summary = await db.IdempotencyKeys
            .Where(x => x.TenantId == tenantId)
            .GroupBy(x => x.Status)
            .Select(g => new { status = g.Key, count = g.Count() })
            .ToListAsync(ct);

        var staleReserved = await db.IdempotencyKeys
            .CountAsync(x => x.TenantId == tenantId && x.Status == "reserved" && (x.ExpiresAtUtc < now || x.CreatedAtUtc < staleBefore), ct);

        return Ok(new
        {
            tenantId,
            tenantSlug = tenant.Slug,
            staleMinutes,
            staleReserved,
            summary = summary.ToDictionary(x => x.status ?? "unknown", x => x.count),
            items = rows
        });
    }
}
