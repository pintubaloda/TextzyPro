namespace Textzy.Api.Models;

public class TenantWabaConfig
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string WabaId { get; set; } = string.Empty;
    public string PhoneNumberId { get; set; } = string.Empty;
    public string BusinessAccountName { get; set; } = string.Empty;
    public string DisplayPhoneNumber { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string BusinessManagerId { get; set; } = string.Empty;
    public string SystemUserId { get; set; } = string.Empty;
    public string SystemUserName { get; set; } = string.Empty;
    public DateTime? SystemUserCreatedAtUtc { get; set; }
    public DateTime? AssetsAssignedAtUtc { get; set; }
    public DateTime? PermanentTokenIssuedAtUtc { get; set; }
    public DateTime? PermanentTokenExpiresAtUtc { get; set; }
    public string TokenSource { get; set; } = "embedded_exchange";
    public bool IsActive { get; set; }
    public DateTime ConnectedAtUtc { get; set; } = DateTime.UtcNow;
    public string OnboardingState { get; set; } = "requested";
    public DateTime? OnboardingStartedAtUtc { get; set; }
    public DateTime? CodeReceivedAtUtc { get; set; }
    public DateTime? ExchangedAtUtc { get; set; }
    public DateTime? AssetsLinkedAtUtc { get; set; }
    public DateTime? WebhookSubscribedAtUtc { get; set; }
    public DateTime? WebhookVerifiedAtUtc { get; set; }
    public string LastError { get; set; } = string.Empty;
    public string LastGraphError { get; set; } = string.Empty;
    public string BusinessVerificationStatus { get; set; } = string.Empty;
    public string PhoneQualityRating { get; set; } = string.Empty;
    public string PhoneNameStatus { get; set; } = string.Empty;
    public string MessagingLimitTier { get; set; } = string.Empty;
    public string AccountHealth { get; set; } = string.Empty;
    public bool PermissionAuditPassed { get; set; }
    public DateTime? TemplatesSyncedAtUtc { get; set; }
    public string TemplatesSyncStatus { get; set; } = string.Empty;
    public int TemplatesSyncFailCount { get; set; }
}
