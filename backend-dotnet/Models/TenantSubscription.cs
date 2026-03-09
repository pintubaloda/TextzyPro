namespace Textzy.Api.Models;

public class TenantSubscription
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PlanId { get; set; }
    public string Status { get; set; } = "active";
    public string BillingCycle { get; set; } = "monthly";
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime RenewAtUtc { get; set; } = DateTime.UtcNow.AddMonths(1);
    public DateTime? CancelledAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
