namespace Textzy.Api.Models;

public class Message
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? CampaignId { get; set; }
    public ChannelType Channel { get; set; }
    public string Recipient { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string MessageType { get; set; } = "session";
    public DateTime? DeliveredAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public string ProviderMessageId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public int RetryCount { get; set; }
    public DateTime? NextRetryAtUtc { get; set; }
    public string LastError { get; set; } = string.Empty;
    public string QueueProvider { get; set; } = "memory";
    public string Status { get; set; } = "Queued";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
