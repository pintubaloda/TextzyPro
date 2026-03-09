namespace Textzy.Api.Models;

public class AutomationFlow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Channel { get; set; } = "waba";
    public string TriggerType { get; set; } = "keyword";
    public string TriggerConfigJson { get; set; } = "{}";
    public bool IsActive { get; set; } = true;
    public string LifecycleStatus { get; set; } = "draft";
    public Guid? CurrentVersionId { get; set; }
    public Guid? PublishedVersionId { get; set; }
    public DateTime? LastPublishedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
