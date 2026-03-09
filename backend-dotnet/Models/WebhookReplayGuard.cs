namespace Textzy.Api.Models;

public class WebhookReplayGuard
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ReplayKey { get; set; } = string.Empty;
    public DateTime FirstSeenAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddDays(2);
}
