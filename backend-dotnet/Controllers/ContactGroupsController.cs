using Microsoft.AspNetCore.Mvc;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/contact-groups")]
public class ContactGroupsController(TenantDbContext db, TenancyContext tenancy, RbacService rbac) : ControllerBase
{
    [HttpGet]
    public IActionResult List()
    {
        if (!rbac.HasPermission(ContactsRead)) return Forbid();
        return Ok(db.ContactGroups.Where(x => x.TenantId == tenancy.TenantId).OrderBy(x => x.Name).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ContactGroup request, CancellationToken ct)
    {
        if (!rbac.HasPermission(ContactsWrite)) return Forbid();
        string name;
        try
        {
            name = InputGuardService.RequireTrimmed(request.Name, "Group name", 120);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        var item = new ContactGroup { Id = Guid.NewGuid(), TenantId = tenancy.TenantId, Name = name };
        db.ContactGroups.Add(item);
        await db.SaveChangesAsync(ct);
        return Ok(item);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!rbac.HasPermission(ContactsWrite)) return Forbid();
        var item = db.ContactGroups.FirstOrDefault(x => x.Id == id && x.TenantId == tenancy.TenantId);
        if (item is null) return NotFound();
        db.ContactGroups.Remove(item);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
