namespace Textzy.Api.Models;

public class SupportTicketMessage
{
    public Guid Id { get; set; }
    public Guid TicketId { get; set; }
    public Guid TenantId { get; set; }
    public Guid AuthorUserId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorEmail { get; set; } = string.Empty;
    public string AuthorType { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
