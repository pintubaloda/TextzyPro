using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;

namespace Textzy.Api.Services;

public sealed record BillingConsumeReceipt(
    Guid TenantId,
    string MetricKey,
    int RequestedUnits,
    int ConsumedFromBalance,
    int ConsumedFromPlan,
    string Source,
    string Service,
    string ReferenceId);

public sealed record BillingConsumeResult(
    bool Allowed,
    int Limit,
    int Used,
    string Message,
    BillingConsumeReceipt? Receipt);

public sealed class CreditGuardrailSettings
{
    public bool LowBalanceAlertsEnabled { get; init; } = true;
    public Dictionary<string, int> LowBalanceThresholds { get; init; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["smsCredits"] = 100,
        ["digilockerKyc"] = 5
    };

    public bool AutoTopupEnabled { get; init; }
    public Dictionary<string, int> AutoTopupTriggerThresholds { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> AutoTopupUnits { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> DailySpendCaps { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> ServiceCreditLimits { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public int ResolveLowBalanceThreshold(string metricKey)
        => LowBalanceThresholds.TryGetValue((metricKey ?? string.Empty).Trim(), out var value) ? Math.Max(0, value) : 0;

    public int ResolveAutoTopupTrigger(string metricKey)
        => AutoTopupTriggerThresholds.TryGetValue((metricKey ?? string.Empty).Trim(), out var value) ? Math.Max(0, value) : 0;

    public int ResolveAutoTopupUnits(string metricKey)
        => AutoTopupUnits.TryGetValue((metricKey ?? string.Empty).Trim(), out var value) ? Math.Max(0, value) : 0;
}

public class BillingGuardService(
    ControlDbContext db,
    SecretCryptoService crypto,
    IEmailService emailService,
    ILogger<BillingGuardService> logger)
{
    private const string GuardrailScopePrefix = "credit-guardrails";
    private const string GuardrailConfigKey = "config";
    public string CurrentMonthKey => DateTime.UtcNow.ToString("yyyy-MM");

    private static bool IsUsagePackCreditMetric(string key)
        => string.Equals(key, "smsCredits", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "digilockerKyc", StringComparison.OrdinalIgnoreCase);

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
        var isKycMetric = string.Equals(key, "digilockerKyc", StringComparison.OrdinalIgnoreCase);
        if (isKycMetric)
        {
            var balance = await GetCreditBalanceAsync(tenantId, key, ct);
            if (balance <= 0)
                return (false, 0, nextUsed, "No KYC credits available. Please buy a KYC pack.");
        }
        else if (IsUsagePackCreditMetric(key) && string.Equals(plan.PricingModel, "usage_pack", StringComparison.OrdinalIgnoreCase))
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
            "digilockerKyc" => usage.DigilockerKycUsed,
            _ => 0
        };
    }

    /// <summary>
    /// Returns the total units available for a metric combining prepaid usage-pack balance and plan remaining.
    /// For unlimited plan limits, returns <see cref="int.MaxValue"/>.
    /// </summary>
    public async Task<int> GetTotalAvailableUnitsAsync(Guid tenantId, string key, CancellationToken ct = default)
    {
        var current = await GetCurrentUsageAsync(tenantId, key, ct);
        if (!IsUsagePackCreditMetric(key))
        {
            var remaining = await GetPlanRemainingAsync(tenantId, key, current, ct);
            return remaining;
        }

        var prepaidBalance = await GetCreditBalanceAsync(tenantId, key, ct);
        if (string.Equals(key, "digilockerKyc", StringComparison.OrdinalIgnoreCase))
            return prepaidBalance;
        var planRemaining = await GetPlanRemainingAsync(tenantId, key, current, ct);
        if (planRemaining == int.MaxValue) return int.MaxValue;
        return prepaidBalance + Math.Max(0, planRemaining);
    }

    public async Task<(bool Allowed, int Limit, int Used, string Message)> TryConsumeAsync(Guid tenantId, string key, int delta = 1, CancellationToken ct = default)
    {
        var result = await TryConsumeDetailedAsync(tenantId, key, delta, ct: ct);
        return (result.Allowed, result.Limit, result.Used, result.Message);
    }

    public async Task<BillingConsumeResult> TryConsumeDetailedAsync(
        Guid tenantId,
        string key,
        int delta = 1,
        string source = "",
        string service = "",
        string referenceId = "",
        CancellationToken ct = default)
    {
        key = (key ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
            return new BillingConsumeResult(false, 0, 0, "Metric key is required.", null);
        if (delta <= 0)
            return new BillingConsumeResult(true, int.MaxValue, 0, string.Empty, new BillingConsumeReceipt(tenantId, key, 0, 0, 0, source, service, referenceId));

        var usage = await GetOrCreateUsageAsync(tenantId, ct);
        var current = await GetCurrentUsageAsync(tenantId, key, ct);
        var guardrails = await GetGuardrailsAsync(tenantId, ct);

        var normalizedService = (service ?? string.Empty).Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(normalizedService) &&
            guardrails.ServiceCreditLimits.TryGetValue(normalizedService, out var maxPerTxn) &&
            maxPerTxn > 0 &&
            delta > maxPerTxn)
        {
            return new BillingConsumeResult(false, maxPerTxn, delta, $"Per-service credit limit exceeded for {normalizedService}: need {delta}, max {maxPerTxn}", null);
        }

        if (guardrails.DailySpendCaps.TryGetValue(key, out var dailyCap) && dailyCap > 0)
        {
            var fromUtc = DateTime.UtcNow.Date;
            var spentToday = await db.TenantCreditTransactions
                .AsNoTracking()
                .Where(x => x.TenantId == tenantId
                    && x.MetricKey == key
                    && x.TransactionType == "debit"
                    && x.CreatedAtUtc >= fromUtc)
                .SumAsync(x => (int?)x.Units, ct) ?? 0;
            var nextDaily = spentToday + delta;
            if (nextDaily > dailyCap)
                return new BillingConsumeResult(false, dailyCap, nextDaily, $"Daily spend cap exceeded for {key}: {nextDaily}/{dailyCap}", null);
        }

        if (IsUsagePackCreditMetric(key))
        {
            var prepaidBalance = await GetCreditBalanceAsync(tenantId, key, ct);
            if (guardrails.AutoTopupEnabled && prepaidBalance < delta)
            {
                var trigger = guardrails.ResolveAutoTopupTrigger(key);
                var topupUnits = guardrails.ResolveAutoTopupUnits(key);
                if (topupUnits > 0 && prepaidBalance <= trigger)
                {
                    await AddCreditUnitsAsync(
                        tenantId,
                        key,
                        topupUnits,
                        ct,
                        source: "auto_topup",
                        service: string.IsNullOrWhiteSpace(normalizedService) ? key : normalizedService,
                        referenceId: string.IsNullOrWhiteSpace(referenceId) ? $"auto:{DateTime.UtcNow:yyyyMMddHHmmss}" : referenceId,
                        status: "credited");
                    prepaidBalance = await GetCreditBalanceAsync(tenantId, key, ct);
                }
            }
            if (string.Equals(key, "digilockerKyc", StringComparison.OrdinalIgnoreCase) && prepaidBalance <= 0)
                return new BillingConsumeResult(false, 0, delta, "No KYC credits available. Please buy a KYC pack.", null);
            if (prepaidBalance > 0)
            {
                var isKycMetric = string.Equals(key, "digilockerKyc", StringComparison.OrdinalIgnoreCase);
                var planRemaining = isKycMetric ? 0 : await GetPlanRemainingAsync(tenantId, key, current, ct);
                long totalAvailableLong = isKycMetric
                    ? prepaidBalance
                    : (long)prepaidBalance + Math.Max(0, (long)planRemaining);
                var totalAvailable = totalAvailableLong >= int.MaxValue ? int.MaxValue : (int)Math.Max(0, totalAvailableLong);
                if (delta > totalAvailable)
                {
                    var label = string.Equals(key, "digilockerKyc", StringComparison.OrdinalIgnoreCase) ? "KYC credits" : "SMS credits";
                    return new BillingConsumeResult(false, totalAvailable, delta, $"Available {label} are insufficient: need {delta}, available {totalAvailable}", null);
                }

                var consumeFromBalance = Math.Min(prepaidBalance, delta);
                var consumeFromPlan = isKycMetric ? 0 : Math.Max(0, delta - consumeFromBalance);
                if (consumeFromBalance > 0)
                    await ConsumeCreditBalanceAsync(tenantId, key, consumeFromBalance, ct);

                if (consumeFromPlan > 0)
                {
                    var planCheck = await CheckLimitAsync(tenantId, key, current + consumeFromPlan, ct);
                    if (!planCheck.Allowed) return new BillingConsumeResult(planCheck.Allowed, planCheck.Limit, planCheck.Used, planCheck.Message, null);
                }

                SetUsageValue(usage, key, current + consumeFromPlan);
                usage.UpdatedAtUtc = DateTime.UtcNow;
                await WriteLedgerAsync(
                    tenantId,
                    key,
                    "debit",
                    delta,
                    source ?? string.Empty,
                    normalizedService,
                    referenceId ?? string.Empty,
                    "applied",
                    ct);
                await db.SaveChangesAsync(ct);
                await MaybeSendLowBalanceAlertAsync(tenantId, key, normalizedService, guardrails, ct);
                return new BillingConsumeResult(
                    true,
                    totalAvailable,
                    delta,
                    string.Empty,
                    new BillingConsumeReceipt(tenantId, key, delta, consumeFromBalance, consumeFromPlan, source ?? string.Empty, normalizedService, referenceId ?? string.Empty));
            }
        }

        var next = Math.Max(0, current + delta);
        var check = await CheckLimitAsync(tenantId, key, next, ct);
        if (!check.Allowed) return new BillingConsumeResult(check.Allowed, check.Limit, check.Used, check.Message, null);

        SetUsageValue(usage, key, next);
        usage.UpdatedAtUtc = DateTime.UtcNow;
        await WriteLedgerAsync(
            tenantId,
            key,
            "debit",
            delta,
            source ?? string.Empty,
            normalizedService,
            referenceId ?? string.Empty,
            "applied",
            ct);
        await db.SaveChangesAsync(ct);
        await MaybeSendLowBalanceAlertAsync(tenantId, key, normalizedService, guardrails, ct);
        return new BillingConsumeResult(
            check.Allowed,
            check.Limit,
            check.Used,
            check.Message,
            new BillingConsumeReceipt(tenantId, key, delta, 0, delta, source ?? string.Empty, normalizedService, referenceId ?? string.Empty));
    }

    public async Task RefundConsumptionAsync(BillingConsumeReceipt? receipt, CancellationToken ct = default)
    {
        if (receipt is null) return;
        if (receipt.RequestedUnits <= 0) return;

        if (receipt.ConsumedFromBalance > 0)
        {
            var balance = await GetOrCreateCreditBalanceEntityAsync(receipt.TenantId, receipt.MetricKey, ct);
            balance.UnitsRemaining += receipt.ConsumedFromBalance;
            balance.UpdatedAtUtc = DateTime.UtcNow;
        }

        if (receipt.ConsumedFromPlan > 0)
        {
            var usage = await GetOrCreateUsageAsync(receipt.TenantId, ct);
            var current = await GetCurrentUsageAsync(receipt.TenantId, receipt.MetricKey, ct);
            SetUsageValue(usage, receipt.MetricKey, Math.Max(0, current - receipt.ConsumedFromPlan));
            usage.UpdatedAtUtc = DateTime.UtcNow;
        }

        await WriteLedgerAsync(
            receipt.TenantId,
            receipt.MetricKey,
            "refund",
            receipt.RequestedUnits,
            receipt.Source,
            receipt.Service,
            receipt.ReferenceId,
            "refunded",
            ct);
        await db.SaveChangesAsync(ct);
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

    public async Task AddCreditUnitsAsync(
        Guid tenantId,
        string key,
        int units,
        CancellationToken ct = default,
        string source = "",
        string service = "",
        string referenceId = "",
        string status = "credited")
    {
        if (units <= 0) return;
        var row = await GetOrCreateCreditBalanceEntityAsync(tenantId, key, ct);
        row.UnitsRemaining += units;
        row.UpdatedAtUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(source) || !string.IsNullOrWhiteSpace(service) || !string.IsNullOrWhiteSpace(referenceId))
        {
            await WriteLedgerAsync(tenantId, key, "credit", units, source, service, referenceId, status, ct);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<CreditGuardrailSettings> GetGuardrailsAsync(Guid tenantId, CancellationToken ct = default)
    {
        var scope = $"{GuardrailScopePrefix}:{tenantId:N}";
        var row = await db.PlatformSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Scope == scope && x.Key == GuardrailConfigKey, ct);
        if (row is null || string.IsNullOrWhiteSpace(row.ValueEncrypted))
            return NormalizeGuardrails(new CreditGuardrailSettings());

        try
        {
            var json = crypto.Decrypt(row.ValueEncrypted);
            var parsed = JsonSerializer.Deserialize<CreditGuardrailSettings>(json) ?? new CreditGuardrailSettings();
            return NormalizeGuardrails(parsed);
        }
        catch
        {
            return NormalizeGuardrails(new CreditGuardrailSettings());
        }
    }

    public async Task<CreditGuardrailSettings> UpsertGuardrailsAsync(
        Guid tenantId,
        CreditGuardrailSettings input,
        Guid actorUserId,
        CancellationToken ct = default)
    {
        var normalized = NormalizeGuardrails(input);
        var scope = $"{GuardrailScopePrefix}:{tenantId:N}";
        var row = await db.PlatformSettings
            .FirstOrDefaultAsync(x => x.Scope == scope && x.Key == GuardrailConfigKey, ct);
        if (row is null)
        {
            row = new Textzy.Api.Models.PlatformSetting
            {
                Id = Guid.NewGuid(),
                Scope = scope,
                Key = GuardrailConfigKey
            };
            db.PlatformSettings.Add(row);
        }

        row.ValueEncrypted = crypto.Encrypt(JsonSerializer.Serialize(normalized));
        row.UpdatedByUserId = actorUserId;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return normalized;
    }

    private async Task MaybeSendLowBalanceAlertAsync(
        Guid tenantId,
        string metricKey,
        string service,
        CreditGuardrailSettings guardrails,
        CancellationToken ct)
    {
        if (!guardrails.LowBalanceAlertsEnabled) return;
        var threshold = guardrails.ResolveLowBalanceThreshold(metricKey);
        if (threshold <= 0) return;

        var balance = await GetCreditBalanceAsync(tenantId, metricKey, ct);
        if (balance > threshold) return;

        var scope = $"{GuardrailScopePrefix}:{tenantId:N}";
        var dateKey = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var alertKey = $"low-balance-alert:{metricKey}:{dateKey}";
        var existing = await db.PlatformSettings
            .FirstOrDefaultAsync(x => x.Scope == scope && x.Key == alertKey, ct);
        if (existing is not null) return;

        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId, ct);
        var profile = await db.TenantCompanyProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);
        var toEmail = (profile?.BillingEmail ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            var ownerEmail = await (from tu in db.TenantUsers.AsNoTracking()
                                    join u in db.Users.AsNoTracking() on tu.UserId equals u.Id
                                    where tu.TenantId == tenantId && tu.Role.ToLower() == "owner"
                                    orderby tu.CreatedAtUtc
                                    select u.Email).FirstOrDefaultAsync(ct);
            toEmail = (ownerEmail ?? string.Empty).Trim();
        }

        if (!string.IsNullOrWhiteSpace(toEmail))
        {
            try
            {
                await emailService.SendBillingEventAsync(
                    toEmail,
                    profile?.CompanyName ?? tenant?.Name ?? "Team",
                    profile?.CompanyName ?? tenant?.Name ?? "Workspace",
                    "Low credit balance alert",
                    $"Your {metricKey} balance is low ({balance} remaining).",
                    new Dictionary<string, string>
                    {
                        ["Metric"] = metricKey,
                        ["Balance Remaining"] = balance.ToString(),
                        ["Threshold"] = threshold.ToString(),
                        ["Service"] = string.IsNullOrWhiteSpace(service) ? "-" : service
                    },
                    ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send low-balance alert for tenant {TenantId} metric {MetricKey}", tenantId, metricKey);
            }
        }

        var marker = new Textzy.Api.Models.PlatformSetting
        {
            Id = Guid.NewGuid(),
            Scope = scope,
            Key = alertKey,
            ValueEncrypted = crypto.Encrypt("sent"),
            UpdatedByUserId = Guid.Empty,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.PlatformSettings.Add(marker);
        await db.SaveChangesAsync(ct);
    }

    private Task WriteLedgerAsync(
        Guid tenantId,
        string key,
        string transactionType,
        int units,
        string source,
        string service,
        string referenceId,
        string status,
        CancellationToken ct)
    {
        db.TenantCreditTransactions.Add(new Textzy.Api.Models.TenantCreditTransaction
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            MetricKey = key,
            TransactionType = (transactionType ?? string.Empty).Trim().ToLowerInvariant(),
            Units = Math.Max(0, units),
            Source = (source ?? string.Empty).Trim(),
            Service = (service ?? string.Empty).Trim(),
            ReferenceId = (referenceId ?? string.Empty).Trim(),
            Status = string.IsNullOrWhiteSpace(status) ? "applied" : status.Trim().ToLowerInvariant(),
            CreatedAtUtc = DateTime.UtcNow
        });
        return Task.CompletedTask;
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
            DigilockerKycUsed = 0,
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

    private static CreditGuardrailSettings NormalizeGuardrails(CreditGuardrailSettings input)
    {
        static Dictionary<string, int> NormalizeMap(Dictionary<string, int>? source, bool lowerKey)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (source is null) return result;
            foreach (var (rawKey, rawValue) in source)
            {
                var key = (rawKey ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (lowerKey) key = key.ToLowerInvariant();
                result[key] = Math.Max(0, rawValue);
            }
            return result;
        }

        var defaults = new CreditGuardrailSettings();
        var lowThresholds = NormalizeMap(input.LowBalanceThresholds, lowerKey: false);
        foreach (var (k, v) in defaults.LowBalanceThresholds)
        {
            if (!lowThresholds.ContainsKey(k))
                lowThresholds[k] = Math.Max(0, v);
        }

        return new CreditGuardrailSettings
        {
            LowBalanceAlertsEnabled = input.LowBalanceAlertsEnabled,
            LowBalanceThresholds = lowThresholds,
            AutoTopupEnabled = input.AutoTopupEnabled,
            AutoTopupTriggerThresholds = NormalizeMap(input.AutoTopupTriggerThresholds, lowerKey: false),
            AutoTopupUnits = NormalizeMap(input.AutoTopupUnits, lowerKey: false),
            DailySpendCaps = NormalizeMap(input.DailySpendCaps, lowerKey: false),
            ServiceCreditLimits = NormalizeMap(input.ServiceCreditLimits, lowerKey: true)
        };
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
            case "digilockerKyc": usage.DigilockerKycUsed = value; break;
        }
    }
}
