using Microsoft.AspNetCore.Mvc;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/campaigns")]
public class CampaignsController(TenantDbContext db, TenancyContext tenancy, RbacService rbac) : ControllerBase
{
    [HttpGet]
    public IActionResult List()
    {
        if (!rbac.HasPermission(CampaignsRead)) return Forbid();

        var metrics = db.Messages
            .Where(m => m.TenantId == tenancy.TenantId && m.CampaignId != null)
            .GroupBy(m => m.CampaignId!.Value)
            .Select(g => new
            {
                CampaignId = g.Key,
                Sent = g.Count(),
                Delivered = g.Count(m =>
                    m.DeliveredAtUtc != null ||
                    m.Status == "Delivered" ||
                    m.Status == "Read"),
                Read = g.Count(m =>
                    m.ReadAtUtc != null ||
                    m.Status == "Read"),
                Failed = g.Count(m => m.Status == "Failed")
            })
            .ToDictionary(x => x.CampaignId, x => new { x.Sent, x.Delivered, x.Read, x.Failed });

        var campaigns = db.Campaigns
            .Where(c => c.TenantId == tenancy.TenantId)
            .ToList()
            .Select(c =>
            {
                metrics.TryGetValue(c.Id, out var metric);
                return new
                {
                    c.Id,
                    c.Name,
                    c.Channel,
                    c.TemplateText,
                    c.CreatedAtUtc,
                    Sent = metric?.Sent ?? 0,
                    Delivered = metric?.Delivered ?? 0,
                    Read = metric?.Read ?? 0,
                    Failed = metric?.Failed ?? 0,
                    Status = (metric?.Failed ?? 0) > 0 && (metric?.Delivered ?? 0) == 0 && (metric?.Read ?? 0) == 0
                        ? "error"
                        : "active"
                };
            })
            .ToList();

        return Ok(campaigns);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Campaign request, CancellationToken ct)
    {
        if (!rbac.HasPermission(CampaignsWrite)) return Forbid();
        var item = new Campaign
        {
            Id = Guid.NewGuid(),
            TenantId = tenancy.TenantId,
            Name = request.Name,
            Channel = request.Channel,
            TemplateText = request.TemplateText
        };
        db.Campaigns.Add(item);
        await db.SaveChangesAsync(ct);
        return Ok(item);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] Campaign request, CancellationToken ct)
    {
        if (!rbac.HasPermission(CampaignsWrite)) return Forbid();
        var item = db.Campaigns.FirstOrDefault(x => x.Id == id && x.TenantId == tenancy.TenantId);
        if (item is null) return NotFound();
        item.Name = request.Name;
        item.Channel = request.Channel;
        item.TemplateText = request.TemplateText;
        await db.SaveChangesAsync(ct);
        return Ok(item);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!rbac.HasPermission(CampaignsWrite)) return Forbid();
        var item = db.Campaigns.FirstOrDefault(x => x.Id == id && x.TenantId == tenancy.TenantId);
        if (item is null) return NotFound();
        db.Campaigns.Remove(item);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
