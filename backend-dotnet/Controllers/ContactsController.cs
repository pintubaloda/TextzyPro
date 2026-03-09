using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.DTOs;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/contacts")]
public class ContactsController(
    TenantDbContext db,
    TenancyContext tenancy,
    RbacService rbac,
    BillingGuardService billingGuard,
    ContactPiiService contactPii) : ControllerBase
{
    [HttpGet]
    public IActionResult List()
    {
        if (!rbac.HasPermission(ContactsRead)) return Forbid();
        var rows = (
            from c in db.Contacts
            join s in db.ContactSegments on c.SegmentId equals s.Id into seg
            from s in seg.DefaultIfEmpty()
            where c.TenantId == tenancy.TenantId
            orderby c.CreatedAtUtc descending
            select new
            {
                c.Id,
                c.TenantId,
                c.GroupId,
                c.SegmentId,
                Segment = s != null ? s.Name : null,
                Name = contactPii.RevealName(c),
                Email = contactPii.RevealEmail(c),
                c.TagsCsv,
                Tags = string.IsNullOrWhiteSpace(c.TagsCsv)
                    ? Array.Empty<string>()
                    : c.TagsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                Phone = contactPii.RevealPhone(c),
                c.OptInStatus,
                c.CreatedAtUtc
            }).ToList();
        return Ok(rows);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UpsertContactRequest request, CancellationToken ct)
    {
        if (!rbac.HasPermission(ContactsWrite)) return Forbid();
        string name;
        string phone;
        string email;
        try
        {
            name = InputGuardService.RequireTrimmed(request.Name, "Name", 256);
            phone = InputGuardService.ValidatePhone(request.Phone);
            email = InputGuardService.ValidateEmailOrEmpty(request.Email);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        var currentCount = await db.Contacts.CountAsync(x => x.TenantId == tenancy.TenantId, ct);
        var limit = await billingGuard.CheckLimitAsync(tenancy.TenantId, "contacts", currentCount + 1, ct);
        if (!limit.Allowed) return BadRequest(limit.Message);

        var defaultSegment = await db.ContactSegments
            .FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.Name.ToLower() == "new", ct);
        if (defaultSegment is null)
        {
            defaultSegment = new ContactSegment
            {
                Id = Guid.NewGuid(),
                TenantId = tenancy.TenantId,
                Name = "New",
                RuleJson = "{}",
                CreatedAtUtc = DateTime.UtcNow
            };
            db.ContactSegments.Add(defaultSegment);
        }

        var tagsCsv = string.IsNullOrWhiteSpace(request.TagsCsv) ? "New" : request.TagsCsv;
        var item = new Contact
        {
            Id = Guid.NewGuid(),
            TenantId = tenancy.TenantId,
            Name = name,
            Email = email,
            TagsCsv = tagsCsv,
            Phone = phone,
            GroupId = request.GroupId,
            SegmentId = request.SegmentId ?? defaultSegment.Id
        };
        contactPii.Protect(item);
        db.Contacts.Add(item);
        await db.SaveChangesAsync(ct);
        await billingGuard.SetAbsoluteUsageAsync(tenancy.TenantId, "contacts", currentCount + 1, ct);
        return Ok(item);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpsertContactRequest request, CancellationToken ct)
    {
        if (!rbac.HasPermission(ContactsWrite)) return Forbid();
        string name;
        string phone;
        string email;
        try
        {
            name = InputGuardService.RequireTrimmed(request.Name, "Name", 256);
            phone = InputGuardService.ValidatePhone(request.Phone);
            email = InputGuardService.ValidateEmailOrEmpty(request.Email);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        var item = db.Contacts.FirstOrDefault(x => x.Id == id && x.TenantId == tenancy.TenantId);
        if (item is null) return NotFound();
        item.Name = name;
        item.Email = email;
        item.TagsCsv = request.TagsCsv ?? string.Empty;
        item.Phone = phone;
        item.GroupId = request.GroupId;
        item.SegmentId = request.SegmentId;
        contactPii.Protect(item);
        await db.SaveChangesAsync(ct);
        return Ok(item);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!rbac.HasPermission(ContactsWrite)) return Forbid();
        var item = db.Contacts.FirstOrDefault(x => x.Id == id && x.TenantId == tenancy.TenantId);
        if (item is null) return NotFound();
        db.Contacts.Remove(item);
        await db.SaveChangesAsync(ct);
        var currentCount = await db.Contacts.CountAsync(x => x.TenantId == tenancy.TenantId, ct);
        await billingGuard.SetAbsoluteUsageAsync(tenancy.TenantId, "contacts", currentCount, ct);
        return NoContent();
    }
}
