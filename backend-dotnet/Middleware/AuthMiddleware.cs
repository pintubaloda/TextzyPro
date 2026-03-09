using Textzy.Api.Data;
using Textzy.Api.Services;
using System.Security.Cryptography;
using System.Text;
using System.Net;
using Microsoft.EntityFrameworkCore;

namespace Textzy.Api.Middleware;

public class AuthMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next;

    public async Task Invoke(
        HttpContext context,
        SessionService sessions,
        ControlDbContext db,
        TenancyContext tenancy,
        AuthContext auth,
        BillingGuardService billingGuard,
        SecretCryptoService crypto,
        AuthCookieService authCookie,
        IConfiguration config,
        IHostEnvironment env)
    {
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        var isAuthPath = path.StartsWith("/api/auth/login", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/auth/two-factor", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/auth/email-verification", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/auth/accept-invite", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/auth/invite-preview", StringComparison.OrdinalIgnoreCase);
        var isAuthRefreshPath = path.StartsWith("/api/auth/refresh", StringComparison.OrdinalIgnoreCase);
        var isProjectPath = path.StartsWith("/api/auth/projects", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/auth/switch-project", StringComparison.OrdinalIgnoreCase);
        var isPublicTenantPath = path.StartsWith("/api/tenants", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/public", StringComparison.OrdinalIgnoreCase);
        var isSwaggerPath = path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase);
        var isHubPath = path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase);
        var isWabaWebhookPath = path.StartsWith("/api/waba/webhook", StringComparison.OrdinalIgnoreCase);
        var isPaymentWebhookPath = path.StartsWith("/api/payments/webhook", StringComparison.OrdinalIgnoreCase);
        var isEmailWebhookPath = path.StartsWith("/api/email/webhook", StringComparison.OrdinalIgnoreCase);
        var isCsrfExemptPath = isAuthPath || isAuthRefreshPath || isProjectPath || isPublicTenantPath || isSwaggerPath || isHubPath || isWabaWebhookPath || isPaymentWebhookPath || isEmailWebhookPath;

        if (isAuthPath || isPublicTenantPath || isSwaggerPath || isWabaWebhookPath || isPaymentWebhookPath || isEmailWebhookPath)
        {
            await _next(context);
            return;
        }

        var apiClientAuthenticated = false;
        if (!isCsrfExemptPath)
        {
            apiClientAuthenticated = await TryAuthenticateApiClientAsync(context, db, tenancy, auth, crypto, config);
        }

        if (env.IsProduction() && IsUnsafeMethod(context.Request.Method))
        {
            var origin = context.Request.Headers.Origin.ToString().Trim().TrimEnd('/');
            var referer = context.Request.Headers.Referer.ToString();
            // Native/mobile clients typically do not send Origin/Referer.
            // Enforce origin checks only when request looks browser-initiated.
            var hasBrowserOriginContext = !string.IsNullOrWhiteSpace(origin) || !string.IsNullOrWhiteSpace(referer);
            if (hasBrowserOriginContext && !IsTrustedOrigin(origin, referer, config))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Untrusted origin.");
                return;
            }
        }
        if (IsUnsafeMethod(context.Request.Method) && !isCsrfExemptPath && !apiClientAuthenticated)
        {
            if (!HasValidDoubleSubmitCsrf(context, authCookie))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Invalid CSRF token.");
                return;
            }
        }

        var header = context.Request.Headers.Authorization.ToString();
        var opaqueToken = string.Empty;
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            opaqueToken = header["Bearer ".Length..].Trim();
        }
        else
        {
            opaqueToken = authCookie.ReadToken(context) ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(opaqueToken) && isHubPath)
        {
            opaqueToken = context.Request.Query["access_token"].FirstOrDefault()?.Trim() ?? string.Empty;
        }

        if (apiClientAuthenticated)
        {
            await _next(context);
            if (ShouldCountApiUsage(context.Request.Path.Value ?? string.Empty, context.Response.StatusCode))
            {
                try
                {
                    await billingGuard.TryConsumeAsync(tenancy.TenantId, "apiCalls", 1, context.RequestAborted);
                }
                catch
                {
                    // Non-blocking metering only.
                }
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(opaqueToken))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Missing bearer token.");
            return;
        }
        var session = sessions.Validate(opaqueToken);
        if (session is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Invalid or expired session.");
            return;
        }

        if (!tenancy.IsSet)
        {
            var sessionTenant = db.Tenants.FirstOrDefault(t => t.Id == session.TenantId);
            if (sessionTenant is null)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Session tenant not found.");
                return;
            }
            tenancy.SetTenant(sessionTenant.Id, sessionTenant.Slug, sessionTenant.DataConnectionString);
        }
        else if (session.TenantId != tenancy.TenantId)
        {
            var sessionTenant = db.Tenants.FirstOrDefault(t => t.Id == session.TenantId);
            if (sessionTenant is null)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Session tenant not found.");
                return;
            }
            tenancy.SetTenant(sessionTenant.Id, sessionTenant.Slug, sessionTenant.DataConnectionString);
        }

        var user = db.Users.FirstOrDefault(u => u.Id == session.UserId && u.IsActive);
        if (user is null)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("User not active.");
            return;
        }

        if (isProjectPath)
        {
            auth.Set(
                user.Id,
                session.TenantId,
                user.Email,
                user.IsSuperAdmin ? RolePermissionCatalog.SuperAdmin : "owner",
                null,
                user.FullName,
                session.Id,
                session.TwoFactorVerifiedAtUtc,
                session.StepUpVerifiedAtUtc);
            await _next(context);
            return;
        }

        if (user.IsSuperAdmin)
        {
            auth.Set(
                user.Id,
                session.TenantId,
                user.Email,
                RolePermissionCatalog.SuperAdmin,
                null,
                user.FullName,
                session.Id,
                session.TwoFactorVerifiedAtUtc,
                session.StepUpVerifiedAtUtc);
            await _next(context);
            return;
        }

        var tenantUser = db.TenantUsers.FirstOrDefault(tu => tu.UserId == session.UserId && tu.TenantId == session.TenantId);
        if (tenantUser is null)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("User is not assigned to tenant.");
            return;
        }

        var effective = RolePermissionCatalog.GetPermissions(tenantUser.Role).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var overrides = db.TenantUserPermissionOverrides
            .Where(x => x.TenantId == session.TenantId && x.UserId == session.UserId)
            .ToList();
        foreach (var ov in overrides)
        {
            if (ov.IsAllowed) effective.Add(ov.Permission);
            else effective.Remove(ov.Permission);
        }

        auth.Set(
            user.Id,
            session.TenantId,
            user.Email,
            tenantUser.Role,
            effective.ToList(),
            user.FullName,
            session.Id,
            session.TwoFactorVerifiedAtUtc,
            session.StepUpVerifiedAtUtc);
        await _next(context);

        if (ShouldCountApiUsage(context.Request.Path.Value ?? string.Empty, context.Response.StatusCode))
        {
            try
            {
                await billingGuard.TryConsumeAsync(session.TenantId, "apiCalls", 1, context.RequestAborted);
            }
            catch
            {
                // Non-blocking metering only.
            }
        }
    }

    private static bool IsUnsafeMethod(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    private static async Task<bool> TryAuthenticateApiClientAsync(
        HttpContext context,
        ControlDbContext db,
        TenancyContext tenancy,
        AuthContext auth,
        SecretCryptoService crypto,
        IConfiguration config)
    {
        var apiKey = context.Request.Headers["X-API-Key"].FirstOrDefault()?.Trim() ?? string.Empty;
        var apiSecret = context.Request.Headers["X-API-Secret"].FirstOrDefault()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
            return false;

        if (!IsHttpsRequest(context))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("HTTPS is required for API key authentication.");
            return false;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        if (!IsApiClientPathAllowed(path))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("API key authentication not allowed for this endpoint.");
            return false;
        }

        // Tenant context should already be resolved by TenantMiddleware for non-auth/public paths.
        if (!tenancy.IsSet)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Missing tenant context for API key authentication.");
            return false;
        }

        var profile = await db.TenantCompanyProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId, context.RequestAborted);
        var expectedKey = crypto.Decrypt(profile?.ApiKeyEncrypted ?? string.Empty);
        var expectedSecret = crypto.Decrypt(profile?.ApiPasswordEncrypted ?? string.Empty);
        var allowIpsRaw = profile?.ApiIpWhitelist ?? string.Empty;
        var enabled = profile?.PublicApiEnabled == true;
        if (!enabled)
            return false;

        if (!SecureEquals(apiKey, expectedKey) || !SecureEquals(apiSecret, expectedSecret))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Invalid API credentials.");
            return false;
        }

        if (!IsIpAllowed(context.Connection.RemoteIpAddress, allowIpsRaw))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Client IP is not allowed.");
            return false;
        }

        var syntheticUserId = CreateStableGuid($"{tenancy.TenantId}:api-client:{expectedKey}");
        auth.Set(
            syntheticUserId,
            tenancy.TenantId,
            "api-client@textzy.local",
            "api_client",
            new[]
            {
                PermissionCatalog.ApiRead,
                PermissionCatalog.ApiWrite
            },
            "API Client");

        return true;
    }

    private static bool IsApiClientPathAllowed(string path)
    {
        return path.StartsWith("/api/messages/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/sms/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/waba/smoke/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/automation/meta/flows", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/automation/metrics/flows", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHttpsRequest(HttpContext context)
    {
        if (context.Request.IsHttps) return true;
        var xfProto = context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault();
        return string.Equals(xfProto, "https", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SecureEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left ?? string.Empty);
        var b = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static bool IsIpAllowed(IPAddress? remoteIp, string rawWhitelist)
    {
        if (string.IsNullOrWhiteSpace(rawWhitelist)) return true; // optional whitelist
        if (remoteIp is null) return false;

        var ip = remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4() : remoteIp;
        var rules = rawWhitelist.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule)) continue;
            if (string.Equals(rule, "*", StringComparison.Ordinal)) return true;
            if (TryMatchCidr(ip, rule)) return true;
            if (IPAddress.TryParse(rule, out var allowed))
            {
                var normalizedAllowed = allowed.IsIPv4MappedToIPv6 ? allowed.MapToIPv4() : allowed;
                if (normalizedAllowed.Equals(ip)) return true;
            }
        }
        return false;
    }

    private static bool TryMatchCidr(IPAddress ip, string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2) return false;
        if (!IPAddress.TryParse(parts[0], out var baseIp)) return false;
        if (!int.TryParse(parts[1], out var prefixLength)) return false;

        var ipBytes = (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).GetAddressBytes();
        var baseBytes = (baseIp.IsIPv4MappedToIPv6 ? baseIp.MapToIPv4() : baseIp).GetAddressBytes();
        if (ipBytes.Length != baseBytes.Length) return false;

        var bits = ipBytes.Length * 8;
        if (prefixLength < 0 || prefixLength > bits) return false;

        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var i = 0; i < fullBytes; i++)
        {
            if (ipBytes[i] != baseBytes[i]) return false;
        }

        if (remainingBits == 0) return true;
        var mask = (byte)(0xFF << (8 - remainingBits));
        return (ipBytes[fullBytes] & mask) == (baseBytes[fullBytes] & mask);
    }

    private static Guid CreateStableGuid(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        return new Guid(bytes);
    }

    private static bool ShouldCountApiUsage(string path, int statusCode)
    {
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) return false;
        if (path.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase)) return false;
        if (path.StartsWith("/api/public/", StringComparison.OrdinalIgnoreCase)) return false;
        if (path.StartsWith("/api/waba/webhook", StringComparison.OrdinalIgnoreCase)) return false;
        if (path.StartsWith("/api/payments/webhook", StringComparison.OrdinalIgnoreCase)) return false;
        if (path.StartsWith("/api/email/webhook", StringComparison.OrdinalIgnoreCase)) return false;
        if (path.StartsWith("/api/billing/", StringComparison.OrdinalIgnoreCase)) return false;
        return statusCode is >= 200 and < 500;
    }

    private static bool HasValidDoubleSubmitCsrf(HttpContext context, AuthCookieService authCookie)
    {
        var cookieToken = authCookie.ReadCsrfToken(context);
        if (string.IsNullOrWhiteSpace(cookieToken)) return false;

        var headerToken = context.Request.Headers["X-CSRF-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(headerToken))
            headerToken = context.Request.Headers["X-XSRF-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(headerToken)) return false;

        var cookieBytes = Encoding.UTF8.GetBytes(cookieToken);
        var headerBytes = Encoding.UTF8.GetBytes(headerToken.Trim());
        return cookieBytes.Length == headerBytes.Length &&
               CryptographicOperations.FixedTimeEquals(cookieBytes, headerBytes);
    }

    private static bool IsTrustedOrigin(string origin, string referer, IConfiguration config)
    {
        var allowed = ParseAllowedOrigins(config);

        if (allowed.Count == 0) return false;
        if (!string.IsNullOrWhiteSpace(origin) && allowed.Contains(origin)) return true;

        if (!string.IsNullOrWhiteSpace(referer))
        {
            foreach (var a in allowed)
            {
                if (referer.StartsWith(a, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }

        return false;
    }

    private static HashSet<string> ParseAllowedOrigins(IConfiguration config)
    {
        var raw = config["AllowedOrigins"] ?? string.Empty;
        var parsed = raw
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim().TrimEnd('/'))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (parsed.Count == 0)
        {
            parsed.Add("https://textzy-frontend-production.up.railway.app");
            parsed.Add("https://textzy-backend-production.up.railway.app");
            parsed.Add("http://localhost:3000");
            parsed.Add("http://localhost:5173");
        }

        return parsed;
    }
}
