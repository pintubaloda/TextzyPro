using Textzy.Api.Services;

namespace Textzy.BillingTests;

public class BillingComputationTests
{
    [Fact]
    public void ComputeInvoiceAmounts_ExclusiveTax_AddsTaxOnTop()
    {
        var result = BillingComputation.ComputeInvoiceAmounts(1000m, 18m, "exclusive", false, false);

        Assert.Equal(1000m, result.Subtotal);
        Assert.Equal(180m, result.TaxAmount);
        Assert.Equal(1180m, result.Total);
    }

    [Fact]
    public void ComputeInvoiceAmounts_GrossAmount_BackCalculatesExclusiveTax()
    {
        var result = BillingComputation.ComputeInvoiceAmounts(1180m, 18m, "exclusive", false, false, amountIsGross: true);

        Assert.Equal(1000m, result.Subtotal);
        Assert.Equal(180m, result.TaxAmount);
        Assert.Equal(1180m, result.Total);
    }

    [Fact]
    public void ComputeInvoiceAmounts_Inclusive_PreservesTotal()
    {
        var result = BillingComputation.ComputeInvoiceAmounts(1180m, 18m, "inclusive", false, false);

        Assert.Equal(1000m, result.Subtotal);
        Assert.Equal(180m, result.TaxAmount);
        Assert.Equal(1180m, result.Total);
    }

    [Fact]
    public void ComputeInvoiceAmounts_ExemptSupply_BlocksTax()
    {
        var result = BillingComputation.ComputeInvoiceAmounts(999m, 18m, "exclusive", true, false);

        Assert.Equal(999m, result.Subtotal);
        Assert.Equal(0m, result.TaxAmount);
        Assert.Equal(999m, result.Total);
    }

    [Theory]
    [InlineData("Growth", "monthly", "subscription", "Growth plan purchase")]
    [InlineData("SMS Pack", "usage_based", "usage_pack", "SMS Pack purchase")]
    [InlineData("Google Authenticator", "monthly", "addon", "Google Authenticator purchase")]
    [InlineData("", "yearly", "subscription", "Yearly subscription purchase")]
    public void ResolveInvoiceDescription_ReturnsExpectedValue(string planName, string billingCycle, string pricingModel, string expected)
    {
        var actual = BillingComputation.ResolveInvoiceDescription(planName, billingCycle, pricingModel);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolvePeriodEndUtc_Yearly_EndsAtFinalSecond()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var yearly = BillingComputation.ResolvePeriodEndUtc(start, "yearly");

        Assert.Equal(new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc), yearly);
    }
}
