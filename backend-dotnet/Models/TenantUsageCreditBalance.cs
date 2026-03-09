namespace Textzy.Api.Models;

public class TenantUsageCreditBalance
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string MetricKey { get; set; } = string.Empty;
    public int UnitsRemaining { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
