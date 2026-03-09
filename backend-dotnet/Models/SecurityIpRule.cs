namespace Textzy.Api.Models;

public class SecurityIpRule
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public string Scope { get; set; } = "session";
    public string RuleType { get; set; } = "allow";
    public string IpRule { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}
