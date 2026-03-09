using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;

namespace Textzy.Api.Services;

public class SessionService(ControlDbContext db, IHttpContextAccessor httpContextAccessor)
{
    private const int SessionHours = 12;

    public async Task<string> CreateSessionAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken ct = default,
        DateTime? twoFactorVerifiedAtUtc = null,
        DateTime? stepUpVerifiedAtUtc = null)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var opaqueToken = Convert.ToBase64String(tokenBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        var session = new SessionToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = tenantId,
            TokenHash = HashToken(opaqueToken),
            CreatedIpAddress = RequestMetadata.GetClientIp(httpContextAccessor.HttpContext),
            LastSeenIpAddress = RequestMetadata.GetClientIp(httpContextAccessor.HttpContext),
            UserAgent = RequestMetadata.GetUserAgent(httpContextAccessor.HttpContext),
            DeviceLabel = RequestMetadata.GetDeviceLabel(httpContextAccessor.HttpContext),
            ExpiresAtUtc = DateTime.UtcNow.AddHours(SessionHours),
            LastSeenAtUtc = DateTime.UtcNow,
            TwoFactorVerifiedAtUtc = twoFactorVerifiedAtUtc,
            StepUpVerifiedAtUtc = stepUpVerifiedAtUtc
        };

        db.SessionTokens.Add(session);
        await db.SaveChangesAsync(ct);
        return opaqueToken;
    }

    public SessionToken? Validate(string opaqueToken)
    {
        var hash = HashToken(opaqueToken);
        var now = DateTime.UtcNow;
        var session = db.SessionTokens.FirstOrDefault(s =>
            s.TokenHash == hash &&
            s.RevokedAtUtc == null &&
            s.ExpiresAtUtc > now);
        if (session is null) return null;
        session.LastSeenAtUtc = now;
        var ip = RequestMetadata.GetClientIp(httpContextAccessor.HttpContext);
        if (!string.IsNullOrWhiteSpace(ip))
            session.LastSeenIpAddress = ip;
        var ua = RequestMetadata.GetUserAgent(httpContextAccessor.HttpContext);
        if (!string.IsNullOrWhiteSpace(ua))
        {
            session.UserAgent = ua;
            session.DeviceLabel = RequestMetadata.GetDeviceLabel(httpContextAccessor.HttpContext);
        }
        db.SaveChanges();
        return session;
    }

    public async Task RevokeAsync(string opaqueToken, CancellationToken ct = default)
    {
        var session = Validate(opaqueToken);
        if (session is null) return;
        session.RevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> RotateAsync(string opaqueToken, CancellationToken ct = default)
    {
        var session = Validate(opaqueToken);
        if (session is null) return null;

        session.RevokedAtUtc = DateTime.UtcNow;
        var newToken = await CreateSessionAsync(
            session.UserId,
            session.TenantId,
            ct,
            session.TwoFactorVerifiedAtUtc,
            session.StepUpVerifiedAtUtc);
        return newToken;
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

    private static string HashToken(string opaqueToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(opaqueToken));
        return Convert.ToHexString(bytes);
    }
}
