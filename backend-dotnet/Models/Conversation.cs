namespace Textzy.Api.Models;

public class Conversation
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public string AssignedUserId { get; set; } = string.Empty;
    public string AssignedUserName { get; set; } = string.Empty;
    public string LabelsCsv { get; set; } = string.Empty;
    public DateTime LastMessageAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
