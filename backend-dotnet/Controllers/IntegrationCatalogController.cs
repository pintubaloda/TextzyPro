using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
public class IntegrationCatalogController(
    ControlDbContext db,
    AuthContext auth,
    RbacService rbac,
    SecretCryptoService crypto,
    TenancyContext tenancy) : ControllerBase
{
    private static readonly TimeSpan StepUpFreshWindow = TimeSpan.FromMinutes(10);
    private const string Scope = "integration-catalog";
    private const string Key = "items";

    [HttpGet("api/integrations/catalog")]
    public async Task<IActionResult> TenantCatalog(CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!tenancy.IsSet) return Unauthorized();
        var items = await ReadCatalogAsync(ct);
        var entitlementTokens = await ResolveTenantEntitlementTokensAsync(ct);
        var rows = items
            .Where(x => x.IsVisible)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x =>
            {
                var globallyActive = x.IsActive;
                var entitled = globallyActive && IsEntitled(x, entitlementTokens);
                var isPaid = string.Equals(x.PricingType, "paid", StringComparison.OrdinalIgnoreCase);
                var activationStatus = !globallyActive
                    ? "unavailable"
                    : entitled
                        ? "active"
                        : isPaid
                            ? "not_purchased"
                            : "available";

                return new
                {
                    x.Slug,
                    x.Name,
                    x.Category,
                    x.Description,
                    x.PricingType,
                    x.BillingFrequency,
                    x.Price,
                    x.Currency,
                    x.TaxMode,
                    isActive = entitled,
                    isVisible = x.IsVisible,
                    x.SortOrder,
                    isPaid,
                    isGloballyActive = globallyActive,
                    isEntitled = entitled,
                    activationStatus,
                    canRequestPurchase = globallyActive && isPaid && !entitled
                };
            })
            .ToList();

        return Ok(rows);
    }

    [HttpGet("api/platform/integrations/catalog")]
    public async Task<IActionResult> PlatformCatalog(CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();
        return Ok(await ReadCatalogAsync(ct));
    }

    [HttpPut("api/platform/integrations/catalog")]
    public async Task<IActionResult> SavePlatformCatalog([FromBody] List<IntegrationCatalogItem>? request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsWrite)) return Forbid();
        if (!HasFreshStepUp())
        {
            return StatusCode(StatusCodes.Status428PreconditionRequired, new
            {
                stepUpRequired = true,
                action = "platform_settings_write",
                title = "Verify before saving",
                message = "Enter your authenticator code to update integration controls."
            });
        }

        var items = (request ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.Slug) && !string.IsNullOrWhiteSpace(x.Name))
            .Select((x, index) => Normalize(x, index + 1))
            .ToList();

        var json = JsonSerializer.Serialize(items);
        var row = await db.PlatformSettings.FirstOrDefaultAsync(x => x.Scope == Scope && x.Key == Key, ct);
        if (row is null)
        {
            row = new PlatformSetting
            {
                Id = Guid.NewGuid(),
                Scope = Scope,
                Key = Key,
                UpdatedByUserId = auth.UserId
            };
            db.PlatformSettings.Add(row);
        }

        row.ValueEncrypted = crypto.Encrypt(json);
        row.UpdatedByUserId = auth.UserId;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(items);
    }

    private bool HasFreshStepUp()
        => auth.StepUpVerifiedAtUtc.HasValue && auth.StepUpVerifiedAtUtc.Value >= DateTime.UtcNow.Subtract(StepUpFreshWindow);

    private async Task<List<IntegrationCatalogItem>> ReadCatalogAsync(CancellationToken ct)
    {
        var row = await db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Scope == Scope && x.Key == Key, ct);
        if (row is null) return DefaultCatalog();
        try
        {
            var json = crypto.Decrypt(row.ValueEncrypted);
            var items = JsonSerializer.Deserialize<List<IntegrationCatalogItem>>(json) ?? [];
            return items.Select((x, index) => Normalize(x, index + 1)).ToList();
        }
        catch
        {
            return DefaultCatalog();
        }
    }

    private static IntegrationCatalogItem Normalize(IntegrationCatalogItem item, int sortOrder) => new()
    {
        Slug = (item.Slug ?? string.Empty).Trim().ToLowerInvariant(),
        Name = (item.Name ?? string.Empty).Trim(),
        Category = string.IsNullOrWhiteSpace(item.Category) ? "general" : item.Category.Trim().ToLowerInvariant(),
        Description = (item.Description ?? string.Empty).Trim(),
        PricingType = NormalizePricingType(item.PricingType),
        BillingFrequency = NormalizeBillingFrequency(item.BillingFrequency),
        Price = Math.Max(0m, item.Price),
        Currency = string.IsNullOrWhiteSpace(item.Currency) ? "INR" : item.Currency.Trim().ToUpperInvariant(),
        TaxMode = string.Equals(item.TaxMode, "inclusive", StringComparison.OrdinalIgnoreCase) ? "inclusive" : "exclusive",
        IsActive = item.IsActive,
        IsVisible = item.IsVisible,
        SortOrder = item.SortOrder > 0 ? item.SortOrder : sortOrder
    };

    private static string NormalizePricingType(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized == "paid" ? "paid" : "free";
    }

    private static string NormalizeBillingFrequency(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "monthly" => "monthly",
            "one_time" => "one_time",
            "onetime" => "one_time",
            _ => "monthly"
        };
    }

    private static List<IntegrationCatalogItem> DefaultCatalog() =>
    [
        new() { Slug = "shopify", Name = "Shopify", Category = "e-commerce", Description = "Sync orders and automate status updates.", PricingType = "paid", BillingFrequency = "monthly", Price = 999m, Currency = "INR", TaxMode = "exclusive", IsActive = true, IsVisible = true, SortOrder = 1 },
        new() { Slug = "woocommerce", Name = "WooCommerce", Category = "e-commerce", Description = "WordPress store messaging and order sync.", PricingType = "paid", BillingFrequency = "monthly", Price = 799m, Currency = "INR", TaxMode = "exclusive", IsActive = true, IsVisible = true, SortOrder = 2 },
        new() { Slug = "razorpay", Name = "Razorpay", Category = "payments", Description = "Payment collection events and invoice updates.", PricingType = "free", BillingFrequency = "monthly", Price = 0m, Currency = "INR", TaxMode = "exclusive", IsActive = true, IsVisible = true, SortOrder = 3 },
        new() { Slug = "zapier", Name = "Zapier", Category = "automation", Description = "Bridge Textzy with external tools.", PricingType = "paid", BillingFrequency = "monthly", Price = 1499m, Currency = "INR", TaxMode = "exclusive", IsActive = true, IsVisible = true, SortOrder = 4 },
        new() { Slug = "google-authenticator", Name = "Google Authenticator", Category = "security", Description = "QR-based TOTP for secure account sign-in.", PricingType = "free", BillingFrequency = "monthly", Price = 0m, Currency = "INR", TaxMode = "exclusive", IsActive = true, IsVisible = true, SortOrder = 5 },
        new() { Slug = "microsoft-authenticator", Name = "Microsoft Authenticator", Category = "security", Description = "QR-based TOTP enrollment for Microsoft Authenticator.", PricingType = "free", BillingFrequency = "monthly", Price = 0m, Currency = "INR", TaxMode = "exclusive", IsActive = true, IsVisible = true, SortOrder = 6 }
    ];

    private async Task<HashSet<string>> ResolveTenantEntitlementTokensAsync(CancellationToken ct)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var activeStatuses = new[] { "active", "trial", "trialing" };
        var subscription = await db.TenantSubscriptions
            .AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId && activeStatuses.Contains(x.Status))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (subscription is null) return tokens;

        var plan = await db.BillingPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == subscription.PlanId, ct);

        foreach (var token in ExpandEntitlementTokens(subscription.BillingCycle))
            tokens.Add(token);

        if (plan is not null)
        {
            foreach (var token in ExpandEntitlementTokens(plan.Code))
                tokens.Add(token);
            foreach (var token in ExpandEntitlementTokens(plan.Name))
                tokens.Add(token);
            foreach (var feature in ParseStringList(plan.FeaturesJson))
            {
                foreach (var token in ExpandEntitlementTokens(feature))
                    tokens.Add(token);
            }
        }

        var featureFlags = await db.TenantFeatureFlags.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId && x.IsEnabled)
            .Select(x => x.FeatureKey)
            .ToListAsync(ct);
        foreach (var featureKey in featureFlags)
        {
            foreach (var token in ExpandEntitlementTokens(featureKey))
                tokens.Add(token);
        }

        return tokens;
    }

    private static bool IsEntitled(IntegrationCatalogItem item, HashSet<string> entitlementTokens)
    {
        if (entitlementTokens.Count == 0) return false;

        var itemTokens = ExpandEntitlementTokens(item.Slug)
            .Concat(ExpandEntitlementTokens(item.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (itemTokens.Any(entitlementTokens.Contains)) return true;

        return itemTokens.Any(token =>
            entitlementTokens.Contains($"integration:{token}") ||
            entitlementTokens.Contains($"plugin:{token}") ||
            entitlementTokens.Contains($"addon:{token}"));
    }

    private static IEnumerable<string> ExpandEntitlementTokens(string? raw)
    {
        var original = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(original))
            yield return original;

        var normalized = NormalizeToken(raw);
        if (string.IsNullOrWhiteSpace(normalized)) yield break;

        yield return normalized;

        if (normalized.Contains('-'))
            yield return normalized.Replace("-", string.Empty, StringComparison.Ordinal);
        if (normalized.Contains('_'))
            yield return normalized.Replace("_", string.Empty, StringComparison.Ordinal);

        if (normalized is "google-authenticator" or "googleauthenticator")
            yield return "google_authenticator";
        else if (normalized is "google_authenticator")
        {
            yield return "google-authenticator";
            yield return "googleauthenticator";
        }

        if (normalized is "microsoft-authenticator" or "microsoftauthenticator")
            yield return "microsoft_authenticator";
        else if (normalized is "microsoft_authenticator")
        {
            yield return "microsoft-authenticator";
            yield return "microsoftauthenticator";
        }

    }

    private static string NormalizeToken(string? raw)
    {
        var value = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        value = value.Replace("&", " and ", StringComparison.Ordinal);
        value = Regex.Replace(value, @"[^a-z0-9]+", "-").Trim('-');
        return value;
    }

    private static List<string> ParseStringList(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; } catch { return []; }
    }

    public sealed class IntegrationCatalogItem
    {
        public string Slug { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = "general";
        public string Description { get; set; } = string.Empty;
        public string PricingType { get; set; } = "free";
        public string BillingFrequency { get; set; } = "monthly";
        public decimal Price { get; set; }
        public string Currency { get; set; } = "INR";
        public string TaxMode { get; set; } = "exclusive";
        public bool IsActive { get; set; } = true;
        public bool IsVisible { get; set; } = true;
        public int SortOrder { get; set; } = 1;
    }
}
