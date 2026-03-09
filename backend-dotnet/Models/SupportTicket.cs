namespace Textzy.Api.Models;

public class SupportTicket
{
    public Guid Id { get; set; }
    public string TicketNo { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public Guid? OwnerGroupId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string TenantSlug { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string CreatedByName { get; set; } = string.Empty;
    public string CreatedByEmail { get; set; } = string.Empty;
    public string ServiceKey { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Status { get; set; } = "open";
    public string Priority { get; set; } = "normal";
    public string LastMessagePreview { get; set; } = string.Empty;
    public string LastActorType { get; set; } = "customer";
    public Guid? ClosedByUserId { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public Guid? ReopenedByUserId { get; set; }
    public DateTime? ReopenedAtUtc { get; set; }
    public DateTime LastMessageAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
