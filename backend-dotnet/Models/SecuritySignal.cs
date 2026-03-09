namespace Textzy.Api.Models;

public class SecuritySignal
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public string SignalType { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string Status { get; set; } = "open";
    public int CountValue { get; set; }
    public string Details { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; set; }
}
