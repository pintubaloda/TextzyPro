namespace Textzy.Api.Services;

public static class SupportTicketCatalog
{
    private static readonly (string Key, string Name)[] DefaultServices =
    [
        ("billing", "Billing and invoices"),
        ("whatsapp-onboarding", "WhatsApp onboarding"),
        ("sms-gateway", "SMS gateway"),
        ("api-integration", "API integration"),
        ("google-authenticator", "Google Authenticator"),
        ("campaigns", "Campaigns"),
        ("templates", "Templates"),
        ("automations", "Automations"),
        ("mobile-devices", "Mobile devices"),
        ("general-support", "General support")
    ];

    public static IReadOnlyList<ServiceOption> Build(string planCode, string planName)
    {
        var items = new List<ServiceOption>();
        if (!string.IsNullOrWhiteSpace(planName))
        {
            var key = string.IsNullOrWhiteSpace(planCode) ? "active-plan" : $"plan-{planCode.Trim().ToLowerInvariant()}";
            items.Add(new ServiceOption(key, $"{planName.Trim()} plan purchase"));
        }

        items.AddRange(DefaultServices.Select(x => new ServiceOption(x.Key, x.Name)));
        return items
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    public static string NormalizeStatus(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "open" => "open",
            "waiting_on_customer" => "waiting_on_customer",
            "closed" => "closed",
            _ => string.Empty
        };
    }

    public static string NormalizePriority(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "low" => "low",
            "normal" => "normal",
            "high" => "high",
            "urgent" => "urgent",
            _ => "normal"
        };
    }

    public static string NormalizeActorType(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "platform" => "platform",
            _ => "customer"
        };
    }

    public static string FormatTicketNo()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        return $"TXT-SUP-{DateTime.UtcNow:yyyyMMdd}-{suffix}";
    }

    public static string BuildPreview(string body)
    {
        var text = (body ?? string.Empty).Trim().Replace("\r", " ").Replace("\n", " ");
        if (text.Length <= 160) return text;
        return $"{text[..157]}...";
    }

    public sealed record ServiceOption(string Key, string Name);
}
