using System.Text;
using Textzy.Api.Models;
using Textzy.Api.Services;

namespace Textzy.BillingTests;

public class InvoiceArtifactsTests
{
    [Fact]
    public void InvoiceIntegrityHasher_ChangesWhenInvoiceContentChanges()
    {
        var first = CreateInvoice();
        var second = CreateInvoice();
        second.Description = "Changed description";

        var firstHash = InvoiceIntegrityHasher.Compute(first);
        var secondHash = InvoiceIntegrityHasher.Compute(second);

        Assert.NotEqual(firstHash, secondHash);
    }

    [Fact]
    public void InvoiceIntegrityHasher_NormalizesEquivalentTimestamps()
    {
        var a = new DateTime(2026, 3, 9, 10, 11, 12, 123, DateTimeKind.Utc).AddTicks(4);
        var b = a.AddTicks(5);

        var normalizedA = InvoiceIntegrityHasher.NormalizeTimestamp(a);
        var normalizedB = InvoiceIntegrityHasher.NormalizeTimestamp(b);

        Assert.Equal(normalizedA, normalizedB);
    }

    [Fact]
    public void InvoicePdfRenderer_BuildsPdfPayload()
    {
        var invoice = CreateInvoice();

        var pdf = InvoicePdfRenderer.BuildInvoicePdf(
            invoice,
            new InvoiceSellerProfile
            {
                PlatformName = "Textzy",
                LegalName = "Textzy Digital Solutions Private Limited",
                Address = "Mumbai, India",
                Gstin = "27AAFCU5055K1ZO",
                Pan = "AAFCU5055K",
                Cin = "U12345MH2020PTC000001",
                BillingEmail = "billing@textzy.com",
                BillingPhone = "+91-9999999999",
                Website = "https://textzy.com",
                InvoiceFooter = "Computer generated invoice."
            },
            new InvoiceBuyerProfile
            {
                CompanyName = "Acme",
                LegalName = "Acme Pvt Ltd",
                BillingEmail = "accounts@acme.test",
                Address = "Bengaluru, India",
                Gstin = "29ABCDE1234F1Z5",
                Pan = "ABCDE1234F"
            },
            18m,
            false,
            false,
            "https://example.com/invoice/verify?id=123");

        Assert.True(pdf.Length > 100);
        var header = Encoding.ASCII.GetString(pdf, 0, Math.Min(pdf.Length, 8));
        Assert.StartsWith("%PDF-1.4", header, StringComparison.Ordinal);
    }

    private static BillingInvoice CreateInvoice()
    {
        return new BillingInvoice
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            InvoiceNo = "INV-20260309-001",
            TenantId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            InvoiceKind = "tax_invoice",
            BillingCycle = "monthly",
            TaxMode = "exclusive",
            ReferenceNo = "order_123",
            Description = "Growth plan purchase",
            PeriodStartUtc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodEndUtc = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc),
            Subtotal = 1000m,
            TaxAmount = 180m,
            Total = 1180m,
            Status = "paid",
            PaidAtUtc = new DateTime(2026, 3, 9, 10, 0, 0, DateTimeKind.Utc),
            IntegrityAlgo = "SHA256",
            IntegrityHash = "hash",
            IssuedAtUtc = new DateTime(2026, 3, 9, 10, 0, 0, DateTimeKind.Utc),
            CreatedAtUtc = new DateTime(2026, 3, 9, 10, 0, 0, DateTimeKind.Utc)
        };
    }
}
