using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/template-lifecycle")]
public class TemplateLifecycleController(
    TenantDbContext db,
    TenancyContext tenancy,
    AuthContext auth,
    RbacService rbac,
    WhatsAppCloudService whatsapp) : ControllerBase
{
    private Guid CurrentTenantId => tenancy.IsSet ? tenancy.TenantId : auth.TenantId;

    [HttpPost("sync")]
    public async Task<IActionResult> Sync([FromQuery] bool rebuild = false, CancellationToken ct = default)
    {
        if (!rbac.HasPermission(TemplatesWrite)) return Forbid();
        try
        {
            var purged = 0;
            if (rebuild)
            {
                purged = await whatsapp.PurgeTenantWhatsAppTemplatesAsync(ct);
            }
            var result = await whatsapp.SyncMessageTemplatesAsync(deepSync: true, ct);
            return Ok(new
            {
                rebuild,
                purged,
                result
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/submit")]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct)
    {
        if (!rbac.HasPermission(TemplatesWrite)) return Forbid();
        var t = await db.Templates.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == CurrentTenantId, ct);
        if (t is null) return NotFound();
        if (t.Channel == ChannelType.WhatsApp)
        {
            try
            {
                var result = await whatsapp.SubmitTemplateForApprovalAsync(id, ct);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        t.LifecycleStatus = "submitted";
        t.Status = "Pending";
        await db.SaveChangesAsync(ct);
        return Ok(t);
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        if (!rbac.HasPermission(TemplatesWrite)) return Forbid();
        var t = await db.Templates.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == CurrentTenantId, ct);
        if (t is null) return NotFound();
        if (t.Channel == ChannelType.WhatsApp)
            return BadRequest("WhatsApp template lifecycle is managed by Meta. Use Sync to refresh status.");
        t.LifecycleStatus = "approved";
        await db.SaveChangesAsync(ct);
        return Ok(t);
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, CancellationToken ct)
    {
        if (!rbac.HasPermission(TemplatesWrite)) return Forbid();
        var t = await db.Templates.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == CurrentTenantId, ct);
        if (t is null) return NotFound();
        if (t.Channel == ChannelType.WhatsApp)
            return BadRequest("WhatsApp template lifecycle is managed by Meta. Use Sync to refresh status.");
        t.LifecycleStatus = "rejected";
        await db.SaveChangesAsync(ct);
        return Ok(t);
    }

    [HttpPost("{id:guid}/disable")]
    public async Task<IActionResult> Disable(Guid id, CancellationToken ct)
    {
        if (!rbac.HasPermission(TemplatesWrite)) return Forbid();
        var t = await db.Templates.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == CurrentTenantId, ct);
        if (t is null) return NotFound();
        t.LifecycleStatus = "disabled";
        await db.SaveChangesAsync(ct);
        return Ok(t);
    }

    [HttpPost("{id:guid}/version")]
    public async Task<IActionResult> NewVersion(Guid id, CancellationToken ct)
    {
        if (!rbac.HasPermission(TemplatesWrite)) return Forbid();
        var current = await db.Templates.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == CurrentTenantId, ct);
        if (current is null) return NotFound();
        var next = new Textzy.Api.Models.Template
        {
            Id = Guid.NewGuid(),
            TenantId = current.TenantId,
            Name = current.Name,
            Channel = current.Channel,
            Category = current.Category,
            Language = current.Language,
            Body = current.Body,
            HeaderType = current.HeaderType,
            HeaderText = current.HeaderText,
            HeaderMediaId = current.HeaderMediaId,
            HeaderMediaName = current.HeaderMediaName,
            FooterText = current.FooterText,
            ButtonsJson = current.ButtonsJson,
            LifecycleStatus = "draft",
            Version = current.Version + 1,
            VariantGroup = string.IsNullOrWhiteSpace(current.VariantGroup) ? current.Name : current.VariantGroup,
            Status = current.Status
        };
        db.Templates.Add(next);
        await db.SaveChangesAsync(ct);
        return Ok(next);
    }
}
