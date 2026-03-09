using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/platform/outbound-deadletters")]
public class PlatformOutboundDeadLettersController(
    ControlDbContext controlDb,
    AuthContext auth,
    RbacService rbac) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid tenantId, [FromQuery] int limit = 200, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();
        if (tenantId == Guid.Empty) return BadRequest("tenantId is required.");
        limit = Math.Clamp(limit, 1, 1000);

        var tenant = await controlDb.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId, ct);
        if (tenant is null) return NotFound("Tenant not found.");

        using var db = SeedData.CreateTenantDbContext(tenant.DataConnectionString);
        var items = await db.OutboundDeadLetters
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(ct);

        return Ok(new { tenantId, tenantSlug = tenant.Slug, items });
    }
}
