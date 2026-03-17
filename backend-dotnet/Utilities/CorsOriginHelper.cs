namespace Textzy.Api.Utilities;

public sealed class FrontendCorsOptions
{
    public required string[] AllowedOrigins { get; init; }
    public string[] AllowedHeaders { get; init; } = ["Authorization", "X-Access-Token", "X-CSRF-Token", "X-Tenant-Slug", "Content-Type"];
    public string[] AllowedMethods { get; init; } = ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"];
    public string[] ExposedHeaders { get; init; } = ["Authorization", "X-Access-Token", "X-CSRF-Token", "X-Textzy-Build", "X-Textzy-Body-Len"];
}

public static class CorsOriginHelper
{
    public static FrontendCorsOptions BuildFrontendCorsOptions(IConfiguration config, bool isProduction)
    {
        var allowedOrigins = ParseAllowedOrigins(config, isProduction).ToArray();

        if (isProduction)
        {
            var defaults = new[]
            {
                "https://textzy.in",
                "https://www.textzy.in"
            };
            var set = new HashSet<string>(allowedOrigins, StringComparer.OrdinalIgnoreCase);
            foreach (var origin in defaults)
            {
                set.Add(origin);
            }
            allowedOrigins = set.ToArray();
        }

        return new FrontendCorsOptions
        {
            AllowedOrigins = allowedOrigins
        };
    }

    public static IEnumerable<string> ParseAllowedOrigins(IConfiguration config, bool isProduction)
    {
        var defaults = new[]
        {
            "http://textzy.in",
            "https://textzy.in"
        };

        var raw = config["AllowedOrigins"] ?? string.Empty;
        var parsed = raw
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeCorsOrigin)
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var origin in defaults)
        {
            if (!parsed.Contains(origin, StringComparer.OrdinalIgnoreCase))
            {
                parsed.Add(origin);
            }
        }

        if (parsed.Count == 0 && !isProduction)
        {
            parsed.Add("http://localhost:3000");
            parsed.Add("http://localhost:5173");
        }

        return parsed;
    }

    public static bool IsAllowedOrigin(FrontendCorsOptions options, string? origin)
    {
        var normalized = NormalizeCorsOrigin(origin);
        return !string.IsNullOrWhiteSpace(normalized) &&
               options.AllowedOrigins.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    public static string NormalizeCorsOrigin(string? raw)
    {
        var value = (raw ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Scheme) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        }

        return value;
    }
}
