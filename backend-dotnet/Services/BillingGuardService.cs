using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;

namespace Textzy.Api.Services;

public class BillingGuardService(ControlDbContext db)
{
    public string CurrentMonthKey => DateTime.UtcNow.ToString("yyyy-MM");

    public async Task<(bool Allowed, int Limit, int Used, string Message)> CheckLimitAsync(Guid tenantId, string key, int nextUsed, CancellationToken ct = default)
    {
        var sub = await db.TenantSubscriptions
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (sub is null) return (false, 0, nextUsed, "No active subscription found.");
        if (string.Equals(sub.Status, "cancelled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sub.Status, "suspended", StringComparison.OrdinalIgnoreCase))
            return (false, 0, nextUsed, $"Subscription status is {sub.Status}. Please renew plan.");

        var plan = await db.BillingPlans.FirstOrDefaultAsync(x => x.Id == sub.PlanId && x.IsActive, ct);
        if (plan is null) return (false, 0, nextUsed, "Subscription plan is inactive or missing.");
        if (string.Equals(key, "smsCredits", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(plan.PricingModel, "usage_pack", StringComparison.OrdinalIgnoreCase))
        {
            var balance = await GetCreditBalanceAsync(tenantId, key, ct);
            if (balance <= 0)
                return (false, 0, nextUsed, "No prepaid SMS credits available. Please buy a new SMS pack.");
        }

        Dictionary<string, int> limits;
        try { limits = JsonSerializer.Deserialize<Dictionary<string, int>>(plan.LimitsJson) ?? new(); }
        catch { limits = new(); }

        if (!limits.TryGetValue(key, out var limit) || limit <= 0) return (true, int.MaxValue, nextUsed, string.Empty);
        if (nextUsed <= limit) return (true, limit, nextUsed, string.Empty);
        return (false, limit, nextUsed, $"Plan limit exceeded for {key}: {nextUsed}/{limit}");
    }

    public async Task<int> GetCurrentUsageAsync(Guid tenantId, string key, CancellationToken ct = default)
    {
        var usage = await GetOrCreateUsageAsync(tenantId, ct);
        return key switch
        {
            "whatsappMessages" => usage.WhatsappMessagesUsed,
            "smsCredits" => usage.SmsCreditsUsed,
            "contacts" => usage.ContactsUsed,
            "teamMembers" => usage.TeamMembersUsed,
            "chatbots" => usage.ChatbotsUsed,
            "flows" => usage.FlowsUsed,
            "apiCalls" => usage.ApiCallsUsed,
            _ => 0
        };
    }

    public async Task<(bool Allowed, int Limit, int Used, string Message)> TryConsumeAsync(Guid tenantId, string key, int delta = 1, CancellationToken ct = default)
    {
        var usage = await GetOrCreateUsageAsync(tenantId, ct);
        var current = await GetCurrentUsageAsync(tenantId, key, ct);
        if (string.Equals(key, "smsCredits", StringComparison.OrdinalIgnoreCase))
        {
            var prepaidBalance = await GetCreditBalanceAsync(tenantId, key, ct);
            if (prepaidBalance > 0)
            {
                var planRemaining = await GetPlanRemainingAsync(tenantId, key, current, ct);
                var totalAvailable = prepaidBalance + Math.Max(0, planRemaining);
                if (delta > totalAvailable)
                    return (false, totalAvailable, delta, $"Available SMS credits are insufficient: need {delta}, available {totalAvailable}");

                var consumeFromBalance = Math.Min(prepaidBalance, delta);
                var consumeFromPlan = Math.Max(0, delta - consumeFromBalance);
                if (consumeFromBalance > 0)
                    await ConsumeCreditBalanceAsync(tenantId, key, consumeFromBalance, ct);

                if (consumeFromPlan > 0)
                {
                    var planCheck = await CheckLimitAsync(tenantId, key, current + consumeFromPlan, ct);
                    if (!planCheck.Allowed) return planCheck;
                }

                SetUsageValue(usage, key, current + consumeFromPlan);
                usage.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                return (true, totalAvailable, delta, string.Empty);
            }
        }

        var next = Math.Max(0, current + delta);
        var check = await CheckLimitAsync(tenantId, key, next, ct);
        if (!check.Allowed) return check;

        SetUsageValue(usage, key, next);
        usage.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return check;
    }

    public async Task SetAbsoluteUsageAsync(Guid tenantId, string key, int value, CancellationToken ct = default)
    {
        var usage = await GetOrCreateUsageAsync(tenantId, ct);
        SetUsageValue(usage, key, Math.Max(0, value));
        usage.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task RotateMonthlyBucketAsync(Guid tenantId, CancellationToken ct = default)
    {
        var monthKey = CurrentMonthKey;
        var exists = await db.TenantUsages.AnyAsync(x => x.TenantId == tenantId && x.MonthKey == monthKey, ct);
        if (exists) return;
        db.TenantUsages.Add(new Textzy.Api.Models.TenantUsage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            MonthKey = monthKey,
            WhatsappMessagesUsed = 0,
            SmsCreditsUsed = 0,
            ContactsUsed = 0,
            TeamMembersUsed = 0,
            ChatbotsUsed = 0,
            FlowsUsed = 0,
            ApiCallsUsed = 0,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> GetCreditBalanceAsync(Guid tenantId, string key, CancellationToken ct = default)
    {
        var row = await db.TenantUsageCreditBalances
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.MetricKey == key, ct);
        return Math.Max(0, row?.UnitsRemaining ?? 0);
    }

    public async Task AddCreditUnitsAsync(Guid tenantId, string key, int units, CancellationToken ct = default)
    {
        if (units <= 0) return;
        var row = await GetOrCreateCreditBalanceEntityAsync(tenantId, key, ct);
        row.UnitsRemaining += units;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task<Textzy.Api.Models.TenantUsage> GetOrCreateUsageAsync(Guid tenantId, CancellationToken ct)
    {
        var monthKey = CurrentMonthKey;
        var usage = await db.TenantUsages.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.MonthKey == monthKey, ct);
        if (usage is not null) return usage;
        usage = new Textzy.Api.Models.TenantUsage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            MonthKey = monthKey,
            WhatsappMessagesUsed = 0,
            SmsCreditsUsed = 0,
            ContactsUsed = 0,
            TeamMembersUsed = 0,
            ChatbotsUsed = 0,
            FlowsUsed = 0,
            ApiCallsUsed = 0,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.TenantUsages.Add(usage);
        await db.SaveChangesAsync(ct);
        return usage;
    }

    private async Task<int> GetPlanRemainingAsync(Guid tenantId, string key, int currentUsage, CancellationToken ct)
    {
        var sub = await db.TenantSubscriptions
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (sub is null) return 0;
        if (string.Equals(sub.Status, "cancelled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sub.Status, "suspended", StringComparison.OrdinalIgnoreCase))
            return 0;

        var plan = await db.BillingPlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sub.PlanId && x.IsActive, ct);
        if (plan is null) return 0;
        if (string.Equals(key, "smsCredits", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(plan.PricingModel, "usage_pack", StringComparison.OrdinalIgnoreCase))
            return 0;

        Dictionary<string, int> limits;
        try { limits = JsonSerializer.Deserialize<Dictionary<string, int>>(plan.LimitsJson) ?? new(); }
        catch { limits = new(); }

        if (!limits.TryGetValue(key, out var limit) || limit <= 0) return int.MaxValue;
        return Math.Max(0, limit - currentUsage);
    }

    private async Task<Textzy.Api.Models.TenantUsageCreditBalance> GetOrCreateCreditBalanceEntityAsync(Guid tenantId, string key, CancellationToken ct)
    {
        var row = await db.TenantUsageCreditBalances.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.MetricKey == key, ct);
        if (row is not null) return row;
        row = new Textzy.Api.Models.TenantUsageCreditBalance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            MetricKey = key,
            UnitsRemaining = 0,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.TenantUsageCreditBalances.Add(row);
        await db.SaveChangesAsync(ct);
        return row;
    }

    private async Task ConsumeCreditBalanceAsync(Guid tenantId, string key, int units, CancellationToken ct)
    {
        if (units <= 0) return;
        var row = await GetOrCreateCreditBalanceEntityAsync(tenantId, key, ct);
        row.UnitsRemaining = Math.Max(0, row.UnitsRemaining - units);
        row.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static void SetUsageValue(Textzy.Api.Models.TenantUsage usage, string key, int value)
    {
        switch (key)
        {
            case "whatsappMessages": usage.WhatsappMessagesUsed = value; break;
            case "smsCredits": usage.SmsCreditsUsed = value; break;
            case "contacts": usage.ContactsUsed = value; break;
            case "teamMembers": usage.TeamMembersUsed = value; break;
            case "chatbots": usage.ChatbotsUsed = value; break;
            case "flows": usage.FlowsUsed = value; break;
            case "apiCalls": usage.ApiCallsUsed = value; break;
        }
    }
}
