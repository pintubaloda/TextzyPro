using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Services;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/security/authenticator")]
public class UserAuthenticatorController(
    ControlDbContext db,
    AuthContext auth,
    SecretCryptoService crypto,
    AuthenticatorTotpService totp) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == auth.UserId, ct);
        if (user is null) return Unauthorized();

        return Ok(new
        {
            provider = string.IsNullOrWhiteSpace(user.AuthenticatorProvider) ? "" : user.AuthenticatorProvider,
            enabled = user.AuthenticatorEnabledAtUtc.HasValue,
            enrolledAtUtc = user.AuthenticatorEnabledAtUtc
        });
    }

    [HttpPost("setup")]
    public async Task<IActionResult> Setup([FromBody] SetupRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == auth.UserId && x.IsActive, ct);
        if (user is null) return Unauthorized();

        var provider = totp.NormalizeProvider(request.Provider);
        if (provider.Length == 0) return BadRequest("provider must be google_authenticator or microsoft_authenticator.");

        var secret = totp.GenerateBase32Secret();
        user.AuthenticatorSecretEncrypted = crypto.Encrypt(secret);
        user.AuthenticatorProvider = provider;
        user.AuthenticatorEnabledAtUtc = null;
        await db.SaveChangesAsync(ct);

        var issuer = "Textzy";
        var label = $"{issuer}:{user.Email}";
        var otpauth = totp.BuildOtpAuthUrl(issuer, label, secret);
        var qrUrl = totp.BuildQrUrl(issuer, user.Email, secret);

        return Ok(new
        {
            provider,
            enabled = false,
            otpauthUrl = otpauth,
            qrUrl,
            label = user.Email
        });
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] VerifyRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == auth.UserId && x.IsActive, ct);
        if (user is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(user.AuthenticatorSecretEncrypted) || string.IsNullOrWhiteSpace(user.AuthenticatorProvider))
            return BadRequest("Authenticator setup is not initialized.");

        var secret = crypto.Decrypt(user.AuthenticatorSecretEncrypted);
        if (!totp.VerifyTotp(secret, request.Code))
            return BadRequest("Invalid authenticator code.");

        user.AuthenticatorEnabledAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { ok = true, provider = user.AuthenticatorProvider, enabled = true, enrolledAtUtc = user.AuthenticatorEnabledAtUtc });
    }

    [HttpDelete]
    public async Task<IActionResult> Disable(CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == auth.UserId && x.IsActive, ct);
        if (user is null) return Unauthorized();

        user.AuthenticatorSecretEncrypted = string.Empty;
        user.AuthenticatorProvider = string.Empty;
        user.AuthenticatorEnabledAtUtc = null;
        await db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }
    public sealed class SetupRequest
    {
        public string Provider { get; set; } = string.Empty;
    }

    public sealed class VerifyRequest
    {
        public string Code { get; set; } = string.Empty;
    }
}
