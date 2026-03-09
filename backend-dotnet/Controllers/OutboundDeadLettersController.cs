using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/outbound/deadletters")]
public class OutboundDeadLettersController(
    ControlDbContext controlDb,
    TenancyContext tenancy,
    AuthContext auth,
    RbacService rbac) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int limit = 100, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(ApiRead)) return Forbid();
        if (tenancy.TenantId == Guid.Empty) return BadRequest("Tenant context missing.");
        limit = Math.Clamp(limit, 1, 500);

        var tenant = await controlDb.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenancy.TenantId, ct);
        if (tenant is null) return NotFound("Tenant not found.");
        using var db = SeedData.CreateTenantDbContext(tenant.DataConnectionString);

        var items = await db.OutboundDeadLetters
            .AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(ct);

        return Ok(items);
    }
}
