using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/ops/slo")]
public class TenantOpsSloController(
    ControlDbContext controlDb,
    TenancyContext tenancy,
    AuthContext auth,
    RbacService rbac,
    OpsMetricsService ops) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 7, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(ApiRead)) return Forbid();
        if (tenancy.TenantId == Guid.Empty) return BadRequest("Tenant context missing.");

        var tenant = await controlDb.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenancy.TenantId, ct);
        if (tenant is null) return NotFound("Tenant not found.");

        using var tenantDb = SeedData.CreateTenantDbContext(tenant.DataConnectionString);
        var webhook = await ops.GetWebhookLagAsync(tenancy.TenantId, days, ct);
        var outbound = await ops.GetOutboundLatencyAsync(tenantDb, tenancy.TenantId, days, ct);

        return Ok(new
        {
            tenantId = tenancy.TenantId,
            days = Math.Clamp(days, 1, 365),
            webhook,
            outbound
        });
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] int days = 7, CancellationToken ct = default)
    {
        var res = await Get(days, ct) as OkObjectResult;
        if (res?.Value is null) return BadRequest("Could not build SLO report.");

        // Simple 1-row CSV export for spreadsheets.
        dynamic v = res.Value;
        var webhook = v.webhook;
        var outbound = v.outbound;

        var csv = OpsMetricsService.ToCsv(
            ("tenantId", v.tenantId),
            ("days", v.days),
            ("webhook_p95_ms", webhook.p95Ms),
            ("webhook_p99_ms", webhook.p99Ms),
            ("webhook_pending", webhook.pending),
            ("webhook_oldest_pending_sec", webhook.oldestPendingAgeSec),
            ("outbound_p95_ms", outbound.p95Ms),
            ("outbound_p99_ms", outbound.p99Ms),
            ("outbound_queued", outbound.queuedCount),
            ("outbound_oldest_queued_sec", outbound.oldestQueuedAgeSec),
            ("outbound_samples", outbound.samples));

        var fileName = $"tenant-ops-slo-{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", fileName);
    }
}

