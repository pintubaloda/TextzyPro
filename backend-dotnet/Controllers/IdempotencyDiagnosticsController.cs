using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/idempotency/diagnostics")]
public class IdempotencyDiagnosticsController(
    ControlDbContext controlDb,
    TenancyContext tenancy,
    AuthContext auth,
    RbacService rbac) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string status = "",
        [FromQuery] int staleMinutes = 30,
        [FromQuery] int limit = 200,
        CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(ApiRead)) return Forbid();
        if (tenancy.TenantId == Guid.Empty) return BadRequest("Tenant context missing.");

        limit = Math.Clamp(limit, 1, 1000);
        staleMinutes = Math.Clamp(staleMinutes, 1, 24 * 60);
        var staleBefore = DateTime.UtcNow.AddMinutes(-staleMinutes);
        var now = DateTime.UtcNow;

        var tenant = await controlDb.Tenants.FirstOrDefaultAsync(x => x.Id == tenancy.TenantId, ct);
        if (tenant is null) return NotFound("Tenant not found.");

        using var db = SeedData.CreateTenantDbContext(tenant.DataConnectionString);
        var query = db.IdempotencyKeys.Where(x => x.TenantId == tenancy.TenantId);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);

        var rows = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(limit)
            .Select(x => new
            {
                x.Id,
                x.Key,
                x.MessageId,
                x.Status,
                x.CreatedAtUtc,
                x.ExpiresAtUtc,
                stale = x.Status == "reserved" && (x.ExpiresAtUtc < now || x.CreatedAtUtc < staleBefore)
            })
            .ToListAsync(ct);

        var summary = await db.IdempotencyKeys
            .Where(x => x.TenantId == tenancy.TenantId)
            .GroupBy(x => x.Status)
            .Select(g => new { status = g.Key, count = g.Count() })
            .ToListAsync(ct);

        var staleReserved = await db.IdempotencyKeys
            .CountAsync(x => x.TenantId == tenancy.TenantId && x.Status == "reserved" && (x.ExpiresAtUtc < now || x.CreatedAtUtc < staleBefore), ct);

        return Ok(new
        {
            tenantId = tenancy.TenantId,
            staleMinutes,
            staleReserved,
            summary = summary.ToDictionary(x => x.status ?? "unknown", x => x.count),
            items = rows
        });
    }
}
