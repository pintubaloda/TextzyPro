using Microsoft.AspNetCore.Mvc;
using Textzy.Api.Data;
using Textzy.Api.Services;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/tenants")]
public class TenantsController(
    ControlDbContext db,
    AuthContext auth,
    RbacService rbac) : ControllerBase
{
    [HttpGet]
    public IActionResult List()
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PermissionCatalog.PlatformSettingsRead)) return Forbid();

        var tenants = db.Tenants
            .Select(t => new { t.Id, t.Name, t.Slug, t.CreatedAtUtc })
            .ToList();
        return Ok(tenants);
    }
}
