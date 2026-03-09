using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;

namespace Textzy.Api.Services;

public class SessionService(
    ControlDbContext db,
    IHttpContextAccessor httpContextAccessor,
    SecurityIpRuleService ipRules,
    IConfiguration config,
    SecretCryptoService crypto)
{
    private const int SessionHours = 12;
    private const int DefaultIdleTimeoutMinutes = 30;
    private const int MaxIdleTimeoutMinutes = 24 * 60;

    public async Task<string> CreateSessionAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken ct = default,
        DateTime? twoFactorVerifiedAtUtc = null,
        DateTime? stepUpVerifiedAtUtc = null,
        bool allowlistBypassEnabled = false)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var opaqueToken = Convert.ToBase64String(tokenBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        var currentIp = SecurityIpRuleService.NormalizeIp(RequestMetadata.GetClientIp(httpContextAccessor.HttpContext));
        var createDecision = ipRules.EvaluateSessionIp(currentIp, tenantId, userId, enforceAllowlist: !allowlistBypassEnabled);
        if (!createDecision.IsAllowed)
        {
            db.SecuritySignals.Add(new SecuritySignal
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                SignalType = string.Equals(createDecision.Message, "This IP address is blocked for platform login.", StringComparison.OrdinalIgnoreCase)
                    ? "session_ip_blocked"
                    : "session_ip_not_allowlisted",
                Severity = "high",
                Status = "open",
                CountValue = 1,
                Details = $"Login denied for user {userId} from IP {currentIp} for tenant {tenantId}. {createDecision.Message}"
            });
            await db.SaveChangesAsync(ct);
            throw new InvalidOperationException(createDecision.Message);
        }

        var session = new SessionToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = tenantId,
            TokenHash = HashToken(opaqueToken),
            CreatedIpAddress = currentIp,
            LastSeenIpAddress = currentIp,
            UserAgent = RequestMetadata.GetUserAgent(httpContextAccessor.HttpContext),
            DeviceLabel = RequestMetadata.GetDeviceLabel(httpContextAccessor.HttpContext),
            ExpiresAtUtc = DateTime.UtcNow.AddHours(SessionHours),
            LastSeenAtUtc = DateTime.UtcNow,
            TwoFactorVerifiedAtUtc = twoFactorVerifiedAtUtc,
            StepUpVerifiedAtUtc = stepUpVerifiedAtUtc,
            AllowlistBypassEnabled = allowlistBypassEnabled
        };

        db.SessionTokens.Add(session);
        await db.SaveChangesAsync(ct);
        return opaqueToken;
    }

    public SessionToken? Validate(string opaqueToken)
        => ValidateDetailed(opaqueToken).Session;

    public SessionValidationResult ValidateDetailed(string opaqueToken)
    {
        if (string.IsNullOrWhiteSpace(opaqueToken))
            return new SessionValidationResult(null, SessionValidationFailure.InvalidOrExpired, "Invalid or expired session.");

        var hash = HashToken(opaqueToken);
        var now = DateTime.UtcNow;
        var session = db.SessionTokens.FirstOrDefault(s =>
            s.TokenHash == hash &&
            s.RevokedAtUtc == null &&
            s.ExpiresAtUtc > now);
        if (session is null)
            return new SessionValidationResult(null, SessionValidationFailure.InvalidOrExpired, "Invalid or expired session.");

        var idleTimeout = GetIdleTimeout();
        var lastSeenAtUtc = session.LastSeenAtUtc ?? session.CreatedAtUtc;
        if (idleTimeout > TimeSpan.Zero && lastSeenAtUtc.Add(idleTimeout) <= now)
        {
            session.RevokedAtUtc = now;
            db.SaveChanges();
            return new SessionValidationResult(null, SessionValidationFailure.IdleTimeout, "Session expired due to inactivity.");
        }

        session.LastSeenAtUtc = now;
        var ip = NormalizeIp(RequestMetadata.GetClientIp(httpContextAccessor.HttpContext));
        if (!string.IsNullOrWhiteSpace(ip))
        {
            var ruleDecision = ipRules.EvaluateSessionIp(
                ip,
                session.TenantId,
                session.UserId,
                enforceAllowlist: !session.AllowlistBypassEnabled);
            if (!ruleDecision.IsAllowed)
            {
                session.LastSeenIpAddress = ip;
                session.RevokedAtUtc = now;
                db.SecuritySignals.Add(new SecuritySignal
                {
                    Id = Guid.NewGuid(),
                    TenantId = session.TenantId,
                    SignalType = string.Equals(ruleDecision.Message, "This IP address is blocked for platform login.", StringComparison.OrdinalIgnoreCase)
                        ? "session_ip_blocked"
                        : "session_ip_not_allowlisted",
                    Severity = "high",
                    Status = "open",
                    CountValue = 1,
                    Details = $"Session {session.Id} revoked for user {session.UserId} from IP {ip}. {ruleDecision.Message}"
                });
                db.SaveChanges();
                return new SessionValidationResult(null, SessionValidationFailure.IpRejected, ruleDecision.Message);
            }

            var baselineIp = NormalizeIp(!string.IsNullOrWhiteSpace(session.CreatedIpAddress)
                ? session.CreatedIpAddress
                : session.LastSeenIpAddress);
            if (string.IsNullOrWhiteSpace(baselineIp))
            {
                session.CreatedIpAddress = ip;
                session.LastSeenIpAddress = ip;
            }
            else if (!string.Equals(ip, baselineIp, StringComparison.OrdinalIgnoreCase))
            {
                session.LastSeenIpAddress = ip;
                session.RevokedAtUtc = now;
                db.SecuritySignals.Add(new SecuritySignal
                {
                    Id = Guid.NewGuid(),
                    TenantId = session.TenantId,
                    SignalType = "session_ip_changed",
                    Severity = "high",
                    Status = "open",
                    CountValue = 1,
                    Details = $"Session {session.Id} revoked because IP changed from {baselineIp} to {ip} for user {session.UserId}."
                });
                db.SaveChanges();
                return new SessionValidationResult(null, SessionValidationFailure.IpChanged, "Your IP changed. Please login again.");
            }
            else
            {
                session.LastSeenIpAddress = ip;
            }
        }
        var ua = RequestMetadata.GetUserAgent(httpContextAccessor.HttpContext);
        if (!string.IsNullOrWhiteSpace(ua))
        {
            session.UserAgent = ua;
            session.DeviceLabel = RequestMetadata.GetDeviceLabel(httpContextAccessor.HttpContext);
        }
        db.SaveChanges();
        return new SessionValidationResult(session, SessionValidationFailure.None, string.Empty);
    }

    public async Task RevokeAsync(string opaqueToken, CancellationToken ct = default)
    {
        var session = ValidateDetailed(opaqueToken).Session;
        if (session is null) return;
        session.RevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> RotateAsync(string opaqueToken, CancellationToken ct = default)
        => (await RotateDetailedAsync(opaqueToken, ct)).Token;

    public async Task<SessionRotationResult> RotateDetailedAsync(string opaqueToken, CancellationToken ct = default)
    {
        var validation = ValidateDetailed(opaqueToken);
        var session = validation.Session;
        if (session is null)
            return new SessionRotationResult(null, validation.Failure, validation.Message);

        session.RevokedAtUtc = DateTime.UtcNow;
        var newToken = await CreateSessionAsync(
            session.UserId,
            session.TenantId,
            ct,
            session.TwoFactorVerifiedAtUtc,
            session.StepUpVerifiedAtUtc,
            session.AllowlistBypassEnabled);
        return new SessionRotationResult(newToken, SessionValidationFailure.None, string.Empty);
    }

    public async Task MarkStepUpVerifiedAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await db.SessionTokens.FirstOrDefaultAsync(x => x.Id == sessionId, ct);
        if (session is null) return;
        var now = DateTime.UtcNow;
        session.StepUpVerifiedAtUtc = now;
        if (!session.TwoFactorVerifiedAtUtc.HasValue)
            session.TwoFactorVerifiedAtUtc = now;
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> GetAllowlistBypassEnabledAsync(Guid sessionId, CancellationToken ct = default)
    {
        if (sessionId == Guid.Empty) return false;
        return await db.SessionTokens
            .AsNoTracking()
            .Where(x => x.Id == sessionId)
            .Select(x => x.AllowlistBypassEnabled)
            .FirstOrDefaultAsync(ct);
    }

    private static string HashToken(string opaqueToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(opaqueToken));
        return Convert.ToHexString(bytes);
    }

    private static string NormalizeIp(string? value)
        => (value ?? string.Empty).Trim();

    private TimeSpan GetIdleTimeout()
    {
        var raw = FirstNonEmpty(
            ReadAuthSecuritySetting("sessionIdleTimeoutMinutes"),
            config["Auth:SessionIdleTimeoutMinutes"],
            config["SESSION_IDLE_TIMEOUT_MINUTES"]);
        if (!int.TryParse(raw, out var minutes))
            minutes = DefaultIdleTimeoutMinutes;
        if (minutes <= 0) return TimeSpan.Zero;
        if (minutes > MaxIdleTimeoutMinutes) minutes = MaxIdleTimeoutMinutes;
        return TimeSpan.FromMinutes(minutes);
    }

    private string ReadAuthSecuritySetting(string key)
    {
        var row = db.PlatformSettings
            .AsNoTracking()
            .FirstOrDefault(x => x.Scope == "auth-security" && x.Key == key);
        if (row is null) return string.Empty;
        try
        {
            return crypto.Decrypt(row.ValueEncrypted);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
    }
}
