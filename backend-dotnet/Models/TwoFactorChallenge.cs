namespace Textzy.Api.Models;

public class TwoFactorChallenge
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public Guid? SessionTokenId { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ActionCode { get; set; } = string.Empty;
    public string ChallengeTokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? VerifiedAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
}
