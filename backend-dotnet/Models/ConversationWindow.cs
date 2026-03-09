namespace Textzy.Api.Models;

public class ConversationWindow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Recipient { get; set; } = string.Empty;
    public DateTime LastInboundAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
