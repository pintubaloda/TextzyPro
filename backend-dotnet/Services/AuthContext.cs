namespace Textzy.Api.Services;

public class AuthContext
{
    public Guid SessionId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid TenantId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public string Role { get; private set; } = string.Empty;
    public IReadOnlyList<string> Permissions { get; private set; } = [];
    public DateTime? TwoFactorVerifiedAtUtc { get; private set; }
    public DateTime? StepUpVerifiedAtUtc { get; private set; }
    public bool IsAuthenticated => UserId != Guid.Empty;

    public void Set(
        Guid userId,
        Guid tenantId,
        string email,
        string role,
        IReadOnlyList<string>? permissions = null,
        string? fullName = null,
        Guid? sessionId = null,
        DateTime? twoFactorVerifiedAtUtc = null,
        DateTime? stepUpVerifiedAtUtc = null)
    {
        SessionId = sessionId ?? Guid.Empty;
        UserId = userId;
        TenantId = tenantId;
        Email = email;
        FullName = fullName ?? string.Empty;
        Role = role;
        Permissions = permissions ?? RolePermissionCatalog.GetPermissions(role);
        TwoFactorVerifiedAtUtc = twoFactorVerifiedAtUtc;
        StepUpVerifiedAtUtc = stepUpVerifiedAtUtc;
    }
}
