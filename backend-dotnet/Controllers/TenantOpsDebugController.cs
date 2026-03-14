using Microsoft.AspNetCore.Mvc;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/ops/debug")]
public sealed class TenantOpsDebugController(
    TenancyContext tenancy,
    AuthContext auth,
    RbacService rbac,
    DeliveryDebugBuffer buffer) : ControllerBase
{
    [HttpGet("delivery")]
    public IActionResult GetDelivery([FromQuery] int take = 200)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(ApiRead)) return Forbid();
        if (tenancy.TenantId == Guid.Empty) return BadRequest("Tenant context missing.");

        var items = buffer.Snapshot(tenancy.TenantId, take)
            .Select(x => new
            {
                atUtc = x.AtUtc,
                kind = x.Kind,
                correlationId = x.CorrelationId,
                durationMs = Math.Round(x.DurationMs, 1),
                detail = x.Detail
            });

        return Ok(new
        {
            tenantId = tenancy.TenantId,
            take = Math.Clamp(take, 1, 1000),
            items
        });
    }
}

