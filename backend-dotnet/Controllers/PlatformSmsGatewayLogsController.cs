using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/platform/sms-gateway-logs")]
public class PlatformSmsGatewayLogsController(
    ControlDbContext db,
    RbacService rbac) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? provider,
        [FromQuery] Guid? tenantId,
        [FromQuery] bool? isSuccess,
        [FromQuery] string? recipientContains,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int limit = 200,
        CancellationToken ct = default)
    {
        if (!rbac.HasAnyRole("super_admin")) return Forbid();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        var q = db.SmsGatewayRequestLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(provider)) q = q.Where(x => x.Provider == provider);
        if (tenantId.HasValue) q = q.Where(x => x.TenantId == tenantId.Value);
        if (isSuccess.HasValue) q = q.Where(x => x.IsSuccess == isSuccess.Value);
        if (!string.IsNullOrWhiteSpace(recipientContains)) q = q.Where(x => x.Recipient.Contains(recipientContains));
        if (fromUtc.HasValue) q = q.Where(x => x.CreatedAtUtc >= fromUtc.Value);
        if (toUtc.HasValue) q = q.Where(x => x.CreatedAtUtc <= toUtc.Value);

        var rows = await q
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(x => new
            {
                x.Id,
                x.CreatedAtUtc,
                x.Provider,
                x.TenantId,
                x.Recipient,
                x.Sender,
                x.PeId,
                x.TemplateId,
                x.HttpMethod,
                x.RequestUrlMasked,
                x.RequestPayloadMasked,
                x.HttpStatusCode,
                x.ResponseBody,
                x.IsSuccess,
                x.Error,
                x.DurationMs,
                x.ProviderMessageId
            })
            .ToListAsync(ct);

        return Ok(rows);
    }
}
