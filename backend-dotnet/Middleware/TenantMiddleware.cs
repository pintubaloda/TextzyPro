using Textzy.Api.Data;
using Textzy.Api.Services;

namespace Textzy.Api.Middleware;

public class TenantMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next;

    public async Task Invoke(
        HttpContext context,
        ControlDbContext db,
        TenancyContext tenancy,
        TenantSchemaGuardService schemaGuard,
        SessionService sessions,
        AuthCookieService authCookie)
    {
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        var isSwaggerPath = path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase);
        var isHubPath = path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase);
        var isWabaWebhook = path.StartsWith("/api/waba/webhook", StringComparison.OrdinalIgnoreCase);
        var isPaymentWebhook = path.StartsWith("/api/payments/webhook", StringComparison.OrdinalIgnoreCase);
        var isAuthPath = path.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase);
        var isPublicPath = path.StartsWith("/api/public", StringComparison.OrdinalIgnoreCase);
        var isPublicMessagesSendPath = path.StartsWith("/api/public/messages/send", StringComparison.OrdinalIgnoreCase);
        if (isPublicMessagesSendPath)
        {
            var msgTenantSlug = context.Request.Query["tenantSlug"].FirstOrDefault()?.Trim();
            if (string.IsNullOrWhiteSpace(msgTenantSlug))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("tenantSlug query parameter is required.");
                return;
            }

            msgTenantSlug = msgTenantSlug.ToLowerInvariant();
            var msgTenant = db.Tenants.FirstOrDefault(t => t.Slug == msgTenantSlug);
            if (msgTenant is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("Tenant not found.");
                return;
            }

            tenancy.SetTenant(msgTenant.Id, msgTenant.Slug, msgTenant.DataConnectionString);
            try
            {
                await schemaGuard.EnsureContactEncryptionColumnsAsync(msgTenant.Id, msgTenant.DataConnectionString, context.RequestAborted);
            }
            catch
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsync("Tenant schema initialization failed.");
                return;
            }

            await _next(context);
            return;
        }

        if (isSwaggerPath || isHubPath || isWabaWebhook || isPaymentWebhook || isAuthPath || (isPublicPath && !isPublicMessagesSendPath))
        {
            await _next(context);
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        var bearerToken = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : string.Empty;
        var cookieToken = authCookie.ReadToken(context) ?? string.Empty;
        var opaqueToken = !string.IsNullOrWhiteSpace(bearerToken) ? bearerToken : cookieToken;

        // For authenticated requests, session tenant is the single source of truth.
        // Ignore X-Tenant-Slug to avoid stale/mismatched tenant context.
        if (!string.IsNullOrWhiteSpace(opaqueToken))
        {
            var session = sessions.Validate(opaqueToken);
            if (session is not null)
            {
                var sessionTenant = db.Tenants.FirstOrDefault(t => t.Id == session.TenantId);
                if (sessionTenant is null)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsync("Session tenant not found.");
                    return;
                }

                tenancy.SetTenant(sessionTenant.Id, sessionTenant.Slug, sessionTenant.DataConnectionString);
                try
                {
                    await schemaGuard.EnsureContactEncryptionColumnsAsync(sessionTenant.Id, sessionTenant.DataConnectionString, context.RequestAborted);
                }
                catch
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsync("Tenant schema initialization failed.");
                    return;
                }

                await _next(context);
                return;
            }
        }

        if (!context.Request.Headers.TryGetValue("X-Tenant-Slug", out var tenantSlug))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Missing X-Tenant-Slug header.");
            return;
        }

        var tenant = db.Tenants.FirstOrDefault(t => t.Slug == tenantSlug.ToString());
        if (tenant is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync("Tenant not found.");
            return;
        }

        tenancy.SetTenant(tenant.Id, tenant.Slug, tenant.DataConnectionString);
        try
        {
            await schemaGuard.EnsureContactEncryptionColumnsAsync(tenant.Id, tenant.DataConnectionString, context.RequestAborted);
        }
        catch
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsync("Tenant schema initialization failed.");
            return;
        }
        await _next(context);
    }
}
