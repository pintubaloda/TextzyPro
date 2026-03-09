using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/platform/request-logs")]
public class PlatformRequestLogsController(
    ControlDbContext db,
    RbacService rbac) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? tenantId,
        [FromQuery] string? method,
        [FromQuery] int? statusCode,
        [FromQuery] string? pathContains,
        [FromQuery] int limit = 200,
        CancellationToken ct = default)
    {
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        var q = db.PlatformRequestLogs.AsNoTracking().AsQueryable();
        if (tenantId.HasValue) q = q.Where(x => x.TenantId == tenantId.Value);
        if (!string.IsNullOrWhiteSpace(method)) q = q.Where(x => x.Method == method);
        if (statusCode.HasValue) q = q.Where(x => x.StatusCode == statusCode.Value);
        if (!string.IsNullOrWhiteSpace(pathContains)) q = q.Where(x => x.Path.Contains(pathContains));

        var rows = await q
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(x => new
            {
                x.Id,
                x.CreatedAtUtc,
                x.RequestId,
                x.Method,
                x.Path,
                x.QueryString,
                x.StatusCode,
                x.DurationMs,
                x.TenantId,
                x.UserId,
                x.ClientIp,
                x.UserAgent,
                x.RequestBody,
                x.ResponseBody,
                x.Error
            })
            .ToListAsync(ct);

        return Ok(rows);
    }
}
