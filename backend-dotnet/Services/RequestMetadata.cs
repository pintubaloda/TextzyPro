using Microsoft.AspNetCore.Http;

namespace Textzy.Api.Services;

public static class RequestMetadata
{
    public static string GetClientIp(HttpContext? context)
    {
        if (context is null) return string.Empty;

        var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded.Split(',')[0].Trim();
            if (!string.IsNullOrWhiteSpace(first)) return first;
        }

        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(realIp)) return realIp.Trim();

        var cloudflareIp = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(cloudflareIp)) return cloudflareIp.Trim();

        return context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
    }

    public static string GetUserAgent(HttpContext? context)
        => context?.Request.Headers.UserAgent.ToString() ?? string.Empty;

    public static string GetDeviceLabel(HttpContext? context)
    {
        var ua = GetUserAgent(context);
        if (string.IsNullOrWhiteSpace(ua)) return "Unknown device";

        var lower = ua.ToLowerInvariant();
        var platform = lower.Contains("android") ? "Android"
            : lower.Contains("iphone") || lower.Contains("ipad") || lower.Contains("ios") ? "iOS"
            : lower.Contains("windows") ? "Windows"
            : lower.Contains("mac os") || lower.Contains("macintosh") ? "macOS"
            : lower.Contains("linux") ? "Linux"
            : "Unknown OS";
        var browser = lower.Contains("edg/") ? "Edge"
            : lower.Contains("chrome/") ? "Chrome"
            : lower.Contains("firefox/") ? "Firefox"
            : lower.Contains("safari/") && !lower.Contains("chrome/") ? "Safari"
            : lower.Contains("okhttp") ? "Mobile App"
            : lower.Contains("electron") ? "Desktop App"
            : "Unknown client";

        return $"{platform} | {browser}";
    }
}
