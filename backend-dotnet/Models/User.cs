namespace Textzy.Api.Models;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsSuperAdmin { get; set; }
    public DateTime? EmailVerifiedAtUtc { get; set; }
    public string AuthenticatorSecretEncrypted { get; set; } = string.Empty;
    public string AuthenticatorProvider { get; set; } = string.Empty;
    public DateTime? AuthenticatorEnabledAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
