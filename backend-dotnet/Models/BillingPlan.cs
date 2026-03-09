namespace Textzy.Api.Models;

public class BillingPlan
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PricingModel { get; set; } = "subscription";
    public decimal PriceMonthly { get; set; }
    public decimal PriceYearly { get; set; }
    public string TaxMode { get; set; } = "exclusive";
    public string UsageUnitName { get; set; } = string.Empty;
    public int IncludedQuantity { get; set; }
    public string Currency { get; set; } = "INR";
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public string FeaturesJson { get; set; } = "[]";
    public string LimitsJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
