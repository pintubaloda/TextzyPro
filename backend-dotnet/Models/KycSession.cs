using System.Text.Json;

namespace Textzy.Api.Models;

public class KycSession
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string ProviderCode { get; set; } = "digilocker";
    public string Status { get; set; } = "created"; // created|redirected|verified|failed|expired
    public string CustomerRef { get; set; } = string.Empty;
    public string GstNumber { get; set; } = string.Empty;
    public string RequestedDocTypesJson { get; set; } = "[]";
    public string SuccessRedirectUrl { get; set; } = string.Empty;
    public string FailureRedirectUrl { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;

    // OAuth/PKCE state for the provider flow (encrypted-at-rest).
    public string StateEncrypted { get; set; } = string.Empty;
    public string CodeVerifierEncrypted { get; set; } = string.Empty;

    // Provider result (encrypted-at-rest): raw response + normalized snapshot.
    public string ResultJsonEncrypted { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
    public string BillingMetric { get; set; } = string.Empty;
    public int CreditsUsed { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }

    public static string NormalizeDocTypes(IEnumerable<string>? docTypes)
    {
        var list = (docTypes ?? Array.Empty<string>())
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.ToLowerInvariant())
            .Distinct()
            .Take(50)
            .ToList();
        return JsonSerializer.Serialize(list);
    }
}
