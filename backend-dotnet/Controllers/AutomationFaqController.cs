using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.DTOs;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/automation/faq")]
public class AutomationFaqController(TenantDbContext db, TenancyContext tenancy, RbacService rbac) : ControllerBase
{
    private void EnsureFaqSchema()
    {
        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "FaqKnowledgeItems" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "Question" text NOT NULL DEFAULT '', "Answer" text NOT NULL DEFAULT '', "Category" text NOT NULL DEFAULT '', "IsActive" boolean NOT NULL DEFAULT true, "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now(), "UpdatedAtUtc" timestamp with time zone NOT NULL DEFAULT now());""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_FaqKnowledgeItems_Tenant_Active" ON "FaqKnowledgeItems" ("TenantId","IsActive");""");
    }

    [HttpGet]
    public IActionResult List()
    {
        if (!rbac.HasPermission(AutomationRead)) return Forbid();
        EnsureFaqSchema();
        var rows = db.FaqKnowledgeItems
            .Where(x => x.TenantId == tenancy.TenantId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToList();
        return Ok(rows);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UpsertFaqKnowledgeRequest req, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationWrite)) return Forbid();
        EnsureFaqSchema();
        if (string.IsNullOrWhiteSpace(req.Question)) return BadRequest("Question is required.");
        if (string.IsNullOrWhiteSpace(req.Answer)) return BadRequest("Answer is required.");

        var item = new FaqKnowledgeItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenancy.TenantId,
            Question = req.Question.Trim(),
            Answer = req.Answer.Trim(),
            Category = req.Category?.Trim() ?? string.Empty,
            IsActive = req.IsActive,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.FaqKnowledgeItems.Add(item);
        await db.SaveChangesAsync(ct);
        return Ok(item);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpsertFaqKnowledgeRequest req, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationWrite)) return Forbid();
        EnsureFaqSchema();
        if (string.IsNullOrWhiteSpace(req.Question)) return BadRequest("Question is required.");
        if (string.IsNullOrWhiteSpace(req.Answer)) return BadRequest("Answer is required.");

        var item = db.FaqKnowledgeItems.FirstOrDefault(x => x.Id == id && x.TenantId == tenancy.TenantId);
        if (item is null) return NotFound();

        item.Question = req.Question.Trim();
        item.Answer = req.Answer.Trim();
        item.Category = req.Category?.Trim() ?? string.Empty;
        item.IsActive = req.IsActive;
        item.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(item);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationWrite)) return Forbid();
        EnsureFaqSchema();
        var item = db.FaqKnowledgeItems.FirstOrDefault(x => x.Id == id && x.TenantId == tenancy.TenantId);
        if (item is null) return NoContent();
        db.FaqKnowledgeItems.Remove(item);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
