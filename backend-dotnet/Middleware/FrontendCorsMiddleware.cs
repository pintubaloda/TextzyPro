using Textzy.Api.Utilities;

namespace Textzy.Api.Middleware;

public class FrontendCorsMiddleware(RequestDelegate next, FrontendCorsOptions corsOptions)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (CorsOriginHelper.IsAllowedOrigin(corsOptions, origin))
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = origin;
            context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
            context.Response.Headers["Vary"] = "Origin";
            var requestedHeaders = context.Request.Headers["Access-Control-Request-Headers"].ToString();
            context.Response.Headers["Access-Control-Allow-Headers"] =
                string.IsNullOrWhiteSpace(requestedHeaders)
                    ? string.Join(", ", corsOptions.AllowedHeaders)
                    : requestedHeaders;
            var requestedMethod = context.Request.Headers["Access-Control-Request-Method"].ToString();
            context.Response.Headers["Access-Control-Allow-Methods"] =
                string.IsNullOrWhiteSpace(requestedMethod)
                    ? string.Join(", ", corsOptions.AllowedMethods)
                    : string.Join(", ", corsOptions.AllowedMethods.Union([requestedMethod], StringComparer.OrdinalIgnoreCase));
            context.Response.Headers["Access-Control-Expose-Headers"] = string.Join(", ", corsOptions.ExposedHeaders);
            if (context.Request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }
        }

        await next(context);
    }
}
