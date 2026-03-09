namespace Textzy.Api.Models;

public class ChatbotConfig
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Greeting { get; set; } = "Hi! Welcome.";
    public string Fallback { get; set; } = "Agent will join shortly.";
    public bool HandoffEnabled { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
