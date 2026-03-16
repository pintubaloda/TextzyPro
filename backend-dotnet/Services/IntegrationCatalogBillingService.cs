using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;

namespace Textzy.Api.Services;

public sealed record IntegrationBillingConfig(
    string MetricKey,
    int CreditsPerSuccess,
    IReadOnlyDictionary<string, int> OperationCredits)
{
    public int ResolveCredits(string? operationCode, int fallbackCredits)
    {
        var op = (operationCode ?? string.Empty).Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(op) && OperationCredits.TryGetValue(op, out var credits) && credits > 0)
            return credits;
        return fallbackCredits;
    }
}

/// <summary>
/// Reads per-plugin billing configuration from the platform Integration Catalog.
/// This lets the platform change credits-per-success at runtime without redeploying.
/// </summary>
public sealed class IntegrationCatalogBillingService(
    ControlDbContext db,
    SecretCryptoService crypto)
{
    private const string CatalogScope = "integration-catalog";
    private const string CatalogKey = "items";
    private const string DigilockerSettingsScope = "digilocker";

    public async Task<IntegrationBillingConfig> ResolveAsync(string pluginSlug, CancellationToken ct = default)
    {
        var slug = (pluginSlug ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(slug))
            return new IntegrationBillingConfig(MetricKey: string.Empty, CreditsPerSuccess: 0, OperationCredits: new Dictionary<string, int>());

        var config = await TryReadFromCatalogAsync(slug, ct);
        if (config is not null)
            return Normalize(config.MetricKey, config.CreditsPerSuccess, config.OperationCredits);

        // Safe defaults.
        if (string.Equals(slug, "digilocker-kyc", StringComparison.OrdinalIgnoreCase))
        {
            // Preserve legacy behavior: allow global default from digilocker scope if catalog hasn't been extended yet.
            var legacyCredits = await TryReadLegacyDigilockerCreditsPerSuccessAsync(ct) ?? 3;
            return Normalize("digilockerKyc", legacyCredits, new Dictionary<string, int>());
        }

        return new IntegrationBillingConfig(MetricKey: string.Empty, CreditsPerSuccess: 0, OperationCredits: new Dictionary<string, int>());
    }

    private async Task<IntegrationBillingConfig?> TryReadFromCatalogAsync(string slug, CancellationToken ct)
    {
        var encrypted = await db.PlatformSettings
            .AsNoTracking()
            .Where(x => x.Scope == CatalogScope && x.Key == CatalogKey)
            .Select(x => x.ValueEncrypted)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(encrypted)) return null;

        string json;
        try { json = crypto.Decrypt(encrypted); }
        catch { return null; }

        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                if (!el.TryGetProperty("slug", out var slugEl)) continue;
                var itemSlug = (slugEl.GetString() ?? string.Empty).Trim().ToLowerInvariant();
                if (!string.Equals(itemSlug, slug, StringComparison.OrdinalIgnoreCase)) continue;

                var metricKey = string.Empty;
                if (el.TryGetProperty("billingMetric", out var metricEl) && metricEl.ValueKind == JsonValueKind.String)
                    metricKey = (metricEl.GetString() ?? string.Empty).Trim();

                var credits = 0;
                if (el.TryGetProperty("creditsPerSuccess", out var creditsEl))
                {
                    if (creditsEl.ValueKind == JsonValueKind.Number) creditsEl.TryGetInt32(out credits);
                    else if (creditsEl.ValueKind == JsonValueKind.String) int.TryParse(creditsEl.GetString(), out credits);
                }

                var opCredits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                if (el.TryGetProperty("operationCredits", out var opEl) && opEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in opEl.EnumerateObject())
                    {
                        var key = (prop.Name ?? string.Empty).Trim().ToUpperInvariant();
                        if (string.IsNullOrWhiteSpace(key) || key.Length > 64) continue;
                        var value = 0;
                        if (prop.Value.ValueKind == JsonValueKind.Number) prop.Value.TryGetInt32(out value);
                        else if (prop.Value.ValueKind == JsonValueKind.String) int.TryParse(prop.Value.GetString(), out value);
                        value = Math.Clamp(value, 0, 50);
                        if (value > 0) opCredits[key] = value;
                    }
                }

                // Back-compat: if catalog item exists but credits aren't set, fallback to legacy setting for DigiLocker.
                if (string.Equals(itemSlug, "digilocker-kyc", StringComparison.OrdinalIgnoreCase) && credits <= 0)
                {
                    credits = await TryReadLegacyDigilockerCreditsPerSuccessAsync(ct) ?? 3;
                }

                return new IntegrationBillingConfig(metricKey, credits, opCredits);
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private async Task<int?> TryReadLegacyDigilockerCreditsPerSuccessAsync(CancellationToken ct)
    {
        try
        {
            var raw = await db.PlatformSettings
                .AsNoTracking()
                .Where(x => x.Scope == DigilockerSettingsScope && x.Key == "creditsPerSuccess")
                .Select(x => x.ValueEncrypted)
                .FirstOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var value = crypto.Decrypt(raw).Trim();
            if (!int.TryParse(value, out var credits)) return null;
            return credits;
        }
        catch
        {
            return null;
        }
    }

    private static IntegrationBillingConfig Normalize(string metricKey, int creditsPerSuccess, IReadOnlyDictionary<string, int> operationCredits)
    {
        var metric = (metricKey ?? string.Empty).Trim();
        var credits = creditsPerSuccess;
        if (credits < 0) credits = 0;
        if (credits > 50) credits = 50;

        var normalizedOps = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (operationCredits is not null)
        {
            foreach (var (keyRaw, valueRaw) in operationCredits)
            {
                var key = (keyRaw ?? string.Empty).Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(key) || key.Length > 64) continue;
                var value = Math.Clamp(valueRaw, 0, 50);
                if (value > 0) normalizedOps[key] = value;
            }
        }

        return new IntegrationBillingConfig(metric, credits, normalizedOps);
    }
}
