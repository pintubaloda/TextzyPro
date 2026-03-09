using Microsoft.AspNetCore.Mvc;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/sms/inputs")]
public class SmsInputsController(TenantDbContext db, TenancyContext tenancy, RbacService rbac) : ControllerBase
{
    [HttpGet]
    public IActionResult List()
    {
        if (!rbac.HasPermission(AutomationRead)) return Forbid();
        return Ok(db.SmsInputFields.Where(x => x.TenantId == tenancy.TenantId).OrderBy(x => x.Name).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SmsInputField request, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationWrite)) return Forbid();
        string name;
        string type;
        try
        {
            name = InputGuardService.RequireTrimmed(request.Name, "Name", 120);
            type = InputGuardService.RequireTrimmed(request.Type, "Type", 60).ToLowerInvariant();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        var item = new SmsInputField { Id = Guid.NewGuid(), TenantId = tenancy.TenantId, Name = name, Type = type };
        db.SmsInputFields.Add(item);
        await db.SaveChangesAsync(ct);
        return Ok(item);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SmsInputField request, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationWrite)) return Forbid();
        string name;
        string type;
        try
        {
            name = InputGuardService.RequireTrimmed(request.Name, "Name", 120);
            type = InputGuardService.RequireTrimmed(request.Type, "Type", 60).ToLowerInvariant();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        var item = db.SmsInputFields.FirstOrDefault(x => x.Id == id && x.TenantId == tenancy.TenantId);
        if (item is null) return NotFound();
        item.Name = name;
        item.Type = type;
        await db.SaveChangesAsync(ct);
        return Ok(item);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationWrite)) return Forbid();
        var item = db.SmsInputFields.FirstOrDefault(x => x.Id == id && x.TenantId == tenancy.TenantId);
        if (item is null) return NotFound();
        db.SmsInputFields.Remove(item);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
