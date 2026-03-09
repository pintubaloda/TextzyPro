using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Textzy.Api.Data;
using Textzy.Api.DTOs;
using Textzy.Api.Models;
using Textzy.Api.Services;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    ControlDbContext db,
    PasswordHasher hasher,
    SessionService sessions,
    EmailService emailService,
    BillingGuardService billingGuard,
    TenancyContext tenancy,
    AuthContext auth,
    AuthCookieService authCookie,
    SecretCryptoService crypto,
    TemplateSyncOrchestrator templateSync,
    SensitiveDataRedactor redactor,
    AuthenticatorTotpService totp,
    ILogger<AuthController> logger,
    IHttpClientFactory httpClientFactory,
    IConfiguration config) : ControllerBase
{
    private const int EmailOtpLength = 6;
    private const int EmailOtpExpiryMinutes = 10;
    private const int EmailActionLinkExpiryMinutes = 15;
    private const int EmailOtpMaxAttempts = 5;
    private const int EmailOtpResendCooldownSeconds = 30;
    private const int LoginTwoFactorChallengeExpiryMinutes = 10;
    private const int StepUpChallengeExpiryMinutes = 10;
    private const int StepUpFreshMinutes = 10;

    private static readonly string[] DefaultAppApiCatalog =
    [
        "/api/auth/login",
        "/api/auth/refresh",
        "/api/auth/logout",
        "/api/auth/me",
        "/api/auth/projects",
        "/api/auth/switch-project",
        "/api/auth/app-bootstrap",
        "/api/auth/devices",
        "/api/auth/devices/pair-qr",
        "/api/public/mobile/pair/exchange",
        "/api/inbox/conversations",
        "/api/inbox/conversations/{id}/messages",
        "/api/inbox/conversations/{id}/assign",
        "/api/inbox/conversations/{id}/transfer",
        "/api/inbox/conversations/{id}/labels",
        "/api/inbox/conversations/{id}/notes",
        "/api/inbox/typing",
        "/api/inbox/sla",
        "/api/messages/send",
        "/api/messages/media/{mediaId}",
        "/hubs/inbox"
    ];

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        string email;
        string password;
        try
        {
            email = InputGuardService.RequireTrimmed(request.Email, "Email", 320).ToLowerInvariant();
            password = InputGuardService.RequireTrimmed(request.Password, "Password", 256);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        var user = db.Users.FirstOrDefault(u => u.Email.ToLower() == email && u.IsActive);
        if (user is null)
            return Unauthorized("Invalid credentials.");

        var verified = hasher.Verify(password, user.PasswordHash, user.PasswordSalt);
        if (!verified) return Unauthorized("Invalid credentials.");

        var verificationMode = await GetEmailVerificationModeAsync(ct);
        var requireOtp = verificationMode switch
        {
            "every-login" => true,
            "registration" => user.EmailVerifiedAtUtc is null,
            _ => false
        };
        if (requireOtp)
        {
            var verificationIdRaw = (request.EmailVerificationId ?? string.Empty).Trim();
            if (!Guid.TryParse(verificationIdRaw, out var verificationId))
                return Unauthorized("Email verification required.");

            var otpSession = await db.EmailOtpVerifications
                .FirstOrDefaultAsync(x => x.Id == verificationId && x.Email.ToLower() == email && x.Purpose == "login", ct);
            var otpExpiry = otpSession?.OtpExpiresAtUtc ?? otpSession?.ExpiresAtUtc;
            if (otpSession is null || !otpSession.IsVerified || otpSession.ConsumedAtUtc is not null || otpExpiry <= DateTime.UtcNow)
                return Unauthorized("Email verification required.");

            otpSession.ConsumedAtUtc = DateTime.UtcNow;
            otpSession.Status = "verified";
            if (user.EmailVerifiedAtUtc is null) user.EmailVerifiedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        Guid tenantId;
        if (tenancy.IsSet)
        {
            var hasAccess = db.TenantUsers.Any(tu => tu.UserId == user.Id && tu.TenantId == tenancy.TenantId);
            if (!user.IsSuperAdmin && !hasAccess) return Forbid();
            tenantId = tenancy.TenantId;
        }
        else
        {
            if (user.IsSuperAdmin)
            {
                tenantId = db.Tenants.OrderBy(t => t.CreatedAtUtc).Select(t => t.Id).FirstOrDefault();
                if (tenantId == Guid.Empty) return BadRequest("No tenant available for super admin.");
            }
            else
            {
                tenantId = db.TenantUsers
                    .Where(tu => tu.UserId == user.Id)
                    .OrderByDescending(tu => tu.CreatedAtUtc)
                    .Select(tu => tu.TenantId)
                    .FirstOrDefault();
                if (tenantId == Guid.Empty) return Forbid();
            }
        }

        if (UserRequiresAuthenticator(user))
        {
            var challenge = await CreateTwoFactorChallengeAsync(
                user,
                tenantId,
                "login",
                "login_two_factor",
                null,
                LoginTwoFactorChallengeExpiryMinutes,
                ct);
            return Ok(new
            {
                requiresTwoFactor = true,
                challengeToken = challenge.token,
                provider = challenge.provider,
                expiresAtUtc = challenge.expiresAtUtc,
                title = "Two-factor authentication required",
                message = "Open your authenticator app and enter the 6-digit code to finish signing in."
            });
        }

        var token = await sessions.CreateSessionAsync(user.Id, tenantId, ct);
        authCookie.SetToken(HttpContext, token);
        authCookie.EnsureCsrfToken(HttpContext);
        WriteAuthHeaders(token);
        return Ok(new AuthTokenResponse { AccessToken = token });
    }

    [HttpPost("two-factor/verify-login")]
    public async Task<IActionResult> VerifyLoginTwoFactor([FromBody] VerifyLoginTwoFactorRequest request, CancellationToken ct)
    {
        var challengeToken = (request.ChallengeToken ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(challengeToken)) return BadRequest("Challenge token is required.");

        var challenge = await db.TwoFactorChallenges
            .FirstOrDefaultAsync(x => x.ChallengeTokenHash == HashToken(challengeToken) && x.Purpose == "login", ct);
        if (challenge is null) return BadRequest("Two-factor challenge not found.");
        if (challenge.ConsumedAtUtc.HasValue || challenge.ExpiresAtUtc <= DateTime.UtcNow) return BadRequest("Two-factor challenge expired.");

        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == challenge.UserId && x.IsActive, ct);
        if (user is null) return Unauthorized("User not active.");
        if (!UserRequiresAuthenticator(user)) return BadRequest("Authenticator is not enabled for this account.");

        var secret = crypto.Decrypt(user.AuthenticatorSecretEncrypted);
        if (!totp.VerifyTotp(secret, request.Code))
            return BadRequest("Invalid authenticator code.");

        var now = DateTime.UtcNow;
        challenge.VerifiedAtUtc = now;
        challenge.ConsumedAtUtc = now;
        var token = await sessions.CreateSessionAsync(user.Id, challenge.TenantId, ct, now, now);
        await db.SaveChangesAsync(ct);

        authCookie.SetToken(HttpContext, token);
        authCookie.EnsureCsrfToken(HttpContext);
        WriteAuthHeaders(token);
        return Ok(new AuthTokenResponse { AccessToken = token });
    }

    [HttpPost("step-up/request")]
    public async Task<IActionResult> RequestStepUp([FromBody] RequestStepUpAuthRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == auth.UserId && x.IsActive, ct);
        if (user is null) return Unauthorized();
        if (!UserRequiresAuthenticator(user))
            return BadRequest("Authenticator 2FA is not enabled for this account.");

        var action = InputGuardService.RequireTrimmed(request.Action ?? "sensitive_action", "Action", 120).ToLowerInvariant();
        var challenge = await CreateTwoFactorChallengeAsync(
            user,
            auth.TenantId,
            "step_up",
            action,
            auth.SessionId == Guid.Empty ? null : auth.SessionId,
            StepUpChallengeExpiryMinutes,
            ct);

        return Ok(new
        {
            challengeToken = challenge.token,
            provider = challenge.provider,
            expiresAtUtc = challenge.expiresAtUtc,
            title = string.IsNullOrWhiteSpace(request.Title) ? "Additional verification required" : request.Title.Trim(),
            message = string.IsNullOrWhiteSpace(request.Message)
                ? "Enter the 6-digit code from your authenticator app to continue."
                : request.Message.Trim()
        });
    }

    [HttpPost("step-up/verify")]
    public async Task<IActionResult> VerifyStepUp([FromBody] VerifyStepUpAuthRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (auth.SessionId == Guid.Empty) return Unauthorized();

        var challengeToken = (request.ChallengeToken ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(challengeToken)) return BadRequest("Challenge token is required.");

        var challenge = await db.TwoFactorChallenges
            .FirstOrDefaultAsync(x => x.ChallengeTokenHash == HashToken(challengeToken) && x.Purpose == "step_up", ct);
        if (challenge is null) return BadRequest("Step-up challenge not found.");
        if (challenge.UserId != auth.UserId || challenge.TenantId != auth.TenantId) return Forbid();
        if (challenge.SessionTokenId.HasValue && challenge.SessionTokenId != auth.SessionId) return Forbid();
        if (challenge.ConsumedAtUtc.HasValue || challenge.ExpiresAtUtc <= DateTime.UtcNow) return BadRequest("Step-up challenge expired.");

        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == auth.UserId && x.IsActive, ct);
        if (user is null) return Unauthorized();
        if (!UserRequiresAuthenticator(user)) return BadRequest("Authenticator 2FA is not enabled for this account.");

        var secret = crypto.Decrypt(user.AuthenticatorSecretEncrypted);
        if (!totp.VerifyTotp(secret, request.Code))
            return BadRequest("Invalid authenticator code.");

        var now = DateTime.UtcNow;
        challenge.VerifiedAtUtc = now;
        challenge.ConsumedAtUtc = now;
        await sessions.MarkStepUpVerifiedAsync(auth.SessionId, ct);
        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            ok = true,
            action = challenge.ActionCode,
            verifiedAtUtc = now,
            validForSeconds = StepUpFreshMinutes * 60
        });
    }

    [HttpPost("email-verification/request")]
    public async Task<IActionResult> RequestEmailVerificationOtp([FromBody] RequestEmailVerificationOtpRequest request, CancellationToken ct)
    {
        var purpose = NormalizeOtpPurpose(request.Purpose);
        var emailResult = ResolveEmailForOtpRequest(request.Email);
        if (!emailResult.ok) return BadRequest(emailResult.error);
        var email = emailResult.email;

        var now = DateTime.UtcNow;
        var requestsInHour = await db.EmailOtpVerifications
            .CountAsync(x => x.Email.ToLower() == email && x.Purpose == purpose && x.CreatedAtUtc >= now.AddHours(-1), ct);
        if (requestsInHour >= 8)
            return StatusCode(StatusCodes.Status429TooManyRequests, "Too many OTP requests. Try again later.");

        var latest = await db.EmailOtpVerifications
            .Where(x => x.Email.ToLower() == email && x.Purpose == purpose)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (latest is not null && (now - latest.LastSentAtUtc).TotalSeconds < EmailOtpResendCooldownSeconds)
            return StatusCode(StatusCodes.Status429TooManyRequests, $"Please wait {EmailOtpResendCooldownSeconds} seconds before requesting another OTP.");

        var linkToken = CreateOpaqueToken(32);
        var row = new EmailOtpVerification
        {
            Id = Guid.NewGuid(),
            Email = email,
            Purpose = purpose,
            Status = "email_sent",
            OtpHash = string.Empty,
            OtpDisplayEncrypted = string.Empty,
            VerificationCode = string.Empty,
            LinkTokenHash = HashToken(linkToken),
            IsVerified = false,
            AttemptCount = 0,
            MaxAttempts = EmailOtpMaxAttempts,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(EmailActionLinkExpiryMinutes),
            OtpExpiresAtUtc = null,
            LinkOpenedAtUtc = null,
            OtpIssuedAtUtc = null,
            LastSentAtUtc = now
        };
        db.EmailOtpVerifications.Add(row);
        await db.SaveChangesAsync(ct);

        var userName = await db.Users
            .Where(x => x.Email.ToLower() == email)
            .Select(x => x.FullName)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        var verifyNowLink = BuildEmailVerificationLink(row.Id, linkToken, purpose);
        await emailService.SendVerificationActionAsync(
            email,
            userName,
            purpose,
            verifyNowLink,
            EmailActionLinkExpiryMinutes,
            ct);

        return Ok(new
        {
            verificationId = row.Id,
            purpose,
            status = row.Status,
            state = "waiting_user_action",
            otpInputAllowed = false,
            expiresAtUtc = row.ExpiresAtUtc,
            otpExpiresAtUtc = row.OtpExpiresAtUtc,
            resendAfterSeconds = EmailOtpResendCooldownSeconds,
            message = "Verification email sent. Click Verify Now in your email to continue."
        });
    }

    [HttpGet("email-verification/status")]
    public async Task<IActionResult> EmailVerificationStatus([FromQuery] string verificationId, [FromQuery] string? purpose, [FromQuery] string? email, CancellationToken ct)
    {
        if (!Guid.TryParse((verificationId ?? string.Empty).Trim(), out var id))
            return BadRequest("Invalid verificationId.");

        var normalizedPurpose = NormalizeOtpPurpose(purpose);
        var resolved = ResolveEmailForOtpStatus(email);
        var rowQuery = db.EmailOtpVerifications.Where(x => x.Id == id && x.Purpose == normalizedPurpose);
        if (resolved.hasEmail)
            rowQuery = rowQuery.Where(x => x.Email.ToLower() == resolved.email);

        var row = await rowQuery.FirstOrDefaultAsync(ct);
        if (row is null) return NotFound("Verification session not found.");

        var now = DateTime.UtcNow;
        var state = GetVerificationState(row, now);
        if (state == "expired" && row.Status != "expired")
        {
            row.Status = "expired";
            await db.SaveChangesAsync(ct);
        }
        var uiState = state switch
        {
            "email_sent" => "waiting_user_action",
            "link_opened" => "waiting_user_action",
            "code_issued" => "otp_ready",
            _ => state
        };
        return Ok(new
        {
            verificationId = row.Id,
            purpose = row.Purpose,
            status = state,
            state = uiState,
            otpInputAllowed = uiState == "otp_ready" || uiState == "verified",
            isVerified = row.IsVerified,
            linkOpenedAtUtc = row.LinkOpenedAtUtc,
            otpIssuedAtUtc = row.OtpIssuedAtUtc,
            expiresAtUtc = row.ExpiresAtUtc,
            otpExpiresAtUtc = row.OtpExpiresAtUtc,
            remainingAttempts = Math.Max(0, row.MaxAttempts - row.AttemptCount)
        });
    }

    [HttpGet("email-verification/link")]
    public async Task<IActionResult> OpenEmailVerificationLink([FromQuery] string verificationId, [FromQuery] string token, [FromQuery] string? purpose, CancellationToken ct)
    {
        if (!Guid.TryParse((verificationId ?? string.Empty).Trim(), out var id))
            return Content(BuildVerificationHtml("Invalid verification link.", null, false), "text/html; charset=utf-8");

        var normalizedPurpose = NormalizeOtpPurpose(purpose);
        var row = await db.EmailOtpVerifications.FirstOrDefaultAsync(x => x.Id == id && x.Purpose == normalizedPurpose, ct);
        if (row is null)
            return Content(BuildVerificationHtml("Verification session not found.", null, false), "text/html; charset=utf-8");

        var now = DateTime.UtcNow;
        if (row.ConsumedAtUtc is not null)
            return Content(BuildVerificationHtml("This verification session is already used.", null, false), "text/html; charset=utf-8");
        if (row.ExpiresAtUtc <= now)
            return Content(BuildVerificationHtml("Verification link expired. Request a new email.", null, false), "text/html; charset=utf-8");
        if (row.LinkOpenedAtUtc is not null || row.Status is "link_opened" or "code_issued" or "verified")
            return Content(BuildVerificationHtml("Verification link already used.", null, false), "text/html; charset=utf-8");
        if (!string.Equals(HashToken((token ?? string.Empty).Trim()), row.LinkTokenHash, StringComparison.Ordinal))
            return Content(BuildVerificationHtml("Invalid verification link token.", null, false), "text/html; charset=utf-8");

        var otp = GenerateNumericCode(EmailOtpLength);
        row.Status = "link_opened";
        row.LinkOpenedAtUtc = now;
        row.OtpHash = HashToken(otp);
        row.OtpDisplayEncrypted = crypto.Encrypt(otp);
        row.OtpIssuedAtUtc = now;
        row.OtpExpiresAtUtc = now.AddMinutes(EmailOtpExpiryMinutes);
        row.Status = "code_issued";
        await db.SaveChangesAsync(ct);

        return Content(
            BuildVerificationHtml(
                "Copy this code and paste it in the main screen.",
                otp,
                true,
                row.Id,
                row.Purpose,
                row.OtpExpiresAtUtc),
            "text/html; charset=utf-8");
    }

    [HttpPost("email-verification/verify")]
    public async Task<IActionResult> VerifyEmailOtp([FromBody] VerifyEmailOtpRequest request, CancellationToken ct)
    {
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var purpose = NormalizeOtpPurpose(request.Purpose);
        string otp;
        try
        {
            otp = InputGuardService.RequireTrimmed(request.Otp, "OTP", 12);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        if (!Guid.TryParse((request.VerificationId ?? string.Empty).Trim(), out var verificationId))
            return BadRequest("Invalid verificationId.");

        var rowQuery = db.EmailOtpVerifications.Where(x => x.Id == verificationId && x.Purpose == purpose);
        if (!string.IsNullOrWhiteSpace(email)) rowQuery = rowQuery.Where(x => x.Email.ToLower() == email);
        else if (auth.IsAuthenticated) rowQuery = rowQuery.Where(x => x.Email.ToLower() == auth.Email.ToLower());

        var row = await rowQuery.FirstOrDefaultAsync(ct);
        if (row is null) return BadRequest("Invalid or expired verification.");
        var now = DateTime.UtcNow;
        var otpExpiry = row.OtpExpiresAtUtc ?? row.ExpiresAtUtc;
        if (row.ConsumedAtUtc is not null || row.AttemptCount >= row.MaxAttempts || row.LinkOpenedAtUtc is null || otpExpiry <= now)
        {
            if (otpExpiry <= now) row.Status = "expired";
            await db.SaveChangesAsync(ct);
            return BadRequest("Invalid or expired verification.");
        }

        if (!string.Equals(HashToken(otp), row.OtpHash, StringComparison.Ordinal))
        {
            row.AttemptCount += 1;
            if (row.AttemptCount >= row.MaxAttempts) row.Status = "expired";
            await db.SaveChangesAsync(ct);
            return BadRequest("Invalid or expired verification.");
        }

        row.IsVerified = true;
        row.Status = "verified";
        row.VerifiedAtUtc = now;
        row.OtpDisplayEncrypted = string.Empty;
        row.OtpHash = string.Empty;
        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            verified = true,
            verificationId = row.Id,
            verifiedAtUtc = row.VerifiedAtUtc,
            nextStep = row.Purpose
        });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var header = Request.Headers.Authorization.ToString();
        var bearerToken = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : string.Empty;
        var cookieToken = authCookie.ReadToken(HttpContext) ?? string.Empty;

        string? rotated = null;
        if (!string.IsNullOrWhiteSpace(bearerToken))
            rotated = await sessions.RotateAsync(bearerToken, ct);
        // Fallback to cookie token when bearer is stale/missing.
        if (rotated is null && !string.IsNullOrWhiteSpace(cookieToken))
            rotated = await sessions.RotateAsync(cookieToken, ct);

        if (rotated is null) return Unauthorized("Invalid or expired session.");
        authCookie.SetToken(HttpContext, rotated);
        authCookie.EnsureCsrfToken(HttpContext);
        WriteAuthHeaders(rotated);
        return Ok(new AuthTokenResponse { AccessToken = rotated });
    }

    [HttpPost("accept-invite")]
    public async Task<IActionResult> AcceptInvite([FromBody] AcceptInviteRequest request, CancellationToken ct)
    {
        var token = (request.Token ?? string.Empty).Trim();
        var password = request.Password ?? string.Empty;
        var fullName = (request.FullName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(token)) return BadRequest("Invite token is required.");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8) return BadRequest("Password must be at least 8 characters.");

        var tokenHash = HashToken(token);
        var invite = await db.TeamInvitations
            .Where(i => i.TokenHash == tokenHash)
            .OrderByDescending(i => i.SentAtUtc)
            .FirstOrDefaultAsync(ct);
        if (invite is null) return BadRequest("Invalid invite token.");
        if (invite.Status == "accepted") return BadRequest("Invite already accepted.");
        if (invite.ExpiresAtUtc <= DateTime.UtcNow) return BadRequest("Invite expired.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == invite.Email.ToLower(), ct);
        if (user is null) return BadRequest("Invited user not found.");
        var inviteTenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == invite.TenantId, ct);
        if (inviteTenant is null) return BadRequest("Invite tenant not found.");
        var inviteOwnerGroupId = await EnsureTenantOwnerGroupForInviteAsync(inviteTenant, invite.CreatedByUserId, ct);
        if (!await CanUserJoinOwnerGroupAsync(user.Id, inviteOwnerGroupId, ct))
            return BadRequest("User belongs to another tenant owner group and cannot accept this invite.");

        var member = await db.TenantUsers.FirstOrDefaultAsync(tu => tu.TenantId == invite.TenantId && tu.UserId == user.Id, ct);
        if (member is null)
        {
            db.TenantUsers.Add(new TenantUser
            {
                Id = Guid.NewGuid(),
                TenantId = invite.TenantId,
                UserId = user.Id,
                Role = invite.Role
            });
        }
        else
        {
            member.Role = invite.Role;
        }

        var (hash, salt) = hasher.HashPassword(password);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        user.IsActive = true;
        if (!string.IsNullOrWhiteSpace(fullName))
            user.FullName = fullName;
        else if (string.IsNullOrWhiteSpace(user.FullName) && !string.IsNullOrWhiteSpace(invite.Name))
            user.FullName = invite.Name.Trim();
        if (user.EmailVerifiedAtUtc is null) user.EmailVerifiedAtUtc = DateTime.UtcNow;

        invite.Status = "accepted";
        invite.AcceptedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        var memberCount = await db.TenantUsers.CountAsync(tu => tu.TenantId == invite.TenantId, ct);
        await billingGuard.SetAbsoluteUsageAsync(invite.TenantId, "teamMembers", memberCount, ct);

        var sessionToken = await sessions.CreateSessionAsync(
            user.Id,
            invite.TenantId,
            ct,
            auth.TwoFactorVerifiedAtUtc,
            auth.StepUpVerifiedAtUtc);
        authCookie.SetToken(HttpContext, sessionToken);
        authCookie.EnsureCsrfToken(HttpContext);
        WriteAuthHeaders(sessionToken);
        return Ok(new { accessToken = sessionToken, tenantSlug = inviteTenant.Slug, projectName = inviteTenant.Name, role = invite.Role });
    }

    [HttpGet("invite-preview")]
    public async Task<IActionResult> InvitePreview([FromQuery] string token, CancellationToken ct)
    {
        var safeToken = (token ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(safeToken)) return BadRequest("Invite token is required.");

        var tokenHash = HashToken(safeToken);
        var invite = await db.TeamInvitations
            .Where(i => i.TokenHash == tokenHash)
            .OrderByDescending(i => i.SentAtUtc)
            .FirstOrDefaultAsync(ct);
        if (invite is null) return NotFound("Invite not found.");
        if (invite.Status == "accepted") return BadRequest("Invite already accepted.");
        if (invite.ExpiresAtUtc <= DateTime.UtcNow) return BadRequest("Invite expired.");

        var tenantName = await db.Tenants
            .Where(t => t.Id == invite.TenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(ct);

        return Ok(new
        {
            email = invite.Email,
            fullName = invite.Name,
            role = invite.Role,
            tenantName = tenantName ?? string.Empty,
            expiresAtUtc = invite.ExpiresAtUtc
        });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var header = Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : (authCookie.ReadToken(HttpContext) ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(token)) await sessions.RevokeAsync(token, ct);
        authCookie.Clear(HttpContext);
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        authCookie.EnsureCsrfToken(HttpContext);
        try
        {
            await templateSync.EnsureInitialOrDailySyncAsync(false, ct);
        }
        catch (Exception ex)
        {
            // Keep auth health independent from template sync health.
            logger.LogWarning("Non-blocking template auto-sync failed on /api/auth/me tenant={TenantId}: {Error}",
                auth.TenantId, redactor.RedactText(ex.Message));
        }
        return Ok(new { auth.UserId, auth.Email, auth.Role, auth.TenantId, tenantSlug = tenancy.TenantSlug, permissions = auth.Permissions });
    }

    [HttpGet("projects")]
    public IActionResult Projects()
    {
        if (!auth.IsAuthenticated) return Unauthorized();

        var projects = db.TenantUsers
            .Where(tu => tu.UserId == auth.UserId)
            .Join(db.Tenants, tu => tu.TenantId, t => t.Id, (tu, t) => new
            {
                t.Id,
                t.Name,
                t.Slug,
                tu.Role,
                t.CreatedAtUtc,
                MembershipCreatedAtUtc = tu.CreatedAtUtc
            })
            .OrderByDescending(x => x.MembershipCreatedAtUtc)
            .ToList();

        return Ok(projects);
    }

    [HttpGet("team-members")]
    public IActionResult TeamMembers()
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();

        var team = db.TenantUsers
            .Where(tu => tu.TenantId == tenancy.TenantId)
            .Join(db.Users, tu => tu.UserId, u => u.Id, (tu, u) => new
            {
                id = u.Id,
                name = string.IsNullOrWhiteSpace(u.FullName) ? u.Email : u.FullName,
                email = u.Email,
                role = tu.Role
            })
            .OrderBy(x => x.name)
            .ToList();

        return Ok(team);
    }

    [HttpGet("app-bootstrap")]
    public async Task<IActionResult> AppBootstrap(CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();

        var entries = await db.PlatformSettings
            .Where(x => x.Scope == "mobile-app")
            .ToListAsync(ct);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
            values[e.Key] = crypto.Decrypt(e.ValueEncrypted);

        var appName = GetSetting(values, "appName", "Textzy");
        var baseDomain = GetSetting(values, "baseDomain", string.Empty);
        var apiBaseUrl = GetSetting(values, "apiBaseUrl", $"{Request.Scheme}://{Request.Host.Value}");
        var hubPath = GetSetting(values, "hubPath", "/hubs/inbox");
        var supportUrl = GetSetting(values, "supportUrl", string.Empty);
        var termsUrl = GetSetting(values, "termsUrl", string.Empty);
        var privacyUrl = GetSetting(values, "privacyUrl", string.Empty);
        var webPushPublicKey = GetSetting(values, "webPushPublicKey", string.Empty);
        var firebaseApiKey = GetSetting(values, "firebaseApiKey", string.Empty);
        var firebaseAuthDomain = GetSetting(values, "firebaseAuthDomain", string.Empty);
        var firebaseProjectId = GetSetting(values, "firebaseProjectId", string.Empty);
        var firebaseStorageBucket = GetSetting(values, "firebaseStorageBucket", string.Empty);
        var firebaseMessagingSenderId = GetSetting(values, "firebaseMessagingSenderId", string.Empty);
        var firebaseAppId = GetSetting(values, "firebaseAppId", string.Empty);
        var firebaseMeasurementId = GetSetting(values, "firebaseMeasurementId", string.Empty);
        var apiCatalog = ParseApiCatalog(values, DefaultAppApiCatalog);
        var allowedApiPrefixes = ParseAllowedPrefixes(values, apiCatalog);
        var enforceAllowList = ParseBool(GetSetting(values, "enforceApiAllowList", "false"));
        var maxDevicesPerUser = ParseInt(GetSetting(values, "maxDevicesPerUser", "3"), 3, 1, 20);
        var pairCodeTtlSeconds = ParseInt(GetSetting(values, "pairCodeTtlSeconds", "180"), 180, 60, 600);
        var minSupportedAppVersion = GetSetting(values, "minSupportedAppVersion", string.Empty);
        var pairSchemaVersion = GetSetting(values, "pairSchemaVersion", "1");

        return Ok(new
        {
            app = new
            {
                appName,
                baseDomain,
                apiBaseUrl,
                hubPath,
                supportUrl,
                termsUrl,
                privacyUrl,
                webPushPublicKey,
                firebaseApiKey,
                firebaseAuthDomain,
                firebaseProjectId,
                firebaseStorageBucket,
                firebaseMessagingSenderId,
                firebaseAppId,
                firebaseMeasurementId,
                enforceApiAllowList = enforceAllowList,
                allowedApiPrefixes,
                apiCatalog,
                maxDevicesPerUser,
                pairCodeTtlSeconds,
                minSupportedAppVersion,
                pairSchemaVersion
            },
            auth = new
            {
                auth.UserId,
                auth.Email,
                auth.Role,
                auth.TenantId,
                tenantSlug = tenancy.TenantSlug,
                permissions = auth.Permissions
            }
        });
    }

    [HttpGet("devices")]
    public IActionResult ListConnectedDevices()
    {
        if (!auth.IsAuthenticated) return Unauthorized();

        var devices = db.UserMobileDevices
            .Where(x => x.UserId == auth.UserId && x.TenantId == auth.TenantId && x.RevokedAtUtc == null)
            .OrderByDescending(x => x.LastSeenAtUtc)
            .Select(x => new
            {
                x.Id,
                x.DeviceName,
                x.Platform,
                x.AppVersion,
                x.CreatedAtUtc,
                x.LastSeenAtUtc
            })
            .ToList();

        return Ok(devices);
    }

    [HttpDelete("devices/{deviceId:guid}")]
    public async Task<IActionResult> RemoveConnectedDevice([FromRoute] Guid deviceId, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();

        var device = await db.UserMobileDevices
            .FirstOrDefaultAsync(x => x.Id == deviceId && x.UserId == auth.UserId && x.TenantId == auth.TenantId, ct);
        if (device is null) return NotFound("Device not found.");
        if (device.RevokedAtUtc is not null) return NoContent();

        device.RevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("devices/pair-qr")]
    public async Task<IActionResult> CreatePairQr([FromBody] PairQrRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!IsSecureRequest()) return StatusCode(StatusCodes.Status400BadRequest, "HTTPS is required.");

        var settings = await ReadMobileAppSettingsAsync(ct);
        var maxDevicesPerUser = ParseInt(GetSetting(settings, "maxDevicesPerUser", "3"), 3, 1, 20);
        var pairCodeTtlSeconds = ParseInt(GetSetting(settings, "pairCodeTtlSeconds", "180"), 180, 60, 600);
        var pairSchemaVersion = GetSetting(settings, "pairSchemaVersion", "1");
        var minSupportedAppVersion = GetSetting(settings, "minSupportedAppVersion", string.Empty);
        var apiBaseUrl = GetSetting(settings, "apiBaseUrl", $"{Request.Scheme}://{Request.Host.Value}");

        var activeDeviceCount = await db.UserMobileDevices
            .CountAsync(x => x.UserId == auth.UserId && x.TenantId == auth.TenantId && x.RevokedAtUtc == null, ct);
        if (activeDeviceCount >= maxDevicesPerUser)
            return BadRequest($"Device limit reached ({maxDevicesPerUser}). Remove a device first.");

        var token = CreateOpaqueToken(32);
        var tokenHash = HashToken(token);
        var now = DateTime.UtcNow;
        var expiresAt = now.AddSeconds(pairCodeTtlSeconds);
        var tenantSlug = tenancy.TenantSlug ?? string.Empty;

        var payload = new PairingPayloadDto
        {
            V = pairSchemaVersion,
            Token = token,
            ApiBaseUrl = apiBaseUrl,
            TenantSlug = tenantSlug,
            IssuedAtUtc = now,
            ExpiresAtUtc = expiresAt,
            MinSupportedAppVersion = minSupportedAppVersion,
            BuildHint = (request.BuildHint ?? string.Empty).Trim()
        };
        var payloadJson = JsonSerializer.Serialize(payload);

        db.MobilePairingRequests.Add(new MobilePairingRequest
        {
            Id = Guid.NewGuid(),
            UserId = auth.UserId,
            TenantId = auth.TenantId,
            PairingTokenHash = tokenHash,
            PairingPayloadJson = payloadJson,
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAt
        });
        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            qrPayload = payloadJson,
            pairingToken = token,
            expiresAtUtc = expiresAt,
            maxDevicesPerUser,
            activeDeviceCount
        });
    }

    [HttpGet("devices/pair-qr-image")]
    public async Task<IActionResult> GetPairQrImage([FromQuery] string pairingToken, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!IsSecureRequest()) return StatusCode(StatusCodes.Status400BadRequest, "HTTPS is required.");
        var token = (pairingToken ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(token)) return BadRequest("Pairing token is required.");

        var tokenHash = HashToken(token);
        var now = DateTime.UtcNow;
        var pair = await db.MobilePairingRequests
            .Where(x => x.PairingTokenHash == tokenHash)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (pair is null || pair.UserId != auth.UserId || pair.TenantId != auth.TenantId)
            return NotFound("Pair request not found.");
        if (pair.ConsumedAtUtc is not null || pair.ExpiresAtUtc <= now)
            return BadRequest("Pairing code expired or already used.");

        try
        {
            var encoded = Uri.EscapeDataString(pair.PairingPayloadJson);
            var qrUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=320x320&ecc=M&data={encoded}";
            var client = httpClientFactory.CreateClient();
            var qrBytes = await client.GetByteArrayAsync(qrUrl, ct);
            var qrBase64 = Convert.ToBase64String(qrBytes);
            var logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "textzy-landing-logo.svg");
            if (!System.IO.File.Exists(logoPath))
                logoPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "textzy-landing-logo.svg");
            if (!System.IO.File.Exists(logoPath))
                logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "textzy-logo-full.png");
            if (!System.IO.File.Exists(logoPath))
                logoPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "textzy-logo-full.png");

            var logoBase64 = System.IO.File.Exists(logoPath)
                ? Convert.ToBase64String(await System.IO.File.ReadAllBytesAsync(logoPath, ct))
                : string.Empty;
            var logoMime = logoPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                ? "image/svg+xml"
                : "image/png";
            var logoOverlay = string.IsNullOrWhiteSpace(logoBase64)
                ? string.Empty
                : $"<image x=\"31%\" y=\"43%\" width=\"38%\" height=\"14%\" href=\"data:{logoMime};base64,{logoBase64}\" preserveAspectRatio=\"xMidYMid meet\" />";
            var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"320\" height=\"320\" viewBox=\"0 0 320 320\"><image x=\"0\" y=\"0\" width=\"320\" height=\"320\" href=\"data:image/png;base64,{qrBase64}\"/>{logoOverlay}</svg>";
            return Content(svg, "image/svg+xml; charset=utf-8");
        }
        catch (Exception ex)
        {
            logger.LogWarning("Failed to render QR image: {Error}", redactor.RedactText(ex.Message));
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "QR image temporarily unavailable.");
        }
    }

    [HttpPost("projects")]
    public async Task<IActionResult> CreateProject([FromBody] CreateProjectRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest("Project name is required.");

        var baseSlug = NormalizeSlug(name);
        if (string.IsNullOrWhiteSpace(baseSlug)) return BadRequest("Invalid project name.");
        var slug = baseSlug;
        var index = 2;
        while (await db.Tenants.AnyAsync(t => t.Slug == slug, ct))
        {
            slug = $"{baseSlug}-{index++}";
        }

        var seedConnection = !string.IsNullOrWhiteSpace(tenancy.DataConnectionString)
            ? tenancy.DataConnectionString
            : db.Tenants.OrderBy(t => t.CreatedAtUtc).Select(t => t.DataConnectionString).FirstOrDefault()
            ?? db.Database.GetConnectionString()
            ?? string.Empty;
        if (string.IsNullOrWhiteSpace(seedConnection))
            return BadRequest("Unable to resolve project data connection.");

        var tenantId = Guid.NewGuid();
        var ownerGroupId = await EnsureOwnerGroupForUserAsync(auth.UserId, name, ct);
        try
        {
            using var tenantDb = SeedData.CreateTenantDbContext(seedConnection);
            tenantDb.Database.EnsureCreated();
            SeedData.InitializeTenant(tenantDb, tenantId);
        }
        catch (Exception ex)
        {
            return BadRequest($"Project DB initialization failed: {ex.Message}");
        }

        var tenant = new Tenant
        {
            Id = tenantId,
            Name = name,
            Slug = slug,
            OwnerGroupId = ownerGroupId,
            DataConnectionString = seedConnection
        };
        db.Tenants.Add(tenant);
        db.TenantUsers.Add(new TenantUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = auth.UserId,
            Role = "owner"
        });
        await db.SaveChangesAsync(ct);

        var token = await sessions.CreateSessionAsync(
            auth.UserId,
            tenant.Id,
            ct,
            auth.TwoFactorVerifiedAtUtc,
            auth.StepUpVerifiedAtUtc);
        authCookie.SetToken(HttpContext, token);
        authCookie.EnsureCsrfToken(HttpContext);
        WriteAuthHeaders(token);
        var ownerPermissions = await BuildEffectivePermissionsAsync(auth.UserId, tenant.Id, RolePermissionCatalog.Owner, ct);
        return Ok(new { tenant.Id, tenant.Name, tenant.Slug, role = "owner", permissions = ownerPermissions, accessToken = token });
    }

    [HttpPost("switch-project")]
    public async Task<IActionResult> SwitchProject([FromBody] SwitchProjectRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        var slug = (request.Slug ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(slug)) return BadRequest("Project slug is required.");

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, ct);
        if (tenant is null) return NotFound("Project not found.");

        var membership = await db.TenantUsers
            .FirstOrDefaultAsync(tu => tu.UserId == auth.UserId && tu.TenantId == tenant.Id, ct);
        if (membership is null) return Forbid();

        var token = await sessions.CreateSessionAsync(
            auth.UserId,
            tenant.Id,
            ct,
            auth.TwoFactorVerifiedAtUtc,
            auth.StepUpVerifiedAtUtc);
        authCookie.SetToken(HttpContext, token);
        authCookie.EnsureCsrfToken(HttpContext);
        WriteAuthHeaders(token);
        var role = membership.Role;
        var permissions = await BuildEffectivePermissionsAsync(auth.UserId, tenant.Id, role, ct);
        return Ok(new { accessToken = token, tenantSlug = tenant.Slug, projectName = tenant.Name, role, permissions });
    }

    private void WriteAuthHeaders(string token)
    {
        var csrf = authCookie.EnsureCsrfToken(HttpContext);
        Response.Headers["Authorization"] = $"Bearer {token}";
        Response.Headers["X-Access-Token"] = token;
        Response.Headers["X-CSRF-Token"] = csrf;
    }

    private bool UserRequiresAuthenticator(User user)
    {
        return user.AuthenticatorEnabledAtUtc.HasValue
            && !string.IsNullOrWhiteSpace(user.AuthenticatorProvider)
            && !string.IsNullOrWhiteSpace(user.AuthenticatorSecretEncrypted);
    }

    private async Task<(string token, string provider, DateTime expiresAtUtc)> CreateTwoFactorChallengeAsync(
        User user,
        Guid tenantId,
        string purpose,
        string actionCode,
        Guid? sessionTokenId,
        int expiresMinutes,
        CancellationToken ct)
    {
        var token = CreateOpaqueToken(32);
        var now = DateTime.UtcNow;
        var challenge = new TwoFactorChallenge
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TenantId = tenantId,
            SessionTokenId = sessionTokenId,
            Purpose = purpose,
            Provider = user.AuthenticatorProvider,
            ActionCode = actionCode,
            ChallengeTokenHash = HashToken(token),
            ExpiresAtUtc = now.AddMinutes(expiresMinutes),
            CreatedAtUtc = now
        };
        db.TwoFactorChallenges.Add(challenge);
        await db.SaveChangesAsync(ct);
        return (token, challenge.Provider, challenge.ExpiresAtUtc);
    }

    private static bool HasFreshStepUp(AuthContext auth, int freshnessMinutes)
    {
        return auth.StepUpVerifiedAtUtc.HasValue && auth.StepUpVerifiedAtUtc.Value >= DateTime.UtcNow.AddMinutes(-freshnessMinutes);
    }

    private ObjectResult BuildStepUpRequired(string action, string title, string message)
    {
        return StatusCode(StatusCodes.Status428PreconditionRequired, new
        {
            stepUpRequired = true,
            action,
            title,
            message
        });
    }

    private static string NormalizeSlug(string value)
    {
        var lowered = value.Trim().ToLowerInvariant();
        var chars = lowered.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return slug.Trim('-');
    }

    private static string GetSetting(Dictionary<string, string> values, string key, string fallback)
    {
        return values.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw)
            ? raw.Trim()
            : fallback;
    }

    private static string[] ParseApiCatalog(Dictionary<string, string> values, string[] fallback)
    {
        var raw = GetSetting(values, "apiCatalog", string.Empty);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;

        var parsed = ParseStringArray(raw);
        return parsed.Length == 0 ? fallback : parsed;
    }

    private static string[] ParseAllowedPrefixes(Dictionary<string, string> values, string[] fallback)
    {
        var raw = GetSetting(values, "allowedApiPrefixes", string.Empty);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;

        var parsed = ParseStringArray(raw);
        return parsed.Length == 0 ? fallback : parsed;
    }

    private static string[] ParseStringArray(string raw)
    {
        try
        {
            if (raw.TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                var fromJson = JsonSerializer.Deserialize<string[]>(raw);
                if (fromJson is not null)
                {
                    return fromJson
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
            }
        }
        catch
        {
            // fallback to csv/newline parsing
        }

        return raw
            .Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool ParseBool(string raw)
    {
        return bool.TryParse(raw, out var value) && value;
    }

    private async Task<Dictionary<string, string>> ReadMobileAppSettingsAsync(CancellationToken ct)
    {
        var entries = await db.PlatformSettings
            .Where(x => x.Scope == "mobile-app")
            .ToListAsync(ct);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
            values[e.Key] = crypto.Decrypt(e.ValueEncrypted);
        return values;
    }

    private static int ParseInt(string raw, int fallback, int min, int max)
    {
        if (!int.TryParse(raw, out var value)) value = fallback;
        if (value < min) value = min;
        if (value > max) value = max;
        return value;
    }

    private async Task<Guid> EnsureOwnerGroupForUserAsync(Guid userId, string projectName, CancellationToken ct)
    {
        var ownerTenantIds = await db.TenantUsers
            .Where(x => x.UserId == userId && x.Role.ToLower() == "owner")
            .Select(x => x.TenantId)
            .ToListAsync(ct);

        var existingGroupId = await db.Tenants
            .Where(t => ownerTenantIds.Contains(t.Id) && t.OwnerGroupId.HasValue)
            .Select(t => t.OwnerGroupId)
            .FirstOrDefaultAsync(ct);

        if (existingGroupId.HasValue && existingGroupId.Value != Guid.Empty)
            return existingGroupId.Value;

        var group = await db.TenantOwnerGroups
            .Where(g => g.OwnerUserId == userId && g.IsActive)
            .OrderBy(g => g.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (group is null)
        {
            group = new TenantOwnerGroup
            {
                Id = Guid.NewGuid(),
                OwnerUserId = userId,
                Name = $"{projectName} Group",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.TenantOwnerGroups.Add(group);
            await db.SaveChangesAsync(ct);
        }

        if (ownerTenantIds.Count > 0)
        {
            var tenantsToPatch = await db.Tenants.Where(t => ownerTenantIds.Contains(t.Id) && !t.OwnerGroupId.HasValue).ToListAsync(ct);
            foreach (var t in tenantsToPatch) t.OwnerGroupId = group.Id;
            if (tenantsToPatch.Count > 0) await db.SaveChangesAsync(ct);
        }

        return group.Id;
    }

    private async Task<Guid> EnsureTenantOwnerGroupForInviteAsync(Tenant tenant, Guid fallbackOwnerUserId, CancellationToken ct)
    {
        if (tenant.OwnerGroupId.HasValue && tenant.OwnerGroupId.Value != Guid.Empty) return tenant.OwnerGroupId.Value;
        var ownerUserId = await db.TenantUsers
            .Where(x => x.TenantId == tenant.Id && x.Role.ToLower() == "owner")
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => x.UserId)
            .FirstOrDefaultAsync(ct);
        if (ownerUserId == Guid.Empty) ownerUserId = fallbackOwnerUserId;
        var group = await db.TenantOwnerGroups
            .Where(g => g.OwnerUserId == ownerUserId && g.IsActive)
            .OrderBy(g => g.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (group is null)
        {
            group = new TenantOwnerGroup
            {
                Id = Guid.NewGuid(),
                OwnerUserId = ownerUserId,
                Name = $"{tenant.Name} Group",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.TenantOwnerGroups.Add(group);
        }
        tenant.OwnerGroupId = group.Id;
        await db.SaveChangesAsync(ct);
        return group.Id;
    }

    private async Task<bool> CanUserJoinOwnerGroupAsync(Guid userId, Guid tenantOwnerGroupId, CancellationToken ct)
    {
        var userTenantIds = await db.TenantUsers.Where(x => x.UserId == userId).Select(x => x.TenantId).Distinct().ToListAsync(ct);
        if (userTenantIds.Count == 0) return true;
        var otherGroupIds = await db.Tenants
            .Where(t => userTenantIds.Contains(t.Id) && t.OwnerGroupId.HasValue)
            .Select(t => t.OwnerGroupId!.Value)
            .Distinct()
            .ToListAsync(ct);
        return otherGroupIds.Count == 0 || (otherGroupIds.Count == 1 && otherGroupIds[0] == tenantOwnerGroupId);
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private static string CreateOpaqueToken(int byteLength)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToBase64String(tokenBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string GenerateNumericCode(int len)
    {
        var chars = new char[len];
        for (var i = 0; i < len; i++) chars[i] = (char)('0' + RandomNumberGenerator.GetInt32(0, 10));
        return new string(chars);
    }

    private static string GenerateAlphaNumericCode(int len)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = new char[len];
        for (var i = 0; i < len; i++) chars[i] = alphabet[RandomNumberGenerator.GetInt32(0, alphabet.Length)];
        return new string(chars);
    }

    private async Task<string> GetEmailVerificationModeAsync(CancellationToken ct)
    {
        static string NormalizeMode(string? raw)
        {
            var mode = (raw ?? string.Empty).Trim().ToLowerInvariant();
            return mode switch
            {
                "every-login" or "every_login" or "login" or "required" => "every-login",
                "registration" or "register" => "registration",
                _ => "none"
            };
        }

        var row = await db.PlatformSettings
            .Where(x => x.Scope == "auth-security" && x.Key == "emailVerificationMode")
            .FirstOrDefaultAsync(ct);
        if (row is null)
        {
            var fallback = config["Auth:EmailVerificationMode"] ?? config["EMAIL_VERIFICATION_MODE"] ?? "every-login";
            return NormalizeMode(fallback);
        }

        return NormalizeMode(crypto.Decrypt(row.ValueEncrypted));
    }

    private string BuildEmailVerificationLink(Guid verificationId, string token, string purpose)
    {
        var scheme = Request.IsHttps
            ? Request.Scheme
            : (string.Equals(Request.Headers["X-Forwarded-Proto"].FirstOrDefault(), "https", StringComparison.OrdinalIgnoreCase)
                ? "https"
                : Request.Scheme);
        var host = Request.Host.Value;
        return $"{scheme}://{host}/api/auth/email-verification/link?verificationId={verificationId}&purpose={Uri.EscapeDataString(purpose)}&token={Uri.EscapeDataString(token)}";
    }

    private static string GetVerificationState(EmailOtpVerification row, DateTime now)
    {
        if (row.ConsumedAtUtc is not null) return "consumed";
        if (row.AttemptCount >= row.MaxAttempts) return "expired";
        if (row.IsVerified || string.Equals(row.Status, "verified", StringComparison.OrdinalIgnoreCase)) return "verified";

        var raw = (row.Status ?? string.Empty).Trim().ToLowerInvariant();
        if (raw is "email_sent")
            return row.ExpiresAtUtc <= now ? "expired" : "email_sent";
        if (raw is "link_opened")
            return row.ExpiresAtUtc <= now ? "expired" : "link_opened";

        var otpExpiry = row.OtpExpiresAtUtc ?? row.ExpiresAtUtc;
        if (raw is "code_issued")
            return otpExpiry <= now ? "expired" : "code_issued";

        if (row.LinkOpenedAtUtc is null) return row.ExpiresAtUtc <= now ? "expired" : "email_sent";
        return otpExpiry <= now ? "expired" : "code_issued";
    }

    private (bool ok, string email, string error) ResolveEmailForOtpRequest(string? rawEmail)
    {
        if (!string.IsNullOrWhiteSpace(rawEmail))
        {
            try
            {
                var email = InputGuardService.RequireTrimmed(rawEmail, "Email", 320).ToLowerInvariant();
                return (true, email, string.Empty);
            }
            catch (InvalidOperationException ex)
            {
                return (false, string.Empty, ex.Message);
            }
        }

        if (auth.IsAuthenticated && !string.IsNullOrWhiteSpace(auth.Email))
            return (true, auth.Email.Trim().ToLowerInvariant(), string.Empty);
        return (false, string.Empty, "Email is required.");
    }

    private (bool hasEmail, string email) ResolveEmailForOtpStatus(string? rawEmail)
    {
        if (!string.IsNullOrWhiteSpace(rawEmail))
            return (true, rawEmail.Trim().ToLowerInvariant());
        if (auth.IsAuthenticated && !string.IsNullOrWhiteSpace(auth.Email))
            return (true, auth.Email.Trim().ToLowerInvariant());
        return (false, string.Empty);
    }

    private static string NormalizeOtpPurpose(string? rawPurpose)
    {
        var v = (rawPurpose ?? string.Empty).Trim().ToLowerInvariant();
        return v switch
        {
            "login" => "login",
            "every-login" => "every-login",
            "registration" => "registration",
            "forgot-password" => "forgot-password",
            "first-login" => "first-login",
            "device-binding" => "device-binding",
            _ => "login"
        };
    }

    private async Task<string[]> BuildEffectivePermissionsAsync(Guid userId, Guid tenantId, string role, CancellationToken ct)
    {
        var effective = RolePermissionCatalog.GetPermissions(role).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var overrides = await db.TenantUserPermissionOverrides
            .Where(x => x.TenantId == tenantId && x.UserId == userId)
            .ToListAsync(ct);

        foreach (var ov in overrides)
        {
            if (ov.IsAllowed) effective.Add(ov.Permission);
            else effective.Remove(ov.Permission);
        }

        return effective.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public sealed class VerifyLoginTwoFactorRequest
    {
        public string ChallengeToken { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }

    public sealed class RequestStepUpAuthRequest
    {
        public string Action { get; set; } = "sensitive_action";
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public sealed class VerifyStepUpAuthRequest
    {
        public string ChallengeToken { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }

    private static string BuildVerificationHtml(
        string message,
        string? code,
        bool success,
        Guid? verificationId = null,
        string? purpose = null,
        DateTime? otpExpiresAtUtc = null)
    {
        var safeMessage = System.Net.WebUtility.HtmlEncode(message);
        var safeCode = string.IsNullOrWhiteSpace(code) ? string.Empty : System.Net.WebUtility.HtmlEncode(code);
        var codeBlock = string.IsNullOrWhiteSpace(safeCode)
            ? string.Empty
            : $"""<div style="margin:16px 0;padding:14px;border-radius:10px;background:#fff7ed;border:1px dashed #f97316;text-align:center;font-size:32px;letter-spacing:8px;font-weight:800;color:#111827;">{safeCode}</div>""";
        var expiresUnixMs = otpExpiresAtUtc.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(otpExpiresAtUtc.Value, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
            : 0;
        var verificationIdJs = verificationId?.ToString() ?? string.Empty;
        var purposeJs = purpose ?? "login";
        var scriptBlock = string.Empty;
        if (!string.IsNullOrWhiteSpace(safeCode) && verificationId is not null)
        {
            var escapedPurpose = purposeJs.Replace("\\", "\\\\").Replace("\"", "\\\"");
            scriptBlock =
                "<script>(function(){" +
                $"const vid=\"{verificationIdJs}\";" +
                $"const purpose=\"{escapedPurpose}\";" +
                $"const expiresAtMs={expiresUnixMs};" +
                "const timerEl=document.getElementById(\"otp-timer\");" +
                "const statusEl=document.getElementById(\"otp-status\");" +
                "function fmt(sec){const m=Math.floor(sec/60);const s=sec%60;return m+\":\"+String(s).padStart(2,\"0\");}" +
                "function tick(){if(!timerEl||!expiresAtMs)return;const left=Math.max(0,Math.floor((expiresAtMs-Date.now())/1000));timerEl.textContent=left>0?(\"Code expires in \"+fmt(left)):\"Code expired. Please request a new verification email.\";}" +
                "async function poll(){try{const url=\"/api/auth/email-verification/status?verificationId=\"+encodeURIComponent(vid)+\"&purpose=\"+encodeURIComponent(purpose);const r=await fetch(url,{credentials:\"include\",cache:\"no-store\"});if(!r.ok)return;const d=await r.json();const state=(d?.state||\"\").toLowerCase();const status=(d?.status||\"\").toLowerCase();if(state===\"verified\"||status===\"verified\"||state===\"consumed\"||status===\"consumed\"){if(statusEl)statusEl.textContent=\"Verification success. You can close this tab now.\";}}catch{}}" +
                "tick();setInterval(tick,1000);setInterval(poll,2000);" +
                "})();</script>";
        }
        var icon = success ? "Verified" : "Error";
        return $"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <title>Textzy Verification</title>
        </head>
        <body style="margin:0;background:#fff7ed;font-family:Segoe UI,Arial,sans-serif;color:#1f2937;">
          <div style="max-width:560px;margin:44px auto;padding:0 16px;">
            <div style="background:#ffffff;border-radius:14px;box-shadow:0 8px 24px rgba(0,0,0,.08);overflow:hidden;">
              <div style="background:#f97316;color:#fff;padding:16px 20px;font-size:20px;font-weight:700;">Textzy Verification</div>
              <div style="padding:20px;">
                <div style="font-size:14px;color:#6b7280;margin-bottom:8px;">{icon}</div>
                <div style="font-size:16px;line-height:1.5;">{safeMessage}</div>
                {codeBlock}
                <div id="otp-status" style="margin-top:10px;font-size:13px;color:#6b7280;">Paste the code into your main screen OTP box.</div>
                <div id="otp-timer" style="margin-top:8px;font-size:13px;color:#dc2626;"></div>
              </div>
            </div>
          </div>
          {scriptBlock}
        </body>
        </html>
        """;
    }

    private bool IsSecureRequest()
    {
        if (Request.IsHttps) return true;
        if (string.Equals(Request.Headers["X-Forwarded-Proto"].FirstOrDefault(), "https", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    public sealed class AcceptInviteRequest
    {
        public string Token { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public sealed class PairQrRequest
    {
        public string BuildHint { get; set; } = string.Empty;
    }

    public sealed class RequestEmailVerificationOtpRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Purpose { get; set; } = "login";
    }

    public sealed class VerifyEmailOtpRequest
    {
        public string VerificationId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Purpose { get; set; } = "login";
        public string Otp { get; set; } = string.Empty;
    }

    public sealed class PairingPayloadDto
    {
        public string V { get; set; } = "1";
        public string Token { get; set; } = string.Empty;
        public string ApiBaseUrl { get; set; } = string.Empty;
        public string TenantSlug { get; set; } = string.Empty;
        public DateTime IssuedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public string MinSupportedAppVersion { get; set; } = string.Empty;
        public string BuildHint { get; set; } = string.Empty;
    }
}
