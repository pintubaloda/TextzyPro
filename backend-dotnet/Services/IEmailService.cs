namespace Textzy.Api.Services;

public interface IEmailService
{
    Task SendInviteAsync(string toEmail, string toName, string inviteUrl, CancellationToken ct = default);

    Task SendVerificationOtpAsync(
        string toEmail,
        string displayName,
        string otp,
        string verificationCode,
        int expiryMinutes,
        string purpose,
        CancellationToken ct = default);

    Task SendVerificationActionAsync(
        string toEmail,
        string displayName,
        string purpose,
        string verifyLink,
        int linkExpiryMinutes,
        CancellationToken ct = default);

    Task SendBillingEventAsync(
        string toEmail,
        string displayName,
        string companyName,
        string eventTitle,
        string eventDescription,
        Dictionary<string, string>? details = null,
        CancellationToken ct = default,
        IReadOnlyCollection<EmailAttachment>? attachments = null);
}
