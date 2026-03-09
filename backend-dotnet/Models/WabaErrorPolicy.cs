namespace Textzy.Api.Models;

public class WabaErrorPolicy
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Classification { get; set; } = "permanent"; // retryable | permanent
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

