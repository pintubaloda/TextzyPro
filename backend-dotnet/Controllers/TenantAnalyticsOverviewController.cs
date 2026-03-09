using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/analytics/overview")]
public class TenantAnalyticsOverviewController(
    TenantDbContext db,
    TenancyContext tenancy,
    AuthContext auth,
    RbacService rbac) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 30, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(ApiRead)) return Forbid();
        if (tenancy.TenantId == Guid.Empty) return BadRequest("Tenant context missing.");

        var safeDays = Math.Clamp(days, 1, 365);
        var from = DateTime.UtcNow.Date.AddDays(-safeDays + 1);

        var tenantMessages = db.Messages.Where(x => x.TenantId == tenancy.TenantId);
        var rangedMessages = tenantMessages.Where(x => x.CreatedAtUtc >= from);
        var rangedList = await rangedMessages.ToListAsync(ct);

        var totalMessages = rangedList.Count;
        var delivered = rangedList.Count(x => x.DeliveredAtUtc != null || string.Equals(x.Status, "Delivered", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Status, "Read", StringComparison.OrdinalIgnoreCase));
        var read = rangedList.Count(x => x.ReadAtUtc != null || string.Equals(x.Status, "Read", StringComparison.OrdinalIgnoreCase));
        var failed = rangedList.Count(x => string.Equals(x.Status, "Failed", StringComparison.OrdinalIgnoreCase));
        var activeConversations = rangedList
            .Select(x => (x.Recipient ?? string.Empty).Trim())
            .Where(x => x != string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var totalContacts = await db.Contacts.CountAsync(x => x.TenantId == tenancy.TenantId, ct);
        var totalCampaigns = await db.Campaigns.CountAsync(x => x.TenantId == tenancy.TenantId, ct);

        var dailyRows = rangedList
            .GroupBy(x => new { Day = x.CreatedAtUtc.Date, Channel = x.Channel.ToString() })
            .Select(g => new { g.Key.Day, g.Key.Channel, Count = g.Count() })
            .OrderBy(x => x.Day)
            .ToList();
        var dailyMap = new SortedDictionary<DateTime, Dictionary<string, int>>();
        for (var cursor = from; cursor <= DateTime.UtcNow.Date; cursor = cursor.AddDays(1))
            dailyMap[cursor] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in dailyRows)
        {
            if (!dailyMap.TryGetValue(row.Day, out var bucket))
            {
                bucket = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                dailyMap[row.Day] = bucket;
            }
            bucket[row.Channel] = row.Count;
        }
        var dailyVolume = dailyMap.Select(kvp => new
        {
            day = kvp.Key.ToString("yyyy-MM-dd"),
            whatsapp = kvp.Value.TryGetValue("WhatsApp", out var wa) ? wa : 0,
            sms = kvp.Value.TryGetValue("SMS", out var sms) ? sms : 0,
            email = kvp.Value.TryGetValue("Email", out var em) ? em : 0,
            other = kvp.Value
                .Where(x => !x.Key.Equals("WhatsApp", StringComparison.OrdinalIgnoreCase)
                         && !x.Key.Equals("SMS", StringComparison.OrdinalIgnoreCase)
                         && !x.Key.Equals("Email", StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.Value)
        });

        var channelDistribution = rangedList
            .GroupBy(x => x.Channel.ToString())
            .Select(g => new { name = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .ToList();

        var statusDistribution = rangedList
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Status) ? "Unknown" : x.Status)
            .Select(g => new { name = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .ToList();

        var hourlyVolume = Enumerable.Range(0, 24)
            .Select(hour => new
            {
                hour = $"{hour:00}:00",
                messages = rangedList.Count(x => x.CreatedAtUtc.Hour == hour)
            })
            .ToList();

        var campaignMetrics = await tenantMessages
            .Where(x => x.CampaignId != null)
            .GroupBy(x => x.CampaignId!.Value)
            .Select(g => new
            {
                CampaignId = g.Key,
                Sent = g.Count(),
                Delivered = g.Count(m => m.DeliveredAtUtc != null || m.Status == "Delivered" || m.Status == "Read"),
                Read = g.Count(m => m.ReadAtUtc != null || m.Status == "Read"),
                Failed = g.Count(m => m.Status == "Failed")
            })
            .ToListAsync(ct);
        var metricsByCampaign = campaignMetrics.ToDictionary(x => x.CampaignId, x => x);
        var campaignPerformance = await db.Campaigns
            .Where(x => x.TenantId == tenancy.TenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(8)
            .Select(x => new { x.Id, x.Name, Channel = x.Channel.ToString(), x.CreatedAtUtc })
            .ToListAsync(ct);

        var campaignRows = campaignPerformance.Select(x =>
        {
            metricsByCampaign.TryGetValue(x.Id, out var metric);
            return new
            {
                id = x.Id,
                name = x.Name,
                channel = x.Channel,
                createdAtUtc = x.CreatedAtUtc,
                sent = metric?.Sent ?? 0,
                delivered = metric?.Delivered ?? 0,
                read = metric?.Read ?? 0,
                failed = metric?.Failed ?? 0
            };
        }).ToList();

        return Ok(new
        {
            tenantId = tenancy.TenantId,
            from,
            days = safeDays,
            totals = new
            {
                totalMessages,
                delivered,
                read,
                failed,
                totalContacts,
                totalCampaigns,
                activeConversations
            },
            rates = new
            {
                deliveryRate = totalMessages > 0 ? Math.Round((double)delivered / totalMessages * 100, 1) : 0,
                readRate = totalMessages > 0 ? Math.Round((double)read / totalMessages * 100, 1) : 0,
                failureRate = totalMessages > 0 ? Math.Round((double)failed / totalMessages * 100, 1) : 0
            },
            dailyVolume,
            channelDistribution,
            statusDistribution,
            hourlyVolume,
            campaignPerformance = campaignRows
        });
    }
}
