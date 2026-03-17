namespace Textzy.Api.Middleware;

public class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var isStyledHtmlPage =
            path.StartsWith("/api/auth/email-verification/link", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/public/invoices/verify", StringComparison.OrdinalIgnoreCase) ||
            (path.StartsWith("/api/billing/invoices/", StringComparison.OrdinalIgnoreCase) &&
             path.EndsWith("/download", StringComparison.OrdinalIgnoreCase)) ||
            (path.StartsWith("/api/platform/purchases/", StringComparison.OrdinalIgnoreCase) &&
             path.EndsWith("/download", StringComparison.OrdinalIgnoreCase));

        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        context.Response.Headers["Content-Security-Policy"] = isStyledHtmlPage
            ? "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'"
            : "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'; object-src 'none'";
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers.Remove("X-Powered-By");

        await next(context);
    }
}
