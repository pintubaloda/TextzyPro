using System.Text.RegularExpressions;

namespace Textzy.Api.Services;

public readonly record struct BillingInvoiceAmounts(decimal Subtotal, decimal TaxAmount, decimal Total);

public static class BillingComputation
{
    public static BillingInvoiceAmounts ComputeInvoiceAmounts(
        decimal planAmount,
        decimal taxRatePercent,
        string? taxMode,
        bool isTaxExempt,
        bool isReverseCharge,
        bool amountIsGross = false)
    {
        var normalizedTaxMode = string.Equals(taxMode, "inclusive", StringComparison.OrdinalIgnoreCase) ? "inclusive" : "exclusive";
        var taxBlocked = isTaxExempt || isReverseCharge || taxRatePercent <= 0m;
        var roundedAmount = Math.Round(planAmount, 2, MidpointRounding.AwayFromZero);
        if (taxBlocked)
            return new BillingInvoiceAmounts(roundedAmount, 0m, roundedAmount);

        if (normalizedTaxMode == "inclusive")
        {
            var subtotalInclusive = Math.Round(roundedAmount / (1m + (taxRatePercent / 100m)), 2, MidpointRounding.AwayFromZero);
            var taxInclusive = Math.Round(roundedAmount - subtotalInclusive, 2, MidpointRounding.AwayFromZero);
            return new BillingInvoiceAmounts(subtotalInclusive, taxInclusive, roundedAmount);
        }

        if (amountIsGross)
        {
            var subtotalGross = Math.Round(roundedAmount / (1m + (taxRatePercent / 100m)), 2, MidpointRounding.AwayFromZero);
            var taxGross = Math.Round(roundedAmount - subtotalGross, 2, MidpointRounding.AwayFromZero);
            return new BillingInvoiceAmounts(subtotalGross, taxGross, roundedAmount);
        }

        var subtotalExclusive = roundedAmount;
        var taxExclusive = Math.Round(subtotalExclusive * (taxRatePercent / 100m), 2, MidpointRounding.AwayFromZero);
        return new BillingInvoiceAmounts(subtotalExclusive, taxExclusive, subtotalExclusive + taxExclusive);
    }

    public static string ResolveInvoiceDescription(string? planName, string? billingCycle, string? pricingModel)
    {
        var name = (planName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            if (string.Equals(pricingModel, "usage_pack", StringComparison.OrdinalIgnoreCase))
                return $"{name} purchase";

            if (name.Contains("authenticator", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("integration", StringComparison.OrdinalIgnoreCase))
                return $"{name} purchase";

            return $"{name} plan purchase";
        }

        return (billingCycle ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "yearly" => "Yearly subscription purchase",
            "monthly" => "Monthly subscription purchase",
            "lifetime" => "Lifetime plan purchase",
            "usage_based" => "Usage pack purchase",
            _ => "Platform service purchase"
        };
    }

    public static string? NormalizeBillingCycle(string? billingCycle)
    {
        var cycle = (billingCycle ?? "monthly").Trim().ToLowerInvariant();
        return cycle switch
        {
            "monthly" => "monthly",
            "yearly" => "yearly",
            "lifetime" => "lifetime",
            "usage_based" => "usage_based",
            "usagebased" => "usage_based",
            _ => null
        };
    }

    public static DateTime ResolveRenewAtUtc(DateTime startUtc, string cycle)
    {
        return cycle switch
        {
            "yearly" => startUtc.AddYears(1),
            "lifetime" => DateTime.MaxValue,
            "usage_based" => DateTime.MaxValue,
            _ => startUtc.AddMonths(1)
        };
    }

    public static DateTime ResolvePeriodEndUtc(DateTime periodStartUtc, string cycle)
    {
        return cycle switch
        {
            "yearly" => periodStartUtc.AddYears(1).AddSeconds(-1),
            "lifetime" => periodStartUtc.AddYears(100).AddSeconds(-1),
            "usage_based" => periodStartUtc.AddMonths(1).AddSeconds(-1),
            _ => periodStartUtc.AddMonths(1).AddSeconds(-1)
        };
    }

    public static DateTime ResolveAddOnPeriodEndUtc(DateTime periodStartUtc, string? billingCycle)
    {
        return string.Equals(billingCycle, "one_time", StringComparison.OrdinalIgnoreCase)
            ? periodStartUtc.AddYears(100).AddSeconds(-1)
            : ResolvePeriodEndUtc(periodStartUtc, NormalizeBillingCycle(billingCycle) ?? "monthly");
    }

    public static string BuildRazorpayReceipt(string kind, Guid tenantId)
    {
        var safeKind = string.IsNullOrWhiteSpace(kind) ? "txn" : Regex.Replace(kind.Trim().ToLowerInvariant(), @"[^a-z0-9]", string.Empty);
        if (safeKind.Length > 4) safeKind = safeKind[..4];
        var tenant = tenantId.ToString("N")[..8];
        var stamp = DateTime.UtcNow.ToString("yyMMddHHmmss");
        return $"tz{safeKind}{tenant}{stamp}";
    }
}
