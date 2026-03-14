using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/platform/revenue-metrics")]
public class PlatformRevenueMetricsController(
    ControlDbContext db,
    AuthContext auth,
    RbacService rbac) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 30, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        var safeDays = Math.Clamp(days, 1, 365);
        var fromUtc = DateTime.UtcNow.Date.AddDays(-safeDays + 1);

        // Latest subscription per tenant drives "current" plan and MRR.
        var latestSubs = await db.TenantSubscriptions.AsNoTracking()
            .GroupBy(x => x.TenantId)
            .Select(g => g.OrderByDescending(x => x.CreatedAtUtc).First())
            .ToListAsync(ct);

        var planIds = latestSubs.Select(x => x.PlanId).Distinct().ToList();
        var plans = planIds.Count == 0
            ? new Dictionary<Guid, Models.BillingPlan>()
            : await db.BillingPlans.AsNoTracking()
                .Where(x => planIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);

        decimal mrr = 0m;
        var statusCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var planCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var planMrr = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var sub in latestSubs)
        {
            var status = string.IsNullOrWhiteSpace(sub.Status) ? "unknown" : sub.Status.Trim().ToLowerInvariant();
            statusCounts.TryGetValue(status, out var s);
            statusCounts[status] = s + 1;

            if (!plans.TryGetValue(sub.PlanId, out var plan)) continue;
            var planCode = string.IsNullOrWhiteSpace(plan.Code) ? "unknown" : plan.Code.Trim().ToLowerInvariant();
            planCounts.TryGetValue(planCode, out var pc);
            planCounts[planCode] = pc + 1;

            // Treat monthly and yearly subscriptions as MRR (yearly amortized / 12).
            var cycle = (sub.BillingCycle ?? "monthly").Trim().ToLowerInvariant();
            var monthly = cycle == "yearly" ? (plan.PriceYearly / 12m) : plan.PriceMonthly;

            var isBillableStatus =
                status is "active" or "trial" or "trialing" or "past_due";
            if (!isBillableStatus) continue;

            mrr += monthly;
            planMrr.TryGetValue(planCode, out var pm);
            planMrr[planCode] = pm + monthly;
        }

        var invoices = await db.BillingInvoices.AsNoTracking()
            .Where(x => x.CreatedAtUtc >= fromUtc)
            .ToListAsync(ct);

        var paidInvoices = invoices.Where(x => string.Equals(x.Status, "paid", StringComparison.OrdinalIgnoreCase)).ToList();
        var unpaidInvoices = invoices.Where(x => !string.Equals(x.Status, "paid", StringComparison.OrdinalIgnoreCase)).ToList();

        var revenuePaid = paidInvoices.Sum(x => x.Total);
        var revenueOutstanding = unpaidInvoices.Sum(x => x.Total);

        var dailyRevenueRows = paidInvoices
            .GroupBy(x => x.CreatedAtUtc.Date)
            .Select(g => new { day = g.Key, total = g.Sum(x => x.Total), count = g.Count() })
            .OrderBy(x => x.day)
            .ToList();

        return Ok(new
        {
            fromUtc,
            days = safeDays,
            current = new
            {
                tenants = latestSubs.Count,
                mrr = Math.Round(mrr, 2),
                arr = Math.Round(mrr * 12m, 2),
                statusCounts,
                planCounts,
                planMrr = planMrr
                    .OrderByDescending(x => x.Value)
                    .ToDictionary(x => x.Key, x => Math.Round(x.Value, 2))
            },
            invoices = new
            {
                issued = invoices.Count,
                paid = paidInvoices.Count,
                unpaid = unpaidInvoices.Count,
                revenuePaid = Math.Round(revenuePaid, 2),
                revenueOutstanding = Math.Round(revenueOutstanding, 2),
                dailyRevenue = dailyRevenueRows.Select(x => new
                {
                    day = x.day.ToString("yyyy-MM-dd"),
                    total = Math.Round(x.total, 2),
                    count = x.count
                })
            }
        });
    }
}

