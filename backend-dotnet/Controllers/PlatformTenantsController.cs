using Microsoft.AspNetCore.Mvc;
using Textzy.Api.DTOs;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/platform/tenants")]
public class PlatformTenantsController(
    TenantProvisioningService provisioning,
    AuthContext auth,
    RbacService rbac) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTenantRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformTenantsManage)) return Forbid();

        try
        {
            var tenant = await provisioning.ProvisionAsync(auth.UserId, request.Name, request.Slug, ct);
            return Ok(new { tenant.Id, tenant.Name, tenant.Slug });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
