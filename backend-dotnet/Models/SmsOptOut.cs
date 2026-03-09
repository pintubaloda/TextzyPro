namespace Textzy.Api.Models;

public class SmsOptOut
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Source { get; set; } = "manual";
    public DateTime OptedOutAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

