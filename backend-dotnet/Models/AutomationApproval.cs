namespace Textzy.Api.Models;

public class AutomationApproval
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid FlowId { get; set; }
    public Guid VersionId { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public string RequestedByRole { get; set; } = string.Empty;
    public string Status { get; set; } = "pending"; // pending/approved/rejected
    public string DecisionComment { get; set; } = string.Empty;
    public string DecidedBy { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAtUtc { get; set; }
}
