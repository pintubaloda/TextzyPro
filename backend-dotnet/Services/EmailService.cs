using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;

namespace Textzy.Api.Services;

public class EmailService(
    IConfiguration config,
    ControlDbContext db,
    SecretCryptoService crypto,
    IHttpClientFactory httpClientFactory) : IEmailService
{
    private sealed class EmailRuntimeSettings
    {
        public string Provider { get; init; } = "smtp";
        public int TimeoutMs { get; init; } = 15000;
        public string Host { get; init; } = string.Empty;
        public int Port { get; init; } = 587;
        public bool EnableSsl { get; init; } = true;
        public string Username { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public string FromEmail { get; init; } = string.Empty;
        public string FromName { get; init; } = "Textzy";
        public string ResendApiKey { get; init; } = string.Empty;
    }

    private static int ParseTimeoutMs(string? raw)
    {
        if (!int.TryParse(raw, out var ms)) ms = 15000;
        if (ms < 3000) ms = 3000;
        if (ms > 60000) ms = 60000;
        return ms;
    }

    private static string Pick(Dictionary<string, string> values, string key, params string?[] fallbacks)
    {
        if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value.Trim();
        foreach (var f in fallbacks)
        {
            if (!string.IsNullOrWhiteSpace(f)) return f.Trim();
        }
        return string.Empty;
    }

    private async Task<EmailRuntimeSettings> GetRuntimeSettingsAsync(CancellationToken ct)
    {
        var values = await db.PlatformSettings
            .AsNoTracking()
            .Where(x => x.Scope == "smtp")
            .ToListAsync(ct);

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in values)
            map[row.Key] = crypto.Decrypt(row.ValueEncrypted);

        var provider = Pick(
            map,
            "provider",
            config["Email:Provider"],
            config["EMAIL_PROVIDER"],
            config["Smtp:Provider"],
            config["SMTP_PROVIDER"],
            "smtp").ToLowerInvariant();

        var timeoutMs = ParseTimeoutMs(Pick(map, "timeoutMs", config["Smtp:TimeoutMs"], config["SMTP_TIMEOUT_MS"], "15000"));
        var fromEmail = Pick(map, "fromEmail", config["Smtp:FromEmail"], config["SMTP_FROM_EMAIL"]);
        var fromName = Pick(map, "fromName", config["Smtp:FromName"], config["SMTP_FROM_NAME"], "Textzy");

        if (provider == "resend")
        {
            var resendApiKey = Pick(map, "resendApiKey", config["Resend:ApiKey"], config["RESEND_API_KEY"]);
            var resendFromEmail = Pick(map, "resendFromEmail", fromEmail);
            var resendFromName = Pick(map, "resendFromName", fromName, "Textzy");
            return new EmailRuntimeSettings
            {
                Provider = "resend",
                TimeoutMs = timeoutMs,
                FromEmail = resendFromEmail,
                FromName = resendFromName,
                ResendApiKey = resendApiKey
            };
        }

        var host = Pick(map, "host", config["Smtp:Host"], config["SMTP_HOST"]);
        var portRaw = Pick(map, "port", config["Smtp:Port"], config["SMTP_PORT"], "587");
        var username = Pick(map, "username", config["Smtp:Username"], config["SMTP_USERNAME"]);
        var password = Pick(map, "password", config["Smtp:Password"], config["SMTP_PASSWORD"]);
        var enableSslRaw = Pick(map, "enableSsl", config["Smtp:EnableSsl"], config["SMTP_ENABLE_SSL"], "true");

        return new EmailRuntimeSettings
        {
            Provider = "smtp",
            TimeoutMs = timeoutMs,
            Host = host,
            Port = int.TryParse(portRaw, out var p) ? p : 587,
            EnableSsl = !bool.TryParse(enableSslRaw, out var ssl) || ssl,
            Username = username,
            Password = password,
            FromEmail = fromEmail,
            FromName = fromName
        };
    }

    private static async Task SendWithTimeoutAsync(SmtpClient client, MailMessage msg, int timeoutMs, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs);
        using var reg = timeoutCts.Token.Register(() => client.SendAsyncCancel());

        var sendTask = client.SendMailAsync(msg);
        var completed = await Task.WhenAny(sendTask, Task.Delay(timeoutMs, ct));
        if (completed != sendTask)
            throw new TimeoutException($"SMTP send timed out after {timeoutMs}ms.");

        await sendTask;
    }

    private async Task SendViaSmtpAsync(
        EmailRuntimeSettings settings,
        string toEmail,
        string subject,
        string htmlBody,
        string plainBody,
        CancellationToken ct,
        IReadOnlyCollection<EmailAttachment>? attachments = null)
    {
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.FromEmail))
            throw new InvalidOperationException("SMTP is not configured. Set SMTP_HOST and SMTP_FROM_EMAIL.");

        using var msg = new MailMessage
        {
            From = new MailAddress(settings.FromEmail, settings.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        msg.To.Add(new MailAddress(toEmail));
        if (!string.IsNullOrWhiteSpace(plainBody))
            msg.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(plainBody, null, "text/plain"));
        if (attachments is not null)
        {
            foreach (var attachment in attachments.Where(x => x.ContentBytes.Length > 0))
            {
                msg.Attachments.Add(new Attachment(new MemoryStream(attachment.ContentBytes, writable: false), attachment.FileName, attachment.ContentType));
            }
        }

        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = settings.TimeoutMs
        };

        if (!string.IsNullOrWhiteSpace(settings.Username))
            client.Credentials = new NetworkCredential(settings.Username, settings.Password ?? string.Empty);

        await SendWithTimeoutAsync(client, msg, settings.TimeoutMs, ct);
    }

    private async Task SendViaResendAsync(
        EmailRuntimeSettings settings,
        string toEmail,
        string subject,
        string htmlBody,
        string plainBody,
        CancellationToken ct,
        IReadOnlyCollection<EmailAttachment>? attachments = null)
    {
        if (string.IsNullOrWhiteSpace(settings.ResendApiKey) || string.IsNullOrWhiteSpace(settings.FromEmail))
            throw new InvalidOperationException("Resend is not configured. Set resendApiKey and resendFromEmail.");

        var from = string.IsNullOrWhiteSpace(settings.FromName)
            ? settings.FromEmail
            : $"{settings.FromName} <{settings.FromEmail}>";

        var payload = new Dictionary<string, object?>
        {
            ["from"] = from,
            ["to"] = new[] { toEmail },
            ["subject"] = subject,
            ["html"] = htmlBody,
            ["text"] = plainBody
        };
        if (attachments is not null && attachments.Count > 0)
        {
            payload["attachments"] = attachments
                .Where(x => x.ContentBytes.Length > 0)
                .Select(x => new
                {
                    filename = x.FileName,
                    content = Convert.ToBase64String(x.ContentBytes)
                })
                .ToArray();
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ResendApiKey);
        req.Content = new StringContent(
            JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            }),
            Encoding.UTF8,
            "application/json");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(settings.TimeoutMs);

        var client = httpClientFactory.CreateClient();
        var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        var body = await res.Content.ReadAsStringAsync(timeoutCts.Token);
        if (!res.IsSuccessStatusCode)
        {
            var err = string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)res.StatusCode}" : body;
            throw new InvalidOperationException($"Resend API failed: {err}");
        }
    }

    private async Task SendEmailAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string plainBody,
        CancellationToken ct,
        IReadOnlyCollection<EmailAttachment>? attachments = null)
    {
        var settings = await GetRuntimeSettingsAsync(ct);
        if (settings.Provider == "resend")
        {
            await SendViaResendAsync(settings, toEmail, subject, htmlBody, plainBody, ct, attachments);
            return;
        }

        await SendViaSmtpAsync(settings, toEmail, subject, htmlBody, plainBody, ct, attachments);
    }

    public async Task SendInviteAsync(string toEmail, string toName, string inviteUrl, CancellationToken ct = default)
    {
        var html = $"""
            <!doctype html>
            <html lang="en">
            <body style="margin:0;padding:0;background:#f7f7fb;font-family:Segoe UI,Arial,sans-serif;color:#111827;">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="padding:24px 12px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#ffffff;border-radius:14px;overflow:hidden;box-shadow:0 8px 30px rgba(17,24,39,.08);">
                      <tr>
                        <td style="background:#f97316;padding:18px 24px;color:#fff;font-weight:700;font-size:20px;">Textzy Invite</td>
                      </tr>
                      <tr>
                        <td style="padding:24px;">
                          <p style="margin:0 0 12px;">Hello {(string.IsNullOrWhiteSpace(toName) ? "there" : WebUtility.HtmlEncode(toName))},</p>
                          <p style="margin:0 0 16px;line-height:1.6;">You are invited to join a Textzy project.</p>
                          <div style="text-align:center;margin:18px 0 22px;">
                            <a href="{WebUtility.HtmlEncode(inviteUrl)}" style="display:inline-block;background:#f97316;color:#fff;text-decoration:none;padding:12px 22px;border-radius:10px;font-weight:700;">Accept Invite</a>
                          </div>
                          <p style="margin:0;color:#4b5563;">This link expires in <strong>7 days</strong>.</p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:14px 24px;background:#f9fafb;color:#6b7280;font-size:12px;">
                          Powered by Moneyart Private Limited
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        var plain = $"""
            Hello {(string.IsNullOrWhiteSpace(toName) ? "there" : toName)},

            You are invited to join a Textzy project.

            Accept invite: {inviteUrl}

            This link expires in 7 days.
            """;

        await SendEmailAsync(toEmail, "You are invited to join Textzy project", html, plain, ct);
    }

    public async Task SendVerificationOtpAsync(
        string toEmail,
        string displayName,
        string otp,
        string verificationCode,
        int expiryMinutes,
        string purpose,
        CancellationToken ct = default)
    {
        var safeName = string.IsNullOrWhiteSpace(displayName) ? "there" : WebUtility.HtmlEncode(displayName);
        var safeOtp = WebUtility.HtmlEncode(otp);
        var safeCode = WebUtility.HtmlEncode(verificationCode);
        var safePurpose = WebUtility.HtmlEncode(purpose);
        var html = $"""
            <!doctype html>
            <html lang="en">
            <body style="margin:0;padding:0;background:#f7f7fb;font-family:Segoe UI,Arial,sans-serif;color:#111827;">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="padding:24px 12px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#ffffff;border-radius:14px;overflow:hidden;box-shadow:0 8px 30px rgba(17,24,39,.08);">
                      <tr>
                        <td style="background:#f97316;padding:18px 24px;color:#fff;font-weight:700;font-size:20px;">Textzy Verification</td>
                      </tr>
                      <tr>
                        <td style="padding:24px;">
                          <p style="margin:0 0 12px;">Hi {safeName},</p>
                          <p style="margin:0 0 16px;line-height:1.6;">Use this one-time password (OTP) to verify your email for <strong>{safePurpose}</strong>.</p>
                          <div style="text-align:center;margin:18px 0;">
                            <div style="display:inline-block;padding:12px 24px;border:1px dashed #f97316;border-radius:10px;font-size:30px;font-weight:800;letter-spacing:6px;color:#111827;">{safeOtp}</div>
                          </div>
                          <p style="margin:0 0 8px;color:#4b5563;">Verification code: <strong>{safeCode}</strong></p>
                          <p style="margin:0 0 8px;color:#4b5563;">This OTP expires in <strong>{expiryMinutes} minutes</strong>.</p>
                          <p style="margin:0;color:#dc2626;font-size:13px;">Do not share this OTP with anyone.</p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:14px 24px;background:#f9fafb;color:#6b7280;font-size:12px;">
                          Powered by Moneyart Private Limited
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        var plain = $"""
            Hi {(string.IsNullOrWhiteSpace(displayName) ? "there" : displayName)},

            Your Textzy OTP for {purpose}: {otp}
            Verification code: {verificationCode}
            Expires in {expiryMinutes} minutes.

            Do not share this OTP with anyone.
            """;
        await SendEmailAsync(toEmail, $"Textzy OTP: {otp}", html, plain, ct);
    }

    public async Task SendVerificationActionAsync(
        string toEmail,
        string displayName,
        string purpose,
        string verifyLink,
        int linkExpiryMinutes,
        CancellationToken ct = default)
    {
        var safeName = string.IsNullOrWhiteSpace(displayName) ? "there" : WebUtility.HtmlEncode(displayName);
        var safePurpose = WebUtility.HtmlEncode(purpose);
        var safeVerifyLink = WebUtility.HtmlEncode(verifyLink);
        var html = $"""
            <!doctype html>
            <html lang="en">
            <body style="margin:0;padding:0;background:#f7f7fb;font-family:Segoe UI,Arial,sans-serif;color:#111827;">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="padding:24px 12px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#ffffff;border-radius:14px;overflow:hidden;box-shadow:0 8px 30px rgba(17,24,39,.08);">
                      <tr>
                        <td style="background:#f97316;padding:18px 24px;color:#fff;font-weight:700;font-size:20px;">Textzy Verification</td>
                      </tr>
                      <tr>
                        <td style="padding:24px;">
                          <p style="margin:0 0 12px;">Hi {safeName},</p>
                          <p style="margin:0 0 16px;line-height:1.6;">
                            Click <strong>Verify Now</strong> to continue <strong>{safePurpose}</strong>. After clicking, a code will be shown in your browser tab.
                          </p>
                          <div style="text-align:center;margin:18px 0 22px;">
                            <a href="{safeVerifyLink}" style="display:inline-block;background:#f97316;color:#fff;text-decoration:none;padding:12px 22px;border-radius:10px;font-weight:700;">Verify Now</a>
                          </div>
                          <p style="margin:0 0 8px;color:#4b5563;">Link expires in <strong>{linkExpiryMinutes} minutes</strong>.</p>
                          <p style="margin:0;color:#dc2626;font-size:13px;">Do not share this email link with anyone.</p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:14px 24px;background:#f9fafb;color:#6b7280;font-size:12px;">
                          Powered by Moneyart Private Limited
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        var plain = $"""
            Hi {(string.IsNullOrWhiteSpace(displayName) ? "there" : displayName)},

            Click this Verify Now link to continue {purpose}:
            {verifyLink}

            This link expires in {linkExpiryMinutes} minutes.
            """;        
        await SendEmailAsync(toEmail, $"Textzy verification required ({purpose})", html, plain, ct);
    }

    public async Task SendBillingEventAsync(
        string toEmail,
        string displayName,
        string companyName,
        string eventTitle,
        string eventDescription,
        Dictionary<string, string>? details = null,
        CancellationToken ct = default,
        IReadOnlyCollection<EmailAttachment>? attachments = null)
    {
        if (string.IsNullOrWhiteSpace(toEmail)) return;
        var safeName = string.IsNullOrWhiteSpace(displayName) ? "there" : WebUtility.HtmlEncode(displayName);
        var safeCompany = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(companyName) ? "Your Workspace" : companyName);
        var safeTitle = WebUtility.HtmlEncode(eventTitle);
        var safeDesc = WebUtility.HtmlEncode(eventDescription);
        var rows = details ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var detailRows = string.Join("", rows.Select(kv =>
            $"<tr><td style=\"padding:8px 0;color:#6b7280;font-size:13px;\">{WebUtility.HtmlEncode(kv.Key)}</td><td style=\"padding:8px 0;color:#111827;font-size:13px;font-weight:600;text-align:right;\">{WebUtility.HtmlEncode(kv.Value ?? string.Empty)}</td></tr>"));

        var html = $"""
            <!doctype html>
            <html lang="en">
            <body style="margin:0;padding:0;background:#f7f7fb;font-family:Segoe UI,Arial,sans-serif;color:#111827;">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="padding:24px 12px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:620px;background:#ffffff;border-radius:14px;overflow:hidden;box-shadow:0 8px 30px rgba(17,24,39,.08);">
                      <tr>
                        <td style="background:#f97316;padding:18px 24px;color:#fff;font-weight:700;font-size:20px;">Textzy Billing Update</td>
                      </tr>
                      <tr>
                        <td style="padding:24px;">
                          <p style="margin:0 0 10px;">Hi {safeName},</p>
                          <p style="margin:0 0 10px;color:#4b5563;">Workspace: <strong>{safeCompany}</strong></p>
                          <h2 style="margin:0 0 8px;font-size:20px;color:#111827;">{safeTitle}</h2>
                          <p style="margin:0 0 16px;color:#4b5563;line-height:1.6;">{safeDesc}</p>
                          {(rows.Count > 0 ? $"<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"border-top:1px solid #e5e7eb;border-bottom:1px solid #e5e7eb;\">{detailRows}</table>" : "")}
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:14px 24px;background:#f9fafb;color:#6b7280;font-size:12px;">
                          Powered by Moneyart Private Limited
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        var detailsPlain = rows.Count == 0
            ? string.Empty
            : string.Join("\n", rows.Select(kv => $"{kv.Key}: {kv.Value}"));
        var plain = $"""
            Hi {(string.IsNullOrWhiteSpace(displayName) ? "there" : displayName)},

            {eventTitle}
            {eventDescription}
            Workspace: {(string.IsNullOrWhiteSpace(companyName) ? "Your Workspace" : companyName)}
            {(string.IsNullOrWhiteSpace(detailsPlain) ? "" : "\n" + detailsPlain)}
            """;

        await SendEmailAsync(toEmail, $"Textzy Billing: {eventTitle}", html, plain, ct, attachments);
    }
}
