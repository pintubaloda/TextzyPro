namespace Textzy.Api.Models;

public class IdempotencyKeyRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Key { get; set; } = string.Empty;
    public Guid? MessageId { get; set; }
    // reserved -> accepted -> failed
    public string Status { get; set; } = "reserved";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddHours(24);
}
