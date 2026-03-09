namespace Textzy.Api.Models;

public class AutomationUsageCounter
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime BucketDateUtc { get; set; } = DateTime.UtcNow.Date;
    public int RunCount { get; set; }
    public int ApiCallCount { get; set; }
    public int ActiveFlowCount { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
