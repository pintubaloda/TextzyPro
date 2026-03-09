namespace Textzy.Api.Models;

public class TenantSecurityControl
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public bool CircuitBreakerEnabled { get; set; }
    public int RatePerMinuteOverride { get; set; } = 0;
    public string Reason { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid UpdatedByUserId { get; set; }
}
