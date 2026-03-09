using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/contact-data")]
public class ContactDataController(
    TenantDbContext db,
    TenancyContext tenancy,
    RbacService rbac,
    BillingGuardService billingGuard,
    ContactPiiService contactPii) : ControllerBase
{
    [HttpPost("import/csv")]
    public async Task<IActionResult> ImportCsv([FromForm] IFormFile file, CancellationToken ct)
    {
        if (!rbac.HasPermission(ContactsWrite)) return Forbid();
        if (file is null || file.Length == 0) return BadRequest("CSV file required.");

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
            await db.SaveChangesAsync(ct);
        }

        using var reader = new StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync(ct);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var currentCount = await db.Contacts.CountAsync(x => x.TenantId == tenancy.TenantId, ct);
        var importable = Math.Max(0, lines.Length - 1);
        var limit = await billingGuard.CheckLimitAsync(tenancy.TenantId, "contacts", currentCount + importable, ct);
        if (!limit.Allowed) return BadRequest(limit.Message);
        var imported = 0;

        foreach (var line in lines.Skip(1))
        {
            var cols = line.Split(',');
            if (cols.Length < 2) continue;
            var name = (cols[0] ?? string.Empty).Trim();
            var phoneRaw = (cols[1] ?? string.Empty).Trim();
            string phone;
            try
            {
                phone = InputGuardService.ValidatePhone(phoneRaw);
            }
            catch
            {
                continue;
            }
            var optIn = cols.Length > 2 ? cols[2].Trim().ToLowerInvariant() : "unknown";
            if (optIn is not ("opted_in" or "opted_out" or "unknown")) optIn = "unknown";
            var contact = new Contact
            {
                Id = Guid.NewGuid(),
                TenantId = tenancy.TenantId,
                Name = name,
                Phone = phone,
                OptInStatus = optIn,
                SegmentId = defaultSegment.Id,
                TagsCsv = "New"
            };
            contactPii.Protect(contact);
            db.Contacts.Add(contact);
            imported++;
        }

        await db.SaveChangesAsync(ct);
        await billingGuard.SetAbsoluteUsageAsync(tenancy.TenantId, "contacts", currentCount + imported, ct);
        return Ok(new { imported });
    }

    [HttpPost("contacts/{contactId:guid}/custom-fields")]
    public async Task<IActionResult> UpsertCustomField(Guid contactId, [FromBody] ContactCustomField field, CancellationToken ct)
    {
        if (!rbac.HasPermission(ContactsWrite)) return Forbid();
        field.FieldKey = InputGuardService.RequireTrimmed(field.FieldKey, "FieldKey", 80);
        field.FieldValue = (field.FieldValue ?? string.Empty).Trim();
        if (field.FieldValue.Length > 1000) return BadRequest("FieldValue is too long.");
        var existing = await db.ContactCustomFields.FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.ContactId == contactId && x.FieldKey == field.FieldKey, ct);
        if (existing is null)
        {
            existing = new ContactCustomField { Id = Guid.NewGuid(), TenantId = tenancy.TenantId, ContactId = contactId, FieldKey = field.FieldKey, FieldValue = field.FieldValue };
            db.ContactCustomFields.Add(existing);
        }
        else
        {
            existing.FieldValue = field.FieldValue;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return Ok(existing);
    }

    [HttpPost("segments")]
    public async Task<IActionResult> CreateSegment([FromBody] ContactSegment segment, CancellationToken ct)
    {
        if (!rbac.HasPermission(ContactsWrite)) return Forbid();
        segment.Name = InputGuardService.RequireTrimmed(segment.Name, "Segment name", 120);
        segment.RuleJson = string.IsNullOrWhiteSpace(segment.RuleJson) ? "{}" : segment.RuleJson.Trim();
        if (segment.RuleJson.Length > 8000) return BadRequest("RuleJson is too long.");
        segment.Id = Guid.NewGuid();
        segment.TenantId = tenancy.TenantId;
        db.ContactSegments.Add(segment);
        await db.SaveChangesAsync(ct);
        return Ok(segment);
    }

    [HttpGet("segments")]
    public IActionResult ListSegments()
    {
        if (!rbac.HasPermission(ContactsRead)) return Forbid();
        return Ok(db.ContactSegments.Where(x => x.TenantId == tenancy.TenantId).OrderBy(x => x.Name).ToList());
    }

    [HttpPost("contacts/{contactId:guid}/opt-in")]
    public async Task<IActionResult> SetOptIn(Guid contactId, [FromBody] Dictionary<string, string> body, CancellationToken ct)
    {
        if (!rbac.HasPermission(ContactsWrite)) return Forbid();
        var status = body.TryGetValue("status", out var s) ? s.ToLowerInvariant() : "unknown";
        if (status is not ("opted_in" or "opted_out" or "unknown")) return BadRequest("Invalid opt-in status.");
        var c = await db.Contacts.FirstOrDefaultAsync(x => x.Id == contactId && x.TenantId == tenancy.TenantId, ct);
        if (c is null) return NotFound();
        c.OptInStatus = status;
        await db.SaveChangesAsync(ct);
        return Ok(c);
    }
}
