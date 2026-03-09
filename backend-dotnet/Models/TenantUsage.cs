namespace Textzy.Api.Models;

public class TenantUsage
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string MonthKey { get; set; } = string.Empty; // yyyy-MM
    public int WhatsappMessagesUsed { get; set; }
    public int SmsCreditsUsed { get; set; }
    public int ContactsUsed { get; set; }
    public int TeamMembersUsed { get; set; }
    public int ChatbotsUsed { get; set; }
    public int FlowsUsed { get; set; }
    public int ApiCallsUsed { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
