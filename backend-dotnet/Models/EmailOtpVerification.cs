namespace Textzy.Api.Models;

public class EmailOtpVerification
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Purpose { get; set; } = "login";
    public string Status { get; set; } = "email_sent";
    public string OtpHash { get; set; } = string.Empty;
    public string OtpDisplayEncrypted { get; set; } = string.Empty;
    public string VerificationCode { get; set; } = string.Empty;
    public string LinkTokenHash { get; set; } = string.Empty;
    public bool IsVerified { get; set; }
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 5;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? OtpExpiresAtUtc { get; set; }
    public DateTime? LinkOpenedAtUtc { get; set; }
    public DateTime? OtpIssuedAtUtc { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public DateTime LastSentAtUtc { get; set; } = DateTime.UtcNow;
}
