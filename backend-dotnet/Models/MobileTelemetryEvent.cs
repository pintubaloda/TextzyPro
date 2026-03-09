namespace Textzy.Api.Models;

public class MobileTelemetryEvent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid? DeviceId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string DataJson { get; set; } = "{}";
    public DateTime EventAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
