namespace Textzy.Api.Models;

public class SmsFlow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public int SentCount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
