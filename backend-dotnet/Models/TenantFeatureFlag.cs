namespace Textzy.Api.Models;

public class TenantFeatureFlag
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string FeatureKey { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid UpdatedByUserId { get; set; }
}
