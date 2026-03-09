using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/sms/compliance")]
public class SmsComplianceController(TenantDbContext db, TenancyContext tenancy, RbacService rbac) : ControllerBase
{
    [HttpGet("kpis")]
    public async Task<IActionResult> Kpis(CancellationToken ct)
    {
        if (!rbac.HasPermission(TemplatesRead)) return Forbid();
        var templates = await db.Templates.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId && x.Channel == ChannelType.Sms)
            .ToListAsync(ct);
        var optOuts = await db.SmsOptOuts.AsNoTracking()
            .CountAsync(x => x.TenantId == tenancy.TenantId && x.IsActive, ct);
        var today = DateTime.UtcNow.Date;
        var sentToday = await db.Messages.AsNoTracking()
            .CountAsync(x => x.TenantId == tenancy.TenantId && x.Channel == ChannelType.Sms && x.CreatedAtUtc >= today, ct);
        var deliveredToday = await db.MessageEvents.AsNoTracking()
            .CountAsync(x => x.TenantId == tenancy.TenantId && x.EventType == "sms.dlr.delivered" && x.CreatedAtUtc >= today, ct);
        var failedToday = await db.MessageEvents.AsNoTracking()
            .CountAsync(x => x.TenantId == tenancy.TenantId && x.EventType == "sms.dlr.failed" && x.CreatedAtUtc >= today, ct);
        var billedToday = await db.SmsBillingLedgers.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId && x.CreatedAtUtc >= today)
            .SumAsync(x => (decimal?)x.TotalAmount, ct) ?? 0m;
        return Ok(new
        {
            templatesTotal = templates.Count,
            templatesApproved = templates.Count(x => string.Equals(x.LifecycleStatus, "approved", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Status, "Approved", StringComparison.OrdinalIgnoreCase)),
            templatesPending = templates.Count(x => string.Equals(x.LifecycleStatus, "submitted", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Status, "InReview", StringComparison.OrdinalIgnoreCase)),
            templatesRejected = templates.Count(x => string.Equals(x.LifecycleStatus, "rejected", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Status, "Rejected", StringComparison.OrdinalIgnoreCase)),
            optOuts,
            sentToday,
            deliveredToday,
            failedToday,
            billedToday = decimal.Round(billedToday, 2)
        });
    }

    [HttpGet("opt-outs")]
    public async Task<IActionResult> ListOptOuts([FromQuery] int take = 300, CancellationToken ct = default)
    {
        if (!rbac.HasPermission(TemplatesRead)) return Forbid();
        take = Math.Clamp(take, 1, 2000);
        var rows = await db.SmsOptOuts.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId && x.IsActive)
            .OrderByDescending(x => x.OptedOutAtUtc)
            .Take(take)
            .ToListAsync(ct);
        return Ok(rows);
    }

    public sealed class UpsertOptOutRequest
    {
        public string Phone { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string Source { get; set; } = "manual";
    }

    [HttpPost("opt-outs")]
    public async Task<IActionResult> AddOptOut([FromBody] UpsertOptOutRequest request, CancellationToken ct)
    {
        if (!rbac.HasPermission(TemplatesWrite)) return Forbid();
        var phone = InputGuardService.ValidatePhone(request.Phone, "Phone");
        var existing = await db.SmsOptOuts.FirstOrDefaultAsync(
            x => x.TenantId == tenancy.TenantId && x.Phone == phone, ct);
        if (existing is not null)
        {
            existing.IsActive = true;
            existing.Reason = (request.Reason ?? string.Empty).Trim();
            existing.Source = string.IsNullOrWhiteSpace(request.Source) ? "manual" : request.Source.Trim();
            existing.OptedOutAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Ok(existing);
        }

        var row = new SmsOptOut
        {
            Id = Guid.NewGuid(),
            TenantId = tenancy.TenantId,
            Phone = phone,
            Reason = (request.Reason ?? string.Empty).Trim(),
            Source = string.IsNullOrWhiteSpace(request.Source) ? "manual" : request.Source.Trim(),
            OptedOutAtUtc = DateTime.UtcNow,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.SmsOptOuts.Add(row);
        await db.SaveChangesAsync(ct);
        return Ok(row);
    }

    [HttpDelete("opt-outs/{id:guid}")]
    public async Task<IActionResult> RemoveOptOut(Guid id, CancellationToken ct)
    {
        if (!rbac.HasPermission(TemplatesWrite)) return Forbid();
        var row = await db.SmsOptOuts.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenancy.TenantId, ct);
        if (row is null) return NotFound();
        row.IsActive = false;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("events")]
    public async Task<IActionResult> Events([FromQuery] int take = 200, CancellationToken ct = default)
    {
        if (!rbac.HasPermission(TemplatesRead)) return Forbid();
        take = Math.Clamp(take, 1, 1000);

        var rows = await (from ev in db.MessageEvents.AsNoTracking()
                          join msg in db.Messages.AsNoTracking()
                              on ev.MessageId equals msg.Id
                          where ev.TenantId == tenancy.TenantId && msg.Channel == ChannelType.Sms
                          orderby ev.CreatedAtUtc descending
                          select new
                          {
                              ev.Id,
                              ev.MessageId,
                              ev.ProviderMessageId,
                              ev.EventType,
                              ev.State,
                              deliveryMessage = MapDeliveryMessage(ev.State, ev.EventType),
                              ev.CustomerPhone,
                              ev.RawPayloadJson,
                              ev.CreatedAtUtc
                          })
            .Take(take)
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpGet("billing-ledger")]
    public async Task<IActionResult> BillingLedger([FromQuery] int take = 200, CancellationToken ct = default)
    {
        if (!rbac.HasPermission(TemplatesRead)) return Forbid();
        take = Math.Clamp(take, 1, 2000);
        var rows = await db.SmsBillingLedgers.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(take)
            .Select(x => new
            {
                x.Id,
                x.MessageId,
                x.Recipient,
                x.ProviderMessageId,
                x.Currency,
                x.UnitPrice,
                x.Segments,
                x.TotalAmount,
                x.BillingState,
                x.DeliveryState,
                deliveryMessage = MapDeliveryMessage(x.DeliveryState, string.Empty),
                x.Notes,
                x.CreatedAtUtc,
                x.UpdatedAtUtc
            })
            .ToListAsync(ct);
        return Ok(rows);
    }

    private static string MapDeliveryMessage(string? state, string? eventType)
    {
        var normalized = (state ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) && !string.IsNullOrWhiteSpace(eventType))
            normalized = eventType.Replace("sms.dlr.", string.Empty, StringComparison.OrdinalIgnoreCase).Trim().ToLowerInvariant();

        return normalized switch
        {
            "delivered" => "Delivered to customer handset.",
            "submitted" => "Submitted to operator, waiting final delivery report.",
            "failed" => "Delivery failed at operator/network.",
            "queued" => "Queued for provider dispatch.",
            "processing" => "Message is being processed.",
            _ => "Delivery status update received."
        };
    }
}
