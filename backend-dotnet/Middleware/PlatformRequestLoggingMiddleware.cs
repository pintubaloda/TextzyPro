using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Textzy.Api.Data;
using Textzy.Api.Services;

namespace Textzy.Api.Middleware;

public class PlatformRequestLoggingMiddleware(RequestDelegate next)
{
    private const int MaxBodyChars = 8000;
    private const int SlowRequestWarnMs = 1200;
    private static int _writeCounter;
    private static readonly HashSet<string> SkipPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/",
        "/favicon.ico"
    };

    public async Task Invoke(HttpContext context, SensitiveDataRedactor redactor)
    {
        if (!ShouldLog(context))
        {
            await next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var originalResponseBody = context.Response.Body;
        await using var responseBuffer = new MemoryStream();
        context.Response.Body = responseBuffer;

        var requestBody = await ReadRequestBodyAsync(context, redactor);
        string responseBody = string.Empty;
        string error = string.Empty;

        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            error = redactor.RedactText(ex.Message);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            // Always copy the response back, even if logging fails.
            // We've seen production IIS setups return 200 with an empty body when this middleware throws
            // before copying the buffer. Copying must be best-effort and independent of persistence.
            try
            {
                responseBuffer.Position = 0;
                using var reader = new StreamReader(responseBuffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                var rawBody = await reader.ReadToEndAsync();
                responseBody = NormalizeBody(rawBody, redactor);
            }
            catch
            {
                responseBody = string.Empty;
            }
            finally
            {
                try
                {
                    responseBuffer.Position = 0;
                    await responseBuffer.CopyToAsync(originalResponseBody);
                    await originalResponseBody.FlushAsync();
                }
                catch
                {
                    // Never break request lifecycle due to logging/copy failures.
                }
                finally
                {
                    context.Response.Body = originalResponseBody;
                }
            }

            try
            {
                await PersistLogAsync(context, stopwatch.ElapsedMilliseconds, requestBody, responseBody, error);
            }
            catch
            {
                // Never break request lifecycle due to logging failures.
            }
        }
    }

    private static bool ShouldLog(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (SkipPaths.Contains(path)) return false;
        if (path.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase)) return false;
        if (path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase)) return false;
        if (path.StartsWith("/api/platform/kyc/reports", StringComparison.OrdinalIgnoreCase)) return false;
        return path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadRequestBodyAsync(HttpContext context, SensitiveDataRedactor redactor)
    {
        if (context.Request.Body == Stream.Null) return string.Empty;
        if (context.Request.ContentLength is > 1024 * 1024) return "[skipped: request body too large]";
        if (!string.IsNullOrWhiteSpace(context.Request.ContentType) &&
            context.Request.ContentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return "[skipped: multipart/form-data]";
        }

        context.Request.EnableBuffering();
        context.Request.Body.Position = 0;
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;
        return NormalizeBody(body, redactor);
    }

    // NOTE: Response copy + logging are intentionally decoupled in Invoke().

    private async Task PersistLogAsync(HttpContext context, long durationMs, string requestBody, string responseBody, string error)
    {
        var db = context.RequestServices.GetService<ControlDbContext>();
        if (db is null) return;

        var tenancy = context.RequestServices.GetService<TenancyContext>();
        var auth = context.RequestServices.GetService<AuthContext>();

        var path = context.Request.Path.Value ?? string.Empty;
        var query = context.Request.QueryString.Value ?? string.Empty;
        var method = context.Request.Method ?? string.Empty;
        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var userAgent = context.Request.Headers.UserAgent.ToString();
        var traceId = Activity.Current?.TraceId.ToString() ?? string.Empty;
        var spanId = Activity.Current?.SpanId.ToString() ?? string.Empty;

        db.PlatformRequestLogs.Add(new Models.PlatformRequestLog
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow,
            RequestId = context.TraceIdentifier,
            TraceId = traceId,
            SpanId = spanId,
            Method = method,
            Path = path,
            QueryString = query,
            StatusCode = context.Response.StatusCode,
            DurationMs = (int)Math.Clamp(durationMs, 0, int.MaxValue),
            TenantId = tenancy?.TenantId,
            UserId = auth?.UserId,
            ClientIp = Truncate(clientIp, 128),
            UserAgent = Truncate(userAgent, 512),
            RequestBody = Truncate(requestBody, MaxBodyChars),
            ResponseBody = Truncate(responseBody, MaxBodyChars),
            Error = Truncate(error, 1024),
        });

        await db.SaveChangesAsync();

        // Emit minimal ops-friendly app logs for SLO debugging (prod logs are Warning+).
        // Keep it bounded to slow/error paths only to avoid turning prod logs noisy.
        if (context.Response.StatusCode >= 500 || durationMs >= SlowRequestWarnMs)
        {
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RequestOps");
            logger.LogWarning(
                "request: method={Method} path={Path} status={StatusCode} durationMs={DurationMs} tenantId={TenantId} userId={UserId} traceId={TraceId} clientIp={ClientIp}",
                method,
                path,
                context.Response.StatusCode,
                durationMs,
                tenancy?.TenantId,
                auth?.UserId,
                traceId,
                clientIp);
        }

        // Keep log table bounded for platform safety; run cleanup periodically.
        if (Interlocked.Increment(ref _writeCounter) % 200 == 0)
        {
            var count = await db.PlatformRequestLogs.CountAsync();
            if (count > 50000)
            {
                var removeCount = count - 40000;
                var oldRows = await db.PlatformRequestLogs
                    .OrderBy(x => x.CreatedAtUtc)
                    .Take(removeCount)
                    .ToListAsync();
                if (oldRows.Count > 0)
                {
                    db.PlatformRequestLogs.RemoveRange(oldRows);
                    await db.SaveChangesAsync();
                }
            }
        }
    }

    private static string NormalizeBody(string body, SensitiveDataRedactor redactor)
    {
        return redactor.NormalizeAndRedactBody(body, MaxBodyChars);
    }

    private static string Truncate(string text, int max) =>
        string.IsNullOrEmpty(text) ? string.Empty : (text.Length <= max ? text : text[..max]);
}
