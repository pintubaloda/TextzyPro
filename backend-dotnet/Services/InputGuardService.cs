using System.Text.RegularExpressions;

namespace Textzy.Api.Services;

public static class InputGuardService
{
    private static readonly Regex PhoneRegex = new(@"^\+?[0-9]{8,15}$", RegexOptions.Compiled);
    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    public static string RequireTrimmed(string? value, string field, int maxLen = 256)
    {
        var text = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException($"{field} is required.");
        if (text.Length > maxLen)
            throw new InvalidOperationException($"{field} is too long.");
        return text;
    }

    public static string ValidatePhone(string? value, string field = "Phone")
    {
        var text = RequireTrimmed(value, field, 32);
        if (!PhoneRegex.IsMatch(text))
            throw new InvalidOperationException($"{field} format is invalid.");
        return text;
    }

    public static string ValidateEmailOrEmpty(string? value, string field = "Email")
    {
        var text = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        if (text.Length > 320 || !EmailRegex.IsMatch(text))
            throw new InvalidOperationException($"{field} format is invalid.");
        return text;
    }
}
