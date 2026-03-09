using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/platform/security")]
public class PlatformSecurityController(
    ControlDbContext db,
    AuthContext auth,
    RbacService rbac,
    SecurityControlService controls,
    OutboundMessageQueueService outboundQueue,
    WabaWebhookQueueService webhookQueue,
    AuditLogService audit) : ControllerBase
{
    [HttpGet("signals")]
    public async Task<IActionResult> Signals([FromQuery] string status = "open", [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();
        limit = Math.Clamp(limit, 1, 500);

        var q = db.SecuritySignals.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
            q = q.Where(x => x.Status == status);

        var rows = await q
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost("signals/{id:guid}/resolve")]
    public async Task<IActionResult> ResolveSignal(Guid id, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsWrite)) return Forbid();
        var row = await db.SecuritySignals.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return NotFound();
        row.Status = "resolved";
        row.ResolvedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    [HttpGet("controls")]
    public async Task<IActionResult> Controls([FromQuery] Guid tenantId, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();
        if (tenantId == Guid.Empty) return BadRequest("tenantId is required.");
        var row = await controls.GetTenantControlAsync(tenantId, ct);
        if (row is null)
            return Ok(new { tenantId, circuitBreakerEnabled = false, ratePerMinuteOverride = 0, reason = "" });

        return Ok(new
        {
            tenantId = row.TenantId,
            circuitBreakerEnabled = row.CircuitBreakerEnabled,
            ratePerMinuteOverride = row.RatePerMinuteOverride,
            reason = row.Reason ?? string.Empty
        });
    }

    [HttpPut("controls")]
    public async Task<IActionResult> UpsertControls([FromBody] ControlRequest request, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsWrite)) return Forbid();
        if (request.TenantId == Guid.Empty) return BadRequest("tenantId is required.");
        await controls.UpsertControlAsync(request.TenantId, request.CircuitBreakerEnabled, request.RatePerMinuteOverride, auth.UserId, request.Reason ?? string.Empty, ct);
        await audit.WriteAsync("platform.security.controls.updated", $"tenantId={request.TenantId}; circuit={request.CircuitBreakerEnabled}; rpm={request.RatePerMinuteOverride}", ct);
        return Ok(new { ok = true });
    }

    [HttpPost("queue/purge")]
    public async Task<IActionResult> PurgeQueue([FromBody] PurgeQueueRequest request, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsWrite)) return Forbid();
        var target = (request.Queue ?? string.Empty).Trim().ToLowerInvariant();
        if (target == "outbound")
            await outboundQueue.PurgeAsync(ct);
        else if (target == "webhook")
            await webhookQueue.PurgeAsync(ct);
        else
            return BadRequest("queue must be outbound or webhook.");

        await audit.WriteAsync("platform.security.queue.purged", $"queue={target}", ct);
        return Ok(new { ok = true, queue = target });
    }

    public sealed class ControlRequest
    {
        public Guid TenantId { get; set; }
        public bool? CircuitBreakerEnabled { get; set; }
        public int? RatePerMinuteOverride { get; set; }
        public string? Reason { get; set; }
    }

    public sealed class PurgeQueueRequest
    {
        public string Queue { get; set; } = string.Empty;
    }
}
