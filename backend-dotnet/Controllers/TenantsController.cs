using Microsoft.AspNetCore.Mvc;
using Textzy.Api.Data;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/tenants")]
public class TenantsController(ControlDbContext db) : ControllerBase
{
    [HttpGet]
    public IActionResult List()
    {
        var tenants = db.Tenants
            .Select(t => new { t.Id, t.Name, t.Slug, t.CreatedAtUtc })
            .ToList();
        return Ok(tenants);
    }
}
