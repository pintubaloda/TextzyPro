using Microsoft.AspNetCore.Mvc;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/platform/ops-slo")]
public class PlatformOpsSloController(
    AuthContext auth,
    RbacService rbac,
    OpsMetricsService ops) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 7, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        var webhook = await ops.GetWebhookLagAsync(tenantId: null, days: days, ct: ct);
        return Ok(new
        {
            days = Math.Clamp(days, 1, 90),
            webhook
        });
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] int days = 7, CancellationToken ct = default)
    {
        var res = await Get(days, ct) as OkObjectResult;
        if (res?.Value is null) return BadRequest("Could not build ops SLO report.");

        dynamic v = res.Value;
        var webhook = v.webhook;

        var csv = OpsMetricsService.ToCsv(
            ("scope", "platform"),
            ("days", v.days),
            ("webhook_p95_ms", webhook.p95Ms),
            ("webhook_p99_ms", webhook.p99Ms),
            ("webhook_pending", webhook.pending),
            ("webhook_oldest_pending_sec", webhook.oldestPendingAgeSec),
            ("webhook_deadletter", webhook.deadLetter),
            ("webhook_unmapped", webhook.unmapped));

        var fileName = $"platform-ops-slo-{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", fileName);
    }
}

