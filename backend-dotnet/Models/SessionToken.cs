namespace Textzy.Api.Models;

public class SessionToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string CreatedIpAddress { get; set; } = string.Empty;
    public string LastSeenIpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string DeviceLabel { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastSeenAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public DateTime? TwoFactorVerifiedAtUtc { get; set; }
    public DateTime? StepUpVerifiedAtUtc { get; set; }
    public bool IsRevoked => RevokedAtUtc.HasValue;
}
