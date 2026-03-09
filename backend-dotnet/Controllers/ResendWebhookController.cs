using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/email/webhook/resend")]
public class ResendWebhookController(
    ControlDbContext db,
    SecretCryptoService crypto,
    IConfiguration config,
    AuditLogService audit) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        string raw;
        using (var reader = new StreamReader(Request.Body))
            raw = await reader.ReadToEndAsync(ct);

        var secret = await ResolveWebhookSecretAsync(ct);
        if (string.IsNullOrWhiteSpace(secret))
            return Unauthorized("Resend webhook secret is not configured.");

        var svixId = Request.Headers["svix-id"].ToString();
        var svixTimestamp = Request.Headers["svix-timestamp"].ToString();
        var svixSignature = Request.Headers["svix-signature"].ToString();

        if (!VerifySvixSignature(secret, svixId, svixTimestamp, svixSignature, raw))
        {
            await audit.WriteAsync("email.webhook.rejected", "provider=resend; reason=signature_invalid", ct);
            return Unauthorized("Invalid webhook signature.");
        }

        if (!string.IsNullOrWhiteSpace(svixId))
        {
            var dup = await db.WebhookEvents.AsNoTracking()
                .AnyAsync(x => x.Provider == "resend" && x.EventKey == svixId, ct);
            if (dup)
                return Ok(new { ok = true, duplicate = true });
        }

        var now = DateTime.UtcNow;
        var (eventType, email, subject) = ExtractResendFields(raw);

        db.WebhookEvents.Add(new WebhookEvent
        {
            Id = Guid.NewGuid(),
            Provider = "resend",
            EventKey = !string.IsNullOrWhiteSpace(svixId) ? svixId : ComputeBodyKey(raw),
            TenantId = null,
            PhoneNumberId = string.Empty,
            PayloadJson = raw,
            Status = MapStatus(eventType),
            RetryCount = 0,
            MaxRetries = 0,
            LastError = string.Empty,
            ReceivedAtUtc = now,
            ProcessedAtUtc = now
        });

        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("email.webhook.received", $"provider=resend; event={eventType}; email={email}; subject={subject}", ct);
        return Ok(new { ok = true });
    }

    private async Task<string> ResolveWebhookSecretAsync(CancellationToken ct)
    {
        var row = await db.PlatformSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Scope == "smtp" && x.Key == "resendWebhookSecret", ct);
        if (row is not null)
        {
            var value = (crypto.Decrypt(row.ValueEncrypted) ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return (config["Resend:WebhookSecret"] ?? config["RESEND_WEBHOOK_SECRET"] ?? string.Empty).Trim();
    }

    private static (string eventType, string email, string subject) ExtractResendFields(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var eventType = root.TryGetProperty("type", out var t) ? (t.GetString() ?? string.Empty) : string.Empty;
            var data = root.TryGetProperty("data", out var d) ? d : default;
            var email = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("to", out var to)
                ? (to.GetString() ?? string.Empty)
                : string.Empty;
            var subject = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("subject", out var s)
                ? (s.GetString() ?? string.Empty)
                : string.Empty;
            return (eventType, email, subject);
        }
        catch
        {
            return (string.Empty, string.Empty, string.Empty);
        }
    }

    private static string MapStatus(string eventType)
    {
        return (eventType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "email.sent" => "Sent",
            "email.delivered" => "Delivered",
            "email.delivery_delayed" => "Delayed",
            "email.bounced" => "Bounced",
            "email.complained" => "Complained",
            "email.opened" => "Opened",
            "email.clicked" => "Clicked",
            _ => "Received"
        };
    }

    private static string ComputeBodyKey(string body)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(body ?? string.Empty));
        return Convert.ToHexString(hash);
    }

    private static bool VerifySvixSignature(string secret, string id, string timestamp, string signatureHeader, string payload)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(timestamp) || string.IsNullOrWhiteSpace(signatureHeader))
            return false;

        if (!long.TryParse(timestamp, out var ts)) return false;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - ts) > 5 * 60) return false;

        var key = DecodeSvixSecret(secret);
        if (key.Length == 0) return false;

        var signedPayload = $"{id}.{timestamp}.{payload}";
        var payloadBytes = Encoding.UTF8.GetBytes(signedPayload);
        using var hmac = new HMACSHA256(key);
        var digest = Convert.ToBase64String(hmac.ComputeHash(payloadBytes));

        var candidates = signatureHeader
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split(',', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && string.Equals(parts[0], "v1", StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts[1]);

        foreach (var cand in candidates)
        {
            if (FixedEquals(digest, cand)) return true;
        }
        return false;
    }

    private static byte[] DecodeSvixSecret(string secret)
    {
        var raw = secret.Trim();
        if (raw.StartsWith("whsec_", StringComparison.OrdinalIgnoreCase))
            raw = raw[6..];

        raw = raw.Replace('-', '+').Replace('_', '/');
        switch (raw.Length % 4)
        {
            case 2: raw += "=="; break;
            case 3: raw += "="; break;
        }

        try
        {
            return Convert.FromBase64String(raw);
        }
        catch
        {
            return [];
        }
    }

    private static bool FixedEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left ?? string.Empty);
        var b = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
