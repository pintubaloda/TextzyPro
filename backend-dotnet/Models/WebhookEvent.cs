namespace Textzy.Api.Models;

public class WebhookEvent
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string EventKey { get; set; } = string.Empty;
    public Guid? TenantId { get; set; }
    public string PhoneNumberId { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string Status { get; set; } = "Queued";
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;
    public string LastError { get; set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAtUtc { get; set; }
    public DateTime? DeadLetteredAtUtc { get; set; }
}

