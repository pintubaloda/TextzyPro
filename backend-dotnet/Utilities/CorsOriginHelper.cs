namespace Textzy.Api.Utilities;

public static class CorsOriginHelper
{
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
