namespace Textzy.Api.Models;

public class SmsBillingLedger
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid MessageId { get; set; }
    public string Recipient { get; set; } = string.Empty;
    public string ProviderMessageId { get; set; } = string.Empty;
    public string Currency { get; set; } = "INR";
    public decimal UnitPrice { get; set; }
    public int Segments { get; set; } = 1;
    public decimal TotalAmount { get; set; }
    public string BillingState { get; set; } = "charged";
    public string DeliveryState { get; set; } = "submitted";
    public string Notes { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}

