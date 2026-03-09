namespace Textzy.Api.Models;

public class BillingPaymentAttempt
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PlanId { get; set; }
    public string BillingCycle { get; set; } = "monthly";
    public string Provider { get; set; } = "razorpay";
    public string OrderId { get; set; } = string.Empty;
    public string PaymentId { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "INR";
    public string Status { get; set; } = "created";
    public string NotesJson { get; set; } = "{}";
    public string RawResponse { get; set; } = string.Empty;
    public string LastError { get; set; } = string.Empty;
    public DateTime? PaidAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
