namespace Textzy.Api.Middleware;

public class FrontendCorsMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (!string.IsNullOrWhiteSpace(origin) &&
            (string.Equals(origin, "https://textzy.in", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(origin, "https://www.textzy.in", StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = origin;
            context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
            context.Response.Headers["Access-Control-Allow-Headers"] = "Authorization, X-Access-Token, X-CSRF-Token, X-Tenant-Slug, Content-Type";
            context.Response.Headers["Access-Control-Allow-Methods"] = "GET,POST,PUT,PATCH,DELETE,OPTIONS";
            context.Response.Headers["Access-Control-Expose-Headers"] = "Authorization, X-Access-Token, X-CSRF-Token, X-Textzy-Build, X-Textzy-Body-Len";
            if (context.Request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }
        }

        await next(context);
    }
}
