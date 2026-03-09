using System.Security.Cryptography;
using System.Text;
using Textzy.Api.Models;

namespace Textzy.Api.Services;

public static class InvoiceIntegrityHasher
{
    public static string Compute(BillingInvoice invoice)
    {
        var canonical = string.Join("|",
            invoice.InvoiceNo ?? string.Empty,
            invoice.TenantId.ToString("D"),
            invoice.InvoiceKind ?? string.Empty,
            invoice.BillingCycle ?? string.Empty,
            invoice.TaxMode ?? string.Empty,
            invoice.ReferenceNo ?? string.Empty,
            invoice.Description ?? string.Empty,
            NormalizeTimestamp(invoice.PeriodStartUtc).ToString("O"),
            NormalizeTimestamp(invoice.PeriodEndUtc).ToString("O"),
            invoice.Subtotal.ToString("0.00"),
            invoice.TaxAmount.ToString("0.00"),
            invoice.Total.ToString("0.00"),
            NormalizeTimestamp(invoice.PaidAtUtc ?? DateTime.MinValue).ToString("O"),
            invoice.Status ?? string.Empty,
            NormalizeTimestamp(invoice.IssuedAtUtc).ToString("O"));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static string ComputeLegacy(BillingInvoice invoice)
    {
        var canonical = string.Join("|",
            invoice.InvoiceNo ?? string.Empty,
            invoice.TenantId.ToString("D"),
            invoice.InvoiceKind ?? string.Empty,
            invoice.BillingCycle ?? string.Empty,
            invoice.TaxMode ?? string.Empty,
            invoice.ReferenceNo ?? string.Empty,
            NormalizeTimestamp(invoice.PeriodStartUtc).ToString("O"),
            NormalizeTimestamp(invoice.PeriodEndUtc).ToString("O"),
            invoice.Subtotal.ToString("0.00"),
            invoice.TaxAmount.ToString("0.00"),
            invoice.Total.ToString("0.00"),
            NormalizeTimestamp(invoice.PaidAtUtc ?? DateTime.MinValue).ToString("O"),
            invoice.Status ?? string.Empty,
            NormalizeTimestamp(invoice.IssuedAtUtc).ToString("O"));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static DateTime NormalizeTimestamp(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value.ToUniversalTime()
        };

        var normalizedTicks = utc.Ticks - (utc.Ticks % 10);
        return new DateTime(normalizedTicks, DateTimeKind.Utc);
    }
}
