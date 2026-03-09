using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/platform/waba-error-policies")]
public class PlatformWabaErrorPoliciesController(
    ControlDbContext db,
    AuthContext auth,
    RbacService rbac,
    AuditLogService audit) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        var rows = await db.WabaErrorPolicies
            .OrderBy(x => x.Code)
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] UpsertPolicyRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsWrite)) return Forbid();

        var code = (request.Code ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(code)) return BadRequest("Code is required.");
        var classification = (request.Classification ?? string.Empty).Trim().ToLowerInvariant();
        if (classification is not ("retryable" or "permanent")) return BadRequest("Classification must be retryable or permanent.");

        var row = await db.WabaErrorPolicies.FirstOrDefaultAsync(x => x.Code == code, ct);
        if (row is null)
        {
            row = new WabaErrorPolicy
            {
                Id = Guid.NewGuid(),
                Code = code
            };
            db.WabaErrorPolicies.Add(row);
        }

        row.Classification = classification;
        row.Description = (request.Description ?? string.Empty).Trim();
        row.IsActive = request.IsActive;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("platform.waba_error_policy.upsert", $"code={code}; class={classification}; active={row.IsActive}", ct);
        return Ok(row);
    }

    [HttpDelete("{code}")]
    public async Task<IActionResult> Deactivate(string code, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsWrite)) return Forbid();

        var key = (code ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key)) return BadRequest("Code is required.");
        var row = await db.WabaErrorPolicies.FirstOrDefaultAsync(x => x.Code == key, ct);
        if (row is null) return NotFound();
        row.IsActive = false;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("platform.waba_error_policy.deactivate", $"code={key}", ct);
        return NoContent();
    }

    public sealed class UpsertPolicyRequest
    {
        public string Code { get; set; } = string.Empty;
        public string Classification { get; set; } = "permanent";
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }
}

