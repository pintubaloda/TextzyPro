using System.Text.RegularExpressions;
using Npgsql;

namespace Textzy.Api.Utilities;

public static class ConnectionStringHelper
{
    public static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    public static string NormalizeConnectionString(string raw)
    {
        var value = (raw ?? string.Empty).Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Database connection string is empty.");

        // Defensive cleanup in case env was pasted as a labeled line.
        if (value.StartsWith("External Database URL", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("Internal Database URL", StringComparison.OrdinalIgnoreCase))
        {
            var idx = value.IndexOf("://", StringComparison.Ordinal);
            if (idx > 0)
            {
                var schemeStart = value.LastIndexOf(' ', idx);
                value = value[(schemeStart >= 0 ? schemeStart + 1 : 0)..].Trim();
            }
        }

        // Remove hidden whitespace/newlines that can appear in env UI paste for URLs.
        var noWhitespace = Regex.Replace(value, @"\s+", string.Empty).Trim();

        // If the value has extra text, extract the first postgres URL segment.
        var urlMatch = Regex.Match(noWhitespace, @"(postgres(?:ql)?://\S+)", RegexOptions.IgnoreCase);
        if (urlMatch.Success)
        {
            value = urlMatch.Groups[1].Value.Trim().Trim('"', '\'');
        }
        else
        {
            value = value.Trim();
        }

        if (value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            if (TryBuildFromUrl(value, out var urlConn))
            {
                return urlConn;
            }
            try
            {
                return new NpgsqlConnectionStringBuilder(value).ConnectionString;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Invalid Postgres URL in ConnectionStrings__Default/DATABASE_URL.", ex);
            }
        }

        try
        {
            return new NpgsqlConnectionStringBuilder(value).ConnectionString;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Invalid key/value Postgres connection string in ConnectionStrings__Default/DATABASE_URL.", ex);
        }
    }

    public static string? BuildFromPgEnvironment()
    {
        var host = FirstNonEmpty(
            Environment.GetEnvironmentVariable("PGHOST"),
            Environment.GetEnvironmentVariable("POSTGRES_HOST"),
            Environment.GetEnvironmentVariable("DB_HOST"));
        var port = FirstNonEmpty(
            Environment.GetEnvironmentVariable("PGPORT"),
            Environment.GetEnvironmentVariable("POSTGRES_PORT"),
            Environment.GetEnvironmentVariable("DB_PORT"));
        var user = FirstNonEmpty(
            Environment.GetEnvironmentVariable("PGUSER"),
            Environment.GetEnvironmentVariable("POSTGRES_USER"),
            Environment.GetEnvironmentVariable("DB_USER"));
        var pass = FirstNonEmpty(
            Environment.GetEnvironmentVariable("PGPASSWORD"),
            Environment.GetEnvironmentVariable("POSTGRES_PASSWORD"),
            Environment.GetEnvironmentVariable("DB_PASSWORD"));
        var db = FirstNonEmpty(
            Environment.GetEnvironmentVariable("PGDATABASE"),
            Environment.GetEnvironmentVariable("POSTGRES_DB"),
            Environment.GetEnvironmentVariable("DB_NAME"));
        var sslMode = FirstNonEmpty(
            Environment.GetEnvironmentVariable("PGSSLMODE"),
            Environment.GetEnvironmentVariable("POSTGRES_SSLMODE"),
            Environment.GetEnvironmentVariable("DB_SSLMODE"));

        if (string.IsNullOrWhiteSpace(host) ||
            string.IsNullOrWhiteSpace(port) ||
            string.IsNullOrWhiteSpace(user) ||
            string.IsNullOrWhiteSpace(pass) ||
            string.IsNullOrWhiteSpace(db))
        {
            return null;
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host.Trim(),
            Port = int.TryParse(port, out var p) ? p : 5432,
            Username = user.Trim(),
            Password = pass,
            Database = db.Trim()
        };

        var ssl = (sslMode ?? string.Empty).Trim().ToLowerInvariant();
        if (ssl is "require" or "prefer" or "verify-ca" or "verify-full")
        {
            builder.SslMode = ssl switch
            {
                "require" => SslMode.Require,
                "prefer" => SslMode.Prefer,
                "verify-ca" => SslMode.VerifyCA,
                "verify-full" => SslMode.VerifyFull,
                _ => SslMode.Prefer
            };
        }
        else
        {
            builder.SslMode = SslMode.Require;
        }

        return builder.ConnectionString;
    }

    public static bool TryBuildFromUrl(string url, out string connectionString)
    {
        connectionString = string.Empty;
        var cleaned = (url ?? string.Empty).Trim().Trim('"', '\'');
        if (!Uri.TryCreate(cleaned, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!uri.Scheme.Equals("postgres", StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals("postgresql", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var userInfo = uri.UserInfo.Split(':', 2);
        var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty;
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
        var database = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        var sslMode = query.TryGetValue("sslmode", out var ssl) ? ssl.ToString() : "require";

        var csb = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Username = username,
            Password = password,
            Database = database
        };

        csb.SslMode = sslMode.ToLowerInvariant() switch
        {
            "disable" => SslMode.Disable,
            "allow" => SslMode.Allow,
            "prefer" => SslMode.Prefer,
            "verify-ca" => SslMode.VerifyCA,
            "verify-full" => SslMode.VerifyFull,
            _ => SslMode.Require
        };
        connectionString = csb.ConnectionString;
        return true;
    }
}
