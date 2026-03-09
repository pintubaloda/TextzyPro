namespace Textzy.Api.Models;

public class MobilePairingRequest
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string PairingTokenHash { get; set; } = string.Empty;
    public string PairingPayloadJson { get; set; } = "{}";
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ConsumedAtUtc { get; set; }
    public Guid? ConsumedDeviceId { get; set; }
}
