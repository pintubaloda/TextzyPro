namespace Textzy.Api.Models;

public class BillingInvoice
{
    public Guid Id { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string InvoiceKind { get; set; } = "tax_invoice";
    public string BillingCycle { get; set; } = "monthly";
    public string TaxMode { get; set; } = "exclusive";
    public string ReferenceNo { get; set; } = string.Empty;
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = "paid";
    public DateTime? PaidAtUtc { get; set; }
    public string PdfUrl { get; set; } = string.Empty;
    public string IntegrityAlgo { get; set; } = "SHA256";
    public string IntegrityHash { get; set; } = string.Empty;
    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
