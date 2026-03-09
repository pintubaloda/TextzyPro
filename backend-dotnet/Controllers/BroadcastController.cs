using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.DTOs;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/broadcasts")]
public class BroadcastController(
    TenantDbContext db,
    TenancyContext tenancy,
    BroadcastQueueService queue,
    RbacService rbac) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBroadcastJobRequest req, CancellationToken ct)
    {
        if (!rbac.HasPermission(CampaignsWrite)) return Forbid();
        string name;
        string body;
        try
        {
            name = InputGuardService.RequireTrimmed(req.Name, "Broadcast name", 160);
            body = InputGuardService.RequireTrimmed(req.MessageBody, "Message body", 4000);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        var recipients = (req.Recipients ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(x => x?.Trim() ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        if (recipients.Count == 0) return BadRequest("At least one recipient is required.");
        try
        {
            recipients = recipients.Select(x => InputGuardService.ValidatePhone(x, "Recipient")).ToList();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        if (recipients.Count > 10000) return BadRequest("Broadcast recipient limit exceeded.");

        var job = new BroadcastJob
        {
            Id = Guid.NewGuid(),
            TenantId = tenancy.TenantId,
            Name = name,
            Channel = req.Channel,
            MessageBody = body,
            RecipientCsv = string.Join(',', recipients),
            Status = "Queued"
        };

        db.BroadcastJobs.Add(job);
        await db.SaveChangesAsync(ct);
        await queue.EnqueueAsync(job, ct);
        return Ok(job);
    }

    [HttpGet]
    public IActionResult List()
    {
        if (!rbac.HasPermission(CampaignsRead)) return Forbid();
        return Ok(db.BroadcastJobs.Where(x => x.TenantId == tenancy.TenantId).OrderByDescending(x => x.CreatedAtUtc).ToList());
    }
}
