using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace Textzy.Api.Services;

public class SensitiveDataRedactor
{
    private static readonly Regex BearerRegex = new(@"Bearer\s+[A-Za-z0-9\-\._~\+\/=]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TokenRegex = new(@"(?<k>token|secret|password|authorization)\s*[:=]\s*(?<v>[^\s,;]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string RedactText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var redacted = BearerRegex.Replace(text, "Bearer ***");
        redacted = TokenRegex.Replace(redacted, "${k}=***");
        return redacted;
    }

    public string NormalizeAndRedactBody(string body, int maxChars = 8000)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        var trimmed = body.Trim();
        if (trimmed.Length > maxChars) trimmed = trimmed[..maxChars];
        try
        {
            var node = JsonNode.Parse(trimmed);
            if (node is null) return RedactText(trimmed);
            RedactJson(node);
            return node.ToJsonString();
        }
        catch
        {
            return RedactText(trimmed);
        }
    }

    public void RedactJson(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            var keys = obj.Select(kv => kv.Key).ToList();
            foreach (var key in keys)
            {
                if (IsSensitiveKey(key))
                {
                    obj[key] = "***";
                    continue;
                }

                if (obj[key] is JsonNode child)
                    RedactJson(child);
            }

            return;
        }

        if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is JsonNode child)
                    RedactJson(child);
            }
        }
    }

    private static bool IsSensitiveKey(string key) =>
        key.Equals("password", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("passwordHash", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("passwordSalt", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("token", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("accessToken", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("refreshToken", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("authorization", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("appSecret", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("keySecret", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("webhookSecret", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("systemUserAccessToken", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("valueEncrypted", StringComparison.OrdinalIgnoreCase);
}
