namespace Textzy.Api.Models;

public class BroadcastJob
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public ChannelType Channel { get; set; }
    public string MessageBody { get; set; } = string.Empty;
    public string RecipientCsv { get; set; } = string.Empty;
    public string Status { get; set; } = "Queued";
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;
    public int SentCount { get; set; }
    public int FailedCount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
