using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/ops/productivity")]
public class TenantProductivityController(
    ControlDbContext controlDb,
    TenancyContext tenancy,
    AuthContext auth,
    RbacService rbac,
    OpsMetricsService ops) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 30, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(ApiRead)) return Forbid();
        if (tenancy.TenantId == Guid.Empty) return BadRequest("Tenant context missing.");

        var tenant = await controlDb.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenancy.TenantId, ct);
        if (tenant is null) return NotFound("Tenant not found.");

        using var tenantDb = SeedData.CreateTenantDbContext(tenant.DataConnectionString);
        var metrics = await ops.GetTenantProductivityAsync(tenantDb, tenancy.TenantId, days, ct);
        return Ok(metrics);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] int days = 30, CancellationToken ct = default)
    {
        var res = await Get(days, ct) as OkObjectResult;
        if (res?.Value is null) return BadRequest("Could not build productivity report.");
        dynamic v = res.Value;

        // Flattened 1-row CSV export.
        var csv = OpsMetricsService.ToCsv(
            ("tenantId", tenancy.TenantId),
            ("days", v.days),
            ("optouts_new", v.optOutsNew),
            ("optouts_active", v.optOutsActive),
            ("broadcast_jobs", v.broadcastJobs),
            ("broadcast_completed", v.broadcastCompleted),
            ("broadcast_failed", v.broadcastFailed),
            ("broadcast_sent", v.broadcastSent),
            ("broadcast_failed_count", v.broadcastFailedCount),
            ("broadcast_avg_duration_sec", v.broadcastAvgDurationSec),
            ("automation_runs", v.automationRuns),
            ("automation_completed", v.automationCompleted),
            ("automation_failed", v.automationFailed),
            ("automation_avg_duration_sec", v.automationAvgDurationSec));

        var fileName = $"tenant-productivity-{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", fileName);
    }
}

