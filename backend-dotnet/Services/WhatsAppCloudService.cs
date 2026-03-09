using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.DTOs;
using Textzy.Api.Models;

namespace Textzy.Api.Services;

public class WhatsAppCloudService(
    TenantDbContext tenantDb,
    ControlDbContext controlDb,
    TenancyContext tenancy,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    SecretCryptoService crypto,
    WabaTenantResolver tenantResolver,
    SensitiveDataRedactor redactor,
    ILogger<WhatsAppCloudService> logger)
{
    private static readonly ConcurrentDictionary<Guid, TenantMetaFlowLimiter> MetaFlowLimiters = new();
    private readonly WhatsAppOptions _options = configuration.GetSection("WhatsApp").Get<WhatsAppOptions>() ?? new WhatsAppOptions();

    private sealed class DiscoveredWabaAsset
    {
        public string BusinessId { get; init; } = string.Empty;
        public string WabaId { get; init; } = string.Empty;
        public string WabaName { get; init; } = string.Empty;
        public string PhoneNumberId { get; init; } = string.Empty;
        public string DisplayPhoneNumber { get; init; } = string.Empty;
    }

    private sealed class SystemUserLifecycleResult
    {
        public string BusinessId { get; init; } = string.Empty;
        public string SystemUserId { get; init; } = string.Empty;
        public string SystemUserName { get; init; } = string.Empty;
        public DateTime? SystemUserCreatedAtUtc { get; init; }
        public DateTime? AssetsAssignedAtUtc { get; init; }
        public string AccessToken { get; init; } = string.Empty;
        public DateTime? TokenExpiresAtUtc { get; init; }
        public string TokenSource { get; init; } = "embedded_exchange";
        public List<string> Warnings { get; init; } = [];
    }

    private sealed class OnboardingAudit
    {
        public bool WebhookSubscribed { get; init; }
        public bool PermissionAuditPassed { get; init; }
        public string BusinessVerificationStatus { get; init; } = string.Empty;
        public string PhoneQualityRating { get; init; } = string.Empty;
        public string PhoneNameStatus { get; init; } = string.Empty;
        public string MessagingLimitTier { get; init; } = string.Empty;
        public string AccountHealth { get; init; } = string.Empty;
        public List<string> Warnings { get; init; } = [];
    }

    public async Task<object> DebugProbeAsync(string accessToken, CancellationToken ct = default)
    {
        var steps = new List<object>();

        var meUrl = $"{_options.GraphApiBase}/{_options.ApiVersion}/me?fields=id,name,whatsapp_business_accounts{{id,name,phone_numbers{{id,display_phone_number,verified_name}}}}";
        var me = await GraphGetRawAsync(meUrl, accessToken, ct);
        steps.Add(new { step = "me_with_waba", me.StatusCode, me.Ok, me.Body });

        var businessesUrl = $"{_options.GraphApiBase}/{_options.ApiVersion}/me/businesses?fields=id,name";
        var businesses = await GraphGetRawAsync(businessesUrl, accessToken, ct);
        steps.Add(new { step = "me_businesses", businesses.StatusCode, businesses.Ok, businesses.Body });

        return new { steps };
    }

    public async Task<object> DebugTenantProbeAsync(CancellationToken ct = default)
    {
        var cfg = await GetTenantConfigAsync(ct);
        if (cfg is null || string.IsNullOrWhiteSpace(cfg.AccessToken))
        {
            return new { connected = false, reason = "No active tenant WABA config/token found." };
        }

        var probe = await DebugProbeAsync(UnprotectToken(cfg.AccessToken), ct);
        return new
        {
            connected = cfg.IsActive,
            cfg.WabaId,
            cfg.PhoneNumberId,
            cfg.BusinessAccountName,
            cfg.DisplayPhoneNumber,
            graph = probe
        };
    }

    public async Task<TenantWabaConfig?> GetTenantConfigAsync(CancellationToken ct = default)
        => await GetTenantConfigRowAsync(onlyActive: true, ct);

    public bool VerifyWebhookSignature(string body, string signatureHeader)
        => VerifyWebhookSignatureWithSecret(body, signatureHeader, _options.AppSecret);

    public async Task<bool> VerifyWebhookSignatureAsync(string body, string signatureHeader, CancellationToken ct = default)
    {
        var platformSecret = string.Empty;
        try
        {
            var row = await controlDb.PlatformSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Scope == "waba-master" && x.Key == "appSecret", ct);
            if (row is not null && !string.IsNullOrWhiteSpace(row.ValueEncrypted))
                platformSecret = crypto.Decrypt(row.ValueEncrypted);
        }
        catch
        {
            // Keep validation resilient and fallback to configured secret.
        }

        var activeSecret = string.IsNullOrWhiteSpace(platformSecret) ? _options.AppSecret : platformSecret;
        return VerifyWebhookSignatureWithSecret(body, signatureHeader, activeSecret);
    }

    private static bool VerifyWebhookSignatureWithSecret(string body, string signatureHeader, string appSecret)
    {
        if (string.IsNullOrWhiteSpace(appSecret) || string.IsNullOrWhiteSpace(signatureHeader)) return false;
        const string prefix = "sha256=";
        if (!signatureHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var expected = signatureHeader[prefix.Length..].Trim();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var computed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(computed));
    }

    public async Task HandleWebhookAsync(string jsonBody, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(jsonBody);
        if (!doc.RootElement.TryGetProperty("entry", out var entries)) return;

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes)) continue;
            foreach (var change in changes.EnumerateArray())
            {
                if (!change.TryGetProperty("value", out var value)) continue;
                if (!value.TryGetProperty("messages", out var messages)) continue;

                foreach (var message in messages.EnumerateArray())
                {
                    if (!message.TryGetProperty("from", out var fromProp)) continue;
                    var from = fromProp.GetString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(from)) continue;

                    var window = await tenantDb.Set<ConversationWindow>()
                        .FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.Recipient == from, ct);

                    if (window is null)
                    {
                        window = new ConversationWindow
                        {
                            Id = Guid.NewGuid(),
                            TenantId = tenancy.TenantId,
                            Recipient = from,
                            LastInboundAtUtc = DateTime.UtcNow,
                            UpdatedAtUtc = DateTime.UtcNow
                        };
                        tenantDb.Set<ConversationWindow>().Add(window);
                    }
                    else
                    {
                        window.LastInboundAtUtc = DateTime.UtcNow;
                        window.UpdatedAtUtc = DateTime.UtcNow;
                    }
                }
            }
        }

        await tenantDb.SaveChangesAsync(ct);
    }

    public async Task<TenantWabaConfig> StartOnboardingAsync(CancellationToken ct = default)
    {
        var config = await GetOrCreateTenantConfigAsync(ct);

        // Safety: do not downgrade an already connected mapping if user clicks
        // "Start onboarding" again from UI.
        var alreadyConnected = config.IsActive
            && !string.IsNullOrWhiteSpace(config.WabaId)
            && !string.IsNullOrWhiteSpace(config.PhoneNumberId);
        if (alreadyConnected)
        {
            return config;
        }

        config.OnboardingState = "requested";
        config.OnboardingStartedAtUtc = DateTime.UtcNow;
        config.LastError = string.Empty;
        await tenantDb.SaveChangesAsync(ct);
        return config;
    }

    public async Task<TenantWabaConfig> DisconnectTenantWabaAsync(string reason, CancellationToken ct = default)
    {
        var config = await GetTenantConfigRowAsync(onlyActive: false, ct)
            ?? throw new InvalidOperationException("WABA config not found for this project.");

        config.IsActive = false;
        config.OnboardingState = "disabled";
        config.PermissionAuditPassed = false;
        config.WebhookSubscribedAtUtc = null;
        config.WebhookVerifiedAtUtc = null;
        config.LastError = string.IsNullOrWhiteSpace(reason)
            ? "Disconnected by tenant admin."
            : reason.Trim();

        await tenantDb.SaveChangesAsync(ct);
        if (!string.IsNullOrWhiteSpace(config.PhoneNumberId))
            await tenantResolver.InvalidateAsync(config.PhoneNumberId, ct);

        return config;
    }

    public async Task<TenantWabaConfig> ExchangeEmbeddedCodeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("Code is required.");
        if (code.StartsWith("EA", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Strict embedded signup requires authorization code exchange. Direct access token payloads are not allowed.");

        var config = await GetOrCreateTenantConfigAsync(ct);

        config.OnboardingState = "code_received";
        config.CodeReceivedAtUtc = DateTime.UtcNow;
        config.LastError = string.Empty;
        await tenantDb.SaveChangesAsync(ct);

        var accessToken = await ResolveAccessTokenAsync(code, ct);
        var discovered = await DiscoverWabaAssetsAsync(accessToken, ct)
            ?? throw new InvalidOperationException("Code exchange succeeded but WABA/phone discovery failed. Verify app scopes and embedded signup completion.");
        var conflict = await FindTenantBindingConflictAsync(discovered.WabaId, discovered.PhoneNumberId, ct);
        if (conflict is not null)
        {
            throw new InvalidOperationException($"This WhatsApp account/number is already linked to another project ({conflict.Value.TenantName} / {conflict.Value.TenantSlug}). Use a different number or disconnect there first.");
        }

        var lifecycle = await ProvisionSystemUserAndPermanentTokenAsync(accessToken, discovered, ct);
        if (lifecycle.Warnings.Count > 0)
        {
            config.LastError = string.Join(" | ", lifecycle.Warnings);
        }

        var effectiveToken = string.IsNullOrWhiteSpace(lifecycle.AccessToken) ? accessToken : lifecycle.AccessToken;
        config.AccessToken = ProtectToken(effectiveToken);
        config.TokenSource = string.IsNullOrWhiteSpace(lifecycle.AccessToken) ? "embedded_exchange" : lifecycle.TokenSource;
        config.BusinessManagerId = lifecycle.BusinessId;
        config.SystemUserId = lifecycle.SystemUserId;
        config.SystemUserName = lifecycle.SystemUserName;
        config.SystemUserCreatedAtUtc = lifecycle.SystemUserCreatedAtUtc;
        config.AssetsAssignedAtUtc = lifecycle.AssetsAssignedAtUtc;
        config.PermanentTokenIssuedAtUtc = string.IsNullOrWhiteSpace(lifecycle.AccessToken) ? null : DateTime.UtcNow;
        config.PermanentTokenExpiresAtUtc = lifecycle.TokenExpiresAtUtc;
        config.IsActive = true;
        config.BusinessAccountName = discovered.WabaName;
        config.WabaId = discovered.WabaId;
        config.PhoneNumberId = discovered.PhoneNumberId;
        config.DisplayPhoneNumber = discovered.DisplayPhoneNumber;
        config.ConnectedAtUtc = DateTime.UtcNow;
        config.ExchangedAtUtc = DateTime.UtcNow;
        config.AssetsLinkedAtUtc = DateTime.UtcNow;
        config.OnboardingState = "assets_linked";

        var webhookOk = await EnsureWebhookSubscriptionAsync(config, effectiveToken, ct);
        config.WebhookSubscribedAtUtc = webhookOk ? DateTime.UtcNow : null;
        config.OnboardingState = webhookOk ? "webhook_subscribed" : "assets_linked";

        var audit = await RunPostOnboardingChecksAsync(config, effectiveToken, ct);
        config.PermissionAuditPassed = audit.PermissionAuditPassed;
        config.BusinessVerificationStatus = audit.BusinessVerificationStatus;
        config.PhoneQualityRating = audit.PhoneQualityRating;
        config.PhoneNameStatus = audit.PhoneNameStatus;
        config.MessagingLimitTier = audit.MessagingLimitTier;
        config.AccountHealth = audit.AccountHealth;
        if (audit.Warnings.Count > 0) config.LastError = string.Join(" | ", audit.Warnings);
        config.OnboardingState = webhookOk && audit.PermissionAuditPassed ? "ready" : config.OnboardingState;
        await tenantResolver.InvalidateAsync(config.PhoneNumberId, ct);

        await tenantDb.SaveChangesAsync(ct);
        return config;
    }

    public async Task<object> ReuseExistingFromCodeAsync(string code, string selectedWabaId, string selectedPhoneNumberId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("Code is required.");
        var accessToken = await ResolveAccessTokenAsync(code, ct);
        var all = await DiscoverAllWabaAssetsAsync(accessToken, ct);
        if (all.Count == 0)
            throw new InvalidOperationException("No existing WABA assets found for this account.");

        if (string.IsNullOrWhiteSpace(selectedWabaId) && string.IsNullOrWhiteSpace(selectedPhoneNumberId))
        {
            return new
            {
                requiresSelection = true,
                count = all.Count,
                assets = all.Select(x => new
                {
                    businessId = x.BusinessId,
                    wabaId = x.WabaId,
                    wabaName = x.WabaName,
                    phoneNumberId = x.PhoneNumberId,
                    displayPhoneNumber = x.DisplayPhoneNumber
                }).ToList()
            };
        }

        var selected = all.FirstOrDefault(x =>
            (string.IsNullOrWhiteSpace(selectedWabaId) || string.Equals(x.WabaId, selectedWabaId, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(selectedPhoneNumberId) || string.Equals(x.PhoneNumberId, selectedPhoneNumberId, StringComparison.OrdinalIgnoreCase)));

        if (selected is null)
            throw new InvalidOperationException("Selected WABA/Phone not found in discovered assets.");

        var conflict = await FindTenantBindingConflictAsync(selected.WabaId, selected.PhoneNumberId, ct);
        if (conflict is not null)
        {
            throw new InvalidOperationException($"This WhatsApp account/number is already linked to another project ({conflict.Value.TenantName} / {conflict.Value.TenantSlug}).");
        }

        var config = await GetOrCreateTenantConfigAsync(ct);
        config.OnboardingState = "code_received";
        config.CodeReceivedAtUtc = DateTime.UtcNow;
        config.LastError = string.Empty;

        var lifecycle = await ProvisionSystemUserAndPermanentTokenAsync(accessToken, selected, ct);
        if (lifecycle.Warnings.Count > 0)
        {
            config.LastError = string.Join(" | ", lifecycle.Warnings);
        }

        var effectiveToken = string.IsNullOrWhiteSpace(lifecycle.AccessToken) ? accessToken : lifecycle.AccessToken;
        config.AccessToken = ProtectToken(effectiveToken);
        config.TokenSource = string.IsNullOrWhiteSpace(lifecycle.AccessToken) ? "embedded_exchange" : lifecycle.TokenSource;
        config.BusinessManagerId = lifecycle.BusinessId;
        config.SystemUserId = lifecycle.SystemUserId;
        config.SystemUserName = lifecycle.SystemUserName;
        config.SystemUserCreatedAtUtc = lifecycle.SystemUserCreatedAtUtc;
        config.AssetsAssignedAtUtc = lifecycle.AssetsAssignedAtUtc;
        config.PermanentTokenIssuedAtUtc = string.IsNullOrWhiteSpace(lifecycle.AccessToken) ? null : DateTime.UtcNow;
        config.PermanentTokenExpiresAtUtc = lifecycle.TokenExpiresAtUtc;
        config.IsActive = true;
        config.BusinessAccountName = selected.WabaName;
        config.WabaId = selected.WabaId;
        config.PhoneNumberId = selected.PhoneNumberId;
        config.DisplayPhoneNumber = selected.DisplayPhoneNumber;
        config.ConnectedAtUtc = DateTime.UtcNow;
        config.ExchangedAtUtc = DateTime.UtcNow;
        config.AssetsLinkedAtUtc = DateTime.UtcNow;
        config.OnboardingState = "assets_linked";

        var webhookOk = await EnsureWebhookSubscriptionAsync(config, effectiveToken, ct);
        config.WebhookSubscribedAtUtc = webhookOk ? DateTime.UtcNow : null;
        config.OnboardingState = webhookOk ? "webhook_subscribed" : "assets_linked";

        var audit = await RunPostOnboardingChecksAsync(config, effectiveToken, ct);
        config.PermissionAuditPassed = audit.PermissionAuditPassed;
        config.BusinessVerificationStatus = audit.BusinessVerificationStatus;
        config.PhoneQualityRating = audit.PhoneQualityRating;
        config.PhoneNameStatus = audit.PhoneNameStatus;
        config.MessagingLimitTier = audit.MessagingLimitTier;
        config.AccountHealth = audit.AccountHealth;
        if (audit.Warnings.Count > 0) config.LastError = string.Join(" | ", audit.Warnings);
        config.OnboardingState = webhookOk && audit.PermissionAuditPassed ? "ready" : config.OnboardingState;
        await tenantResolver.InvalidateAsync(config.PhoneNumberId, ct);

        await tenantDb.SaveChangesAsync(ct);

        return new
        {
            requiresSelection = false,
            linked = true,
            wabaId = config.WabaId,
            phoneNumberId = config.PhoneNumberId,
            businessName = config.BusinessAccountName,
            phone = config.DisplayPhoneNumber,
            state = config.OnboardingState
        };
    }

    public async Task<object> MapExistingWabaAsync(Guid targetTenantId, string wabaId, string phoneNumberId, string accessToken, CancellationToken ct = default)
    {
        var normalizedWabaId = (wabaId ?? string.Empty).Trim();
        var normalizedPhoneId = (phoneNumberId ?? string.Empty).Trim();
        var normalizedToken = (accessToken ?? string.Empty).Trim();

        if (targetTenantId == Guid.Empty) throw new InvalidOperationException("Project is required.");
        if (string.IsNullOrWhiteSpace(normalizedWabaId)) throw new InvalidOperationException("WABA ID is required.");
        if (string.IsNullOrWhiteSpace(normalizedPhoneId)) throw new InvalidOperationException("Phone Number ID is required.");
        if (string.IsNullOrWhiteSpace(normalizedToken)) throw new InvalidOperationException("Access token is required.");

        var targetTenant = await controlDb.Tenants.FirstOrDefaultAsync(x => x.Id == targetTenantId, ct)
            ?? throw new InvalidOperationException("Project not found.");

        var wabaUrl = $"{_options.GraphApiBase}/{_options.ApiVersion}/{normalizedWabaId}?fields=id,name,business_verification_status,account_review_status";
        var phoneUrl = $"{_options.GraphApiBase}/{_options.ApiVersion}/{normalizedPhoneId}?fields=id,display_phone_number,verified_name,quality_rating,name_status,status";
        var phonesForWabaUrl = $"{_options.GraphApiBase}/{_options.ApiVersion}/{normalizedWabaId}/phone_numbers?fields=id,display_phone_number,verified_name";

        var wabaRaw = await GraphGetRawAsync(wabaUrl, normalizedToken, ct);
        if (!wabaRaw.Ok)
            throw new InvalidOperationException($"Unable to validate WABA ID. Graph status={wabaRaw.StatusCode} payload={wabaRaw.Body}");
        var phoneRaw = await GraphGetRawAsync(phoneUrl, normalizedToken, ct);
        if (!phoneRaw.Ok)
            throw new InvalidOperationException($"Unable to validate Phone Number ID. Graph status={phoneRaw.StatusCode} payload={phoneRaw.Body}");
        var wabaPhonesRaw = await GraphGetRawAsync(phonesForWabaUrl, normalizedToken, ct);
        if (!wabaPhonesRaw.Ok)
            throw new InvalidOperationException($"Unable to validate WABA phone list. Graph status={wabaPhonesRaw.StatusCode} payload={wabaPhonesRaw.Body}");

        string wabaName = string.Empty;
        string businessVerificationStatus = string.Empty;
        using (var wabaDoc = JsonDocument.Parse(wabaRaw.Body))
        {
            wabaName = TryGetString(wabaDoc.RootElement, "name");
            businessVerificationStatus = TryGetString(wabaDoc.RootElement, "business_verification_status");
        }

        string displayPhoneNumber = string.Empty;
        string verifiedName = string.Empty;
        string phoneQualityRating = string.Empty;
        string phoneNameStatus = string.Empty;
        using (var phoneDoc = JsonDocument.Parse(phoneRaw.Body))
        {
            displayPhoneNumber = TryGetString(phoneDoc.RootElement, "display_phone_number");
            verifiedName = TryGetString(phoneDoc.RootElement, "verified_name");
            phoneQualityRating = TryGetString(phoneDoc.RootElement, "quality_rating");
            phoneNameStatus = TryGetString(phoneDoc.RootElement, "name_status");
        }

        var phoneBelongsToWaba = false;
        using (var wabaPhonesDoc = JsonDocument.Parse(wabaPhonesRaw.Body))
        {
            if (wabaPhonesDoc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in data.EnumerateArray())
                {
                    var id = TryGetString(row, "id");
                    if (!string.Equals(id, normalizedPhoneId, StringComparison.OrdinalIgnoreCase)) continue;
                    phoneBelongsToWaba = true;
                    if (string.IsNullOrWhiteSpace(displayPhoneNumber))
                        displayPhoneNumber = TryGetString(row, "display_phone_number");
                    if (string.IsNullOrWhiteSpace(verifiedName))
                        verifiedName = TryGetString(row, "verified_name");
                    break;
                }
            }
        }

        if (!phoneBelongsToWaba)
            throw new InvalidOperationException("Phone Number ID does not belong to the provided WABA ID.");

        var conflict = await FindTenantBindingConflictAsync(normalizedWabaId, normalizedPhoneId, ct, targetTenantId);
        if (conflict is not null)
        {
            throw new InvalidOperationException($"This WhatsApp account/number is already linked to another project ({conflict.Value.TenantName} / {conflict.Value.TenantSlug}).");
        }

        using var targetTenantDb = SeedData.CreateTenantDbContext(targetTenant.DataConnectionString);
        var config = await targetTenantDb.Set<TenantWabaConfig>()
            .Where(x => x.TenantId == targetTenantId)
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.ConnectedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (config is null)
        {
            config = new TenantWabaConfig
            {
                Id = Guid.NewGuid(),
                TenantId = targetTenantId
            };
            targetTenantDb.Set<TenantWabaConfig>().Add(config);
        }

        config.AccessToken = ProtectToken(normalizedToken);
        config.TokenSource = "manual_map";
        config.IsActive = true;
        config.BusinessAccountName = string.IsNullOrWhiteSpace(wabaName) ? (verifiedName ?? string.Empty) : wabaName;
        config.WabaId = normalizedWabaId;
        config.PhoneNumberId = normalizedPhoneId;
        config.DisplayPhoneNumber = displayPhoneNumber;
        config.BusinessVerificationStatus = businessVerificationStatus;
        config.PhoneQualityRating = phoneQualityRating;
        config.PhoneNameStatus = phoneNameStatus;
        config.OnboardingState = "assets_linked";
        config.OnboardingStartedAtUtc ??= DateTime.UtcNow;
        config.CodeReceivedAtUtc ??= DateTime.UtcNow;
        config.ExchangedAtUtc = DateTime.UtcNow;
        config.AssetsLinkedAtUtc = DateTime.UtcNow;
        config.ConnectedAtUtc = DateTime.UtcNow;
        config.LastError = string.Empty;
        config.LastGraphError = string.Empty;

        var webhookOk = await EnsureWebhookSubscriptionAsync(config, normalizedToken, ct);
        config.WebhookSubscribedAtUtc = webhookOk ? DateTime.UtcNow : null;
        config.OnboardingState = webhookOk ? "webhook_subscribed" : "assets_linked";

        var audit = await RunPostOnboardingChecksAsync(config, normalizedToken, ct);
        config.PermissionAuditPassed = audit.PermissionAuditPassed;
        if (!string.IsNullOrWhiteSpace(audit.BusinessVerificationStatus))
            config.BusinessVerificationStatus = audit.BusinessVerificationStatus;
        if (!string.IsNullOrWhiteSpace(audit.PhoneQualityRating))
            config.PhoneQualityRating = audit.PhoneQualityRating;
        if (!string.IsNullOrWhiteSpace(audit.PhoneNameStatus))
            config.PhoneNameStatus = audit.PhoneNameStatus;
        if (!string.IsNullOrWhiteSpace(audit.MessagingLimitTier))
            config.MessagingLimitTier = audit.MessagingLimitTier;
        if (!string.IsNullOrWhiteSpace(audit.AccountHealth))
            config.AccountHealth = audit.AccountHealth;
        if (audit.Warnings.Count > 0)
            config.LastError = string.Join(" | ", audit.Warnings);
        if (webhookOk && audit.PermissionAuditPassed)
            config.OnboardingState = "ready";

        await targetTenantDb.SaveChangesAsync(ct);
        await tenantResolver.InvalidateAsync(normalizedPhoneId, ct);

        return new
        {
            linked = true,
            tenantId = targetTenant.Id,
            tenantName = targetTenant.Name,
            tenantSlug = targetTenant.Slug,
            wabaId = config.WabaId,
            phoneNumberId = config.PhoneNumberId,
            businessName = config.BusinessAccountName,
            phone = config.DisplayPhoneNumber,
            state = config.OnboardingState,
            readyToSend = config.OnboardingState == "ready"
        };
    }

    private async Task BootstrapMessageTemplatesAsync(TenantWabaConfig config, string accessToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.WabaId) || string.IsNullOrWhiteSpace(accessToken)) return;
        try
        {
            var after = string.Empty;
            var pages = 0;
            while (pages < 10)
            {
                pages++;
                var url = $"{_options.GraphApiBase}/{_options.ApiVersion}/{config.WabaId}/message_templates?fields=name,status,category,language,rejected_reason,components";
                if (!string.IsNullOrWhiteSpace(after))
                    url += $"&after={Uri.EscapeDataString(after)}";

                var (ok, status, body) = await GraphGetRawAsync(url, accessToken, ct);
                if (!ok)
                {
                    logger.LogWarning("Template bootstrap failed: tenant={TenantId} waba={WabaId} status={Status} body={Body}", tenancy.TenantId, config.WabaId, status, redactor.RedactText(body));
                    return;
                }

                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in data.EnumerateArray())
                    {
                        var name = TryGetString(t, "name");
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        var language = TryGetString(t, "language");
                        if (string.IsNullOrWhiteSpace(language)) language = "en";
                        var category = TryGetString(t, "category");
                        if (string.IsNullOrWhiteSpace(category)) category = "UTILITY";
                        var statusText = TryGetString(t, "status");
                        if (string.IsNullOrWhiteSpace(statusText)) statusText = "UNKNOWN";
                        var rejectionReason = TryGetString(t, "rejected_reason");
                        var lifecycleStatus = MapLifecycleStatus(statusText);

                        string bodyText = string.Empty;
                        string headerType = "none";
                        string headerText = string.Empty;
                        string footerText = string.Empty;
                        string buttonsJson = "[]";

                        if (t.TryGetProperty("components", out var comps) && comps.ValueKind == JsonValueKind.Array)
                        {
                            var buttons = new List<object>();
                            foreach (var c in comps.EnumerateArray())
                            {
                                var type = TryGetString(c, "type").ToUpperInvariant();
                                if (type == "BODY")
                                {
                                    bodyText = TryGetString(c, "text");
                                }
                                else if (type == "HEADER")
                                {
                                    var fmt = TryGetString(c, "format").ToLowerInvariant();
                                    headerType = string.IsNullOrWhiteSpace(fmt) ? "text" : fmt;
                                    headerText = TryGetString(c, "text");
                                }
                                else if (type == "FOOTER")
                                {
                                    footerText = TryGetString(c, "text");
                                }
                                else if (type == "BUTTONS" && c.TryGetProperty("buttons", out var btns) && btns.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var b in btns.EnumerateArray())
                                    {
                                        buttons.Add(new
                                        {
                                            type = TryGetString(b, "type"),
                                            text = TryGetString(b, "text"),
                                            url = TryGetString(b, "url"),
                                            phone_number = TryGetString(b, "phone_number")
                                        });
                                    }
                                }
                            }
                            if (buttons.Count > 0) buttonsJson = JsonSerializer.Serialize(buttons);
                        }

                        if (string.IsNullOrWhiteSpace(bodyText))
                            bodyText = $"Template: {name}";

                        var existing = await tenantDb.Templates
                            .FirstOrDefaultAsync(x =>
                                x.TenantId == tenancy.TenantId &&
                                x.Channel == ChannelType.WhatsApp &&
                                x.Name == name &&
                                x.Language == language, ct);

                        if (existing is null)
                        {
                            tenantDb.Templates.Add(new Template
                            {
                                Id = Guid.NewGuid(),
                                TenantId = tenancy.TenantId,
                                Name = name,
                                Channel = ChannelType.WhatsApp,
                                Category = category,
                                Language = language,
                                Body = bodyText,
                                LifecycleStatus = lifecycleStatus,
                                Version = 1,
                                VariantGroup = $"{name}:{language}",
                                Status = statusText,
                                HeaderType = headerType,
                                HeaderText = headerText,
                                HeaderMediaId = string.Empty,
                                HeaderMediaName = string.Empty,
                                FooterText = footerText,
                                ButtonsJson = buttonsJson,
                                RejectionReason = rejectionReason,
                                CreatedAtUtc = DateTime.UtcNow
                            });
                        }
                        else
                        {
                            existing.Category = category;
                            existing.Body = bodyText;
                            existing.Status = statusText;
                            existing.HeaderType = headerType;
                            existing.HeaderText = headerText;
                            existing.HeaderMediaId = string.Empty;
                            existing.HeaderMediaName = string.Empty;
                            existing.FooterText = footerText;
                            existing.ButtonsJson = buttonsJson;
                            existing.LifecycleStatus = lifecycleStatus;
                            existing.RejectionReason = rejectionReason;
                        }
                    }
                }

                await tenantDb.SaveChangesAsync(ct);

                after = string.Empty;
                if (doc.RootElement.TryGetProperty("paging", out var paging) &&
                    paging.TryGetProperty("cursors", out var cursors) &&
                    cursors.TryGetProperty("after", out var afterNode))
                {
                    after = afterNode.GetString() ?? string.Empty;
                }
                if (string.IsNullOrWhiteSpace(after)) break;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Template bootstrap exception for tenant={TenantId} waba={WabaId}: {Error}", tenancy.TenantId, config.WabaId, redactor.RedactText(ex.Message));
        }
    }

    public async Task<object> SyncMessageTemplatesAsync(bool deepSync = false, CancellationToken ct = default)
    {
        var cfg = await GetTenantConfigAsync(ct) ?? throw new InvalidOperationException("WABA config not connected.");
        if (string.IsNullOrWhiteSpace(cfg.WabaId)) throw new InvalidOperationException("WABA ID missing.");
        var token = UnprotectToken(cfg.AccessToken);
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("WABA access token missing.");

        var attempts = deepSync ? 3 : 1;
        var performedAttempts = 0;
        for (var i = 0; i < attempts; i++)
        {
            performedAttempts++;
            await BootstrapMessageTemplatesAsync(cfg, token, ct);
            var pendingNow = await tenantDb.Templates.CountAsync(x =>
                x.TenantId == tenancy.TenantId &&
                x.Channel == ChannelType.WhatsApp &&
                ((x.Status ?? string.Empty).ToLower() == "pending" || (x.Status ?? string.Empty).ToLower() == "in_review"), ct);
            if (!deepSync || pendingNow == 0 || i == attempts - 1) break;
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        var total = await tenantDb.Templates.CountAsync(x => x.TenantId == tenancy.TenantId && x.Channel == ChannelType.WhatsApp, ct);
        var approved = await tenantDb.Templates.CountAsync(x =>
            x.TenantId == tenancy.TenantId &&
            x.Channel == ChannelType.WhatsApp &&
            (x.Status ?? string.Empty).ToLower() == "approved", ct);
        var pending = await tenantDb.Templates.CountAsync(x =>
            x.TenantId == tenancy.TenantId &&
            x.Channel == ChannelType.WhatsApp &&
            ((x.Status ?? string.Empty).ToLower() == "pending" || (x.Status ?? string.Empty).ToLower() == "in_review"), ct);
        var disabled = await tenantDb.Templates.CountAsync(x =>
            x.TenantId == tenancy.TenantId &&
            x.Channel == ChannelType.WhatsApp &&
            ((x.Status ?? string.Empty).ToLower() == "disabled" || (x.Status ?? string.Empty).ToLower() == "paused"), ct);

        cfg.TemplatesSyncedAtUtc = DateTime.UtcNow;
        cfg.TemplatesSyncStatus = "ok";
        cfg.TemplatesSyncFailCount = 0;
        await tenantDb.SaveChangesAsync(ct);

        return new
        {
            synced = true,
            wabaId = cfg.WabaId,
            attempts = performedAttempts,
            total,
            approved,
            pending,
            disabled
        };
    }

    public async Task<int> PurgeTenantWhatsAppTemplatesAsync(CancellationToken ct = default)
    {
        var rows = await tenantDb.Templates
            .Where(x => x.TenantId == tenancy.TenantId && x.Channel == ChannelType.WhatsApp)
            .ToListAsync(ct);
        if (rows.Count == 0) return 0;
        tenantDb.Templates.RemoveRange(rows);
        await tenantDb.SaveChangesAsync(ct);
        return rows.Count;
    }

    public async Task<object> SubmitTemplateForApprovalAsync(Guid templateId, CancellationToken ct = default)
    {
        var cfg = await GetTenantConfigAsync(ct) ?? throw new InvalidOperationException("WABA config not connected.");
        if (string.IsNullOrWhiteSpace(cfg.WabaId)) throw new InvalidOperationException("WABA ID missing.");
        var token = UnprotectToken(cfg.AccessToken);
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("WABA access token missing.");

        var template = await tenantDb.Templates.FirstOrDefaultAsync(x => x.Id == templateId && x.TenantId == tenancy.TenantId, ct);
        if (template is null) throw new InvalidOperationException("Template not found.");
        if (template.Channel != ChannelType.WhatsApp) throw new InvalidOperationException("Only WhatsApp templates can be submitted to Meta.");

        var components = BuildGraphTemplateComponents(template);
        var payload = JsonSerializer.Serialize(new
        {
            name = template.Name,
            category = (template.Category ?? "UTILITY").ToUpperInvariant(),
            language = (template.Language ?? "en").ToLowerInvariant(),
            components
        });

        var url = $"{_options.GraphApiBase}/{_options.ApiVersion}/{cfg.WabaId}/message_templates";
        var post = await GraphPostRawAsync(url, token, payload, ct);
        if (!post.Ok)
        {
            template.LifecycleStatus = "rejected";
            template.Status = "Rejected";
            template.RejectionReason = ExtractGraphErrorReason(post.Body);
            await tenantDb.SaveChangesAsync(ct);
            throw new InvalidOperationException($"Graph template submit failed ({post.StatusCode}): {post.Body}");
        }

        var graphStatus = "PENDING";
        string graphTemplateId = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(post.Body);
            graphStatus = TryGetString(doc.RootElement, "status");
            if (string.IsNullOrWhiteSpace(graphStatus)) graphStatus = "PENDING";
            graphTemplateId = TryGetString(doc.RootElement, "id");
        }
        catch
        {
            graphStatus = "PENDING";
        }

        template.LifecycleStatus = "submitted";
        template.Status = graphStatus;
        template.RejectionReason = string.Empty;
        await tenantDb.SaveChangesAsync(ct);

        return new
        {
            submitted = true,
            templateId = template.Id,
            graphTemplateId,
            graphStatus,
            responseStatus = post.StatusCode
        };
    }

    public async Task<object> DeleteTemplateFromMetaAndDbAsync(Guid templateId, CancellationToken ct = default)
    {
        var cfg = await GetTenantConfigAsync(ct) ?? throw new InvalidOperationException("WABA config not connected.");
        if (string.IsNullOrWhiteSpace(cfg.WabaId)) throw new InvalidOperationException("WABA ID missing.");
        var token = UnprotectToken(cfg.AccessToken);
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("WABA access token missing.");

        var template = await tenantDb.Templates.FirstOrDefaultAsync(x => x.Id == templateId && x.TenantId == tenancy.TenantId, ct);
        if (template is null) throw new InvalidOperationException("Template not found.");
        if (template.Channel != ChannelType.WhatsApp) throw new InvalidOperationException("Only WhatsApp templates can be deleted from Meta.");

        var name = template.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Template name is missing.");

        var url = $"{_options.GraphApiBase}/{_options.ApiVersion}/{cfg.WabaId}/message_templates?name={Uri.EscapeDataString(name)}";
        var del = await GraphDeleteRawAsync(url, token, ct);
        if (!del.Ok)
        {
            var body = del.Body ?? string.Empty;
            var maybeGone = del.StatusCode == 404 ||
                            body.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
                            body.Contains("not found", StringComparison.OrdinalIgnoreCase);
            if (!maybeGone)
                throw new InvalidOperationException($"Graph template delete failed ({del.StatusCode}): {body}");
        }

        tenantDb.Templates.Remove(template);
        await tenantDb.SaveChangesAsync(ct);

        return new
        {
            deleted = true,
            deletedFromMeta = del.Ok,
            templateId,
            templateName = name
        };
    }

    private static object[] BuildGraphTemplateComponents(Template template)
    {
        var components = new List<object>();
        var headerType = (template.HeaderType ?? "none").Trim().ToLowerInvariant();
        if (headerType == "text" && !string.IsNullOrWhiteSpace(template.HeaderText))
        {
            components.Add(new
            {
                type = "HEADER",
                format = "TEXT",
                text = template.HeaderText.Trim()
            });
        }
        else if (headerType is "image" or "video" or "document")
        {
            var mediaId = (template.HeaderMediaId ?? string.Empty).Trim();
            components.Add(string.IsNullOrWhiteSpace(mediaId)
                ? new
                {
                    type = "HEADER",
                    format = headerType.ToUpperInvariant()
                }
                : new
                {
                    type = "HEADER",
                    format = headerType.ToUpperInvariant(),
                    example = new { header_handle = new[] { mediaId } }
                });
        }

        components.Add(new
        {
            type = "BODY",
            text = template.Body ?? string.Empty
        });

        if (!string.IsNullOrWhiteSpace(template.FooterText))
        {
            components.Add(new
            {
                type = "FOOTER",
                text = template.FooterText.Trim()
            });
        }

        var buttons = ParseGraphButtons(template.ButtonsJson);
        if (buttons.Count > 0)
        {
            components.Add(new
            {
                type = "BUTTONS",
                buttons
            });
        }

        return components.ToArray();
    }

    private static List<object> ParseGraphButtons(string buttonsJson)
    {
        var result = new List<object>();
        if (string.IsNullOrWhiteSpace(buttonsJson)) return result;
        try
        {
            using var doc = JsonDocument.Parse(buttonsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var b in doc.RootElement.EnumerateArray())
            {
                var type = TryGetString(b, "type").Trim().ToLowerInvariant();
                var text = TryGetString(b, "text").Trim();
                if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(text)) continue;
                if (type is "quick_reply" or "quickreply")
                {
                    result.Add(new { type = "QUICK_REPLY", text });
                    continue;
                }
                if (type is "url" or "cta_url")
                {
                    var url = TryGetString(b, "url").Trim();
                    if (!string.IsNullOrWhiteSpace(url))
                        result.Add(new { type = "URL", text, url });
                    continue;
                }
                if (type is "phone" or "phone_number" or "call")
                {
                    var phone = TryGetString(b, "phone_number").Trim();
                    if (!string.IsNullOrWhiteSpace(phone))
                        result.Add(new { type = "PHONE_NUMBER", text, phone_number = phone });
                }
            }
        }
        catch
        {
            return result;
        }

        return result;
    }

    private static string MapLifecycleStatus(string status)
    {
        var s = (status ?? string.Empty).Trim().ToUpperInvariant();
        return s switch
        {
            "APPROVED" => "approved",
            "PENDING" or "IN_REVIEW" => "submitted",
            "REJECTED" => "rejected",
            "DISABLED" or "PAUSED" => "disabled",
            _ => "draft"
        };
    }

    private static string ExtractGraphErrorReason(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                var message = TryGetString(error, "message");
                var title = TryGetString(error, "error_user_title");
                var description = TryGetString(error, "error_user_msg");
                var parts = new[] { title, message, description }
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct()
                    .ToArray();
                return string.Join(" | ", parts);
            }
        }
        catch
        {
            // ignore malformed payload
        }

        return body.Length <= 300 ? body : body[..300];
    }

    public async Task<object> GetOnboardingStatusAsync(CancellationToken ct = default)
    {
        var config = await GetTenantConfigRowAsync(onlyActive: false, ct);
        if (config is null)
        {
            return new
            {
                state = "requested",
                isConnected = false,
                readyToSend = false,
                tenantId = tenancy.TenantId,
                tenantSlug = tenancy.TenantSlug
            };
        }

        if (!string.IsNullOrWhiteSpace(config.AccessToken) && !string.IsNullOrWhiteSpace(config.WabaId))
        {
            var token = UnprotectToken(config.AccessToken);
            var audit = await RunPostOnboardingChecksAsync(config, token, ct);
            config.PermissionAuditPassed = audit.PermissionAuditPassed;
            config.BusinessVerificationStatus = audit.BusinessVerificationStatus;
            config.PhoneQualityRating = audit.PhoneQualityRating;
            config.PhoneNameStatus = audit.PhoneNameStatus;
            config.MessagingLimitTier = audit.MessagingLimitTier;
            config.AccountHealth = audit.AccountHealth;
            if (audit.Warnings.Count > 0)
            {
                config.LastError = string.Join(" | ", audit.Warnings);
            }

            if (audit.WebhookSubscribed && config.WebhookSubscribedAtUtc is null)
                config.WebhookSubscribedAtUtc = DateTime.UtcNow;

            if (config.OnboardingState != "ready" && audit.WebhookSubscribed && audit.PermissionAuditPassed)
                config.OnboardingState = "ready";

            await tenantDb.SaveChangesAsync(ct);
        }

        var ready = config.IsActive
            && !string.IsNullOrWhiteSpace(config.WabaId)
            && !string.IsNullOrWhiteSpace(config.PhoneNumberId)
            && config.PermissionAuditPassed
            && config.WebhookSubscribedAtUtc.HasValue;

        return new
        {
            state = config.OnboardingState,
            isConnected = config.IsActive,
            readyToSend = ready,
            businessName = config.BusinessAccountName,
            phone = config.DisplayPhoneNumber,
            wabaId = config.WabaId,
            phoneNumberId = config.PhoneNumberId,
            businessManagerId = config.BusinessManagerId,
            systemUserId = config.SystemUserId,
            systemUserName = config.SystemUserName,
            tokenSource = config.TokenSource,
            permanentTokenIssuedAtUtc = config.PermanentTokenIssuedAtUtc,
            permanentTokenExpiresAtUtc = config.PermanentTokenExpiresAtUtc,
            businessVerificationStatus = config.BusinessVerificationStatus,
            phoneQualityRating = config.PhoneQualityRating,
            phoneNameStatus = config.PhoneNameStatus,
            messagingLimitTier = config.MessagingLimitTier,
            accountHealth = config.AccountHealth,
            permissionAuditPassed = config.PermissionAuditPassed,
            webhookSubscribed = config.WebhookSubscribedAtUtc.HasValue,
            timeline = new
            {
                requestedAtUtc = config.OnboardingStartedAtUtc,
                codeReceivedAtUtc = config.CodeReceivedAtUtc,
                exchangedAtUtc = config.ExchangedAtUtc,
                assetsLinkedAtUtc = config.AssetsLinkedAtUtc,
                webhookSubscribedAtUtc = config.WebhookSubscribedAtUtc,
                verifiedAtUtc = config.WebhookVerifiedAtUtc
            },
            lastError = config.LastError,
            lastGraphError = config.LastGraphError,
            tenantId = tenancy.TenantId,
            tenantSlug = tenancy.TenantSlug
        };
    }

    private async Task<string> ResolveAccessTokenAsync(string codeOrToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.AppId) || string.IsNullOrWhiteSpace(_options.AppSecret))
            throw new InvalidOperationException("WhatsApp AppId/AppSecret missing.");

        var client = httpClientFactory.CreateClient();
        var tokenUrl = $"{_options.GraphApiBase}/{_options.ApiVersion}/oauth/access_token?client_id={Uri.EscapeDataString(_options.AppId)}&client_secret={Uri.EscapeDataString(_options.AppSecret)}&code={Uri.EscapeDataString(codeOrToken)}";
        var tokenResp = await client.GetAsync(tokenUrl, ct);
        var tokenPayload = await tokenResp.Content.ReadAsStringAsync(ct);
        if (!tokenResp.IsSuccessStatusCode)
        {
            logger.LogWarning("WABA code exchange failed: status={Status} body={Body}", (int)tokenResp.StatusCode, redactor.RedactText(tokenPayload));
            throw new InvalidOperationException($"Failed to exchange embedded signup code. Graph status={(int)tokenResp.StatusCode} payload={tokenPayload}");
        }

        using var tokenDoc = JsonDocument.Parse(tokenPayload);
        var accessToken = tokenDoc.RootElement.TryGetProperty("access_token", out var at) ? at.GetString() ?? string.Empty : string.Empty;
        if (string.IsNullOrWhiteSpace(accessToken)) throw new InvalidOperationException("Missing access token in exchange response.");
        return accessToken;
    }

    public async Task MarkWebhookVerifiedAsync(string phoneNumberId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumberId)) return;
        var cfg = await tenantDb.Set<TenantWabaConfig>()
            .FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.PhoneNumberId == phoneNumberId, ct);
        if (cfg is null) return;
        cfg.WebhookVerifiedAtUtc = DateTime.UtcNow;
        if (cfg.OnboardingState != "ready" && cfg.WebhookSubscribedAtUtc.HasValue && cfg.PermissionAuditPassed)
            cfg.OnboardingState = "ready";
        await tenantDb.SaveChangesAsync(ct);
    }

    private async Task<bool> EnsureWebhookSubscriptionAsync(TenantWabaConfig config, string accessToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.WabaId) || string.IsNullOrWhiteSpace(accessToken)) return false;
        var url = $"{_options.GraphApiBase}/{_options.ApiVersion}/{config.WabaId}/subscribed_apps";
        var post = await GraphPostRawAsync(url, accessToken, null, ct);
        if (!post.Ok)
        {
            config.LastGraphError = post.Body;
            return false;
        }

        return true;
    }

    private async Task<OnboardingAudit> RunPostOnboardingChecksAsync(TenantWabaConfig config, string accessToken, CancellationToken ct)
    {
        var warnings = new List<string>();
        var businessVerificationStatus = string.Empty;
        var phoneQualityRating = string.Empty;
        var phoneNameStatus = string.Empty;
        var messagingLimitTier = string.Empty;
        var accountHealth = string.Empty;
        var permissionAuditPassed = false;
        var webhookSubscribed = false;

        if (string.IsNullOrWhiteSpace(accessToken))
            return new OnboardingAudit { Warnings = ["Missing access token."] };

        if (!string.IsNullOrWhiteSpace(config.WabaId))
        {
            var wabaUrl = $"{_options.GraphApiBase}/{_options.ApiVersion}/{config.WabaId}?fields=id,name,account_review_status,business_verification_status,messaging_limit_tier";
            var waba = await GraphGetRawAsync(wabaUrl, accessToken, ct);
            if (waba.Ok && !string.IsNullOrWhiteSpace(waba.Body))
            {
                try
                {
                    using var wabaDoc = JsonDocument.Parse(waba.Body);
                    businessVerificationStatus = TryGetString(wabaDoc.RootElement, "business_verification_status");
                    messagingLimitTier = TryGetString(wabaDoc.RootElement, "messaging_limit_tier");
                    accountHealth = TryGetString(wabaDoc.RootElement, "account_review_status");
                }
                catch
                {
                    warnings.Add("Unable to parse WABA verification status.");
                }
            }
            else
            {
                warnings.Add("Failed to fetch WABA status.");
                config.LastGraphError = waba.Body;
            }

            var subscribedUrl = $"{_options.GraphApiBase}/{_options.ApiVersion}/{config.WabaId}/subscribed_apps";
            var subscribed = await GraphGetRawAsync(subscribedUrl, accessToken, ct);
            webhookSubscribed = subscribed.Ok;
            if (!subscribed.Ok) warnings.Add("Webhook subscription check failed.");
        }

        if (!string.IsNullOrWhiteSpace(config.PhoneNumberId))
        {
            var phoneUrl = $"{_options.GraphApiBase}/{_options.ApiVersion}/{config.PhoneNumberId}?fields=id,verified_name,quality_rating,name_status";
            var phone = await GraphGetRawAsync(phoneUrl, accessToken, ct);
            if (phone.Ok && !string.IsNullOrWhiteSpace(phone.Body))
            {
                try
                {
                    using var phoneDoc = JsonDocument.Parse(phone.Body);
                    phoneQualityRating = TryGetString(phoneDoc.RootElement, "quality_rating");
                    phoneNameStatus = TryGetString(phoneDoc.RootElement, "name_status");
                }
                catch
                {
                    warnings.Add("Unable to parse phone quality/name status.");
                }
            }
            else
            {
                warnings.Add("Failed to fetch phone quality status.");
            }
        }

        var permsUrl = $"{_options.GraphApiBase}/{_options.ApiVersion}/me/permissions";
        var permsRaw = await GraphGetRawAsync(permsUrl, accessToken, ct);
        if (permsRaw.Ok && !string.IsNullOrWhiteSpace(permsRaw.Body))
        {
            permissionAuditPassed = permsRaw.Body.Contains("whatsapp_business_management", StringComparison.OrdinalIgnoreCase)
                && permsRaw.Body.Contains("whatsapp_business_messaging", StringComparison.OrdinalIgnoreCase)
                && permsRaw.Body.Contains("business_management", StringComparison.OrdinalIgnoreCase);
            if (!permissionAuditPassed) warnings.Add("Required scopes are missing.");
        }
        else
        {
            warnings.Add("Permissions audit failed.");
        }

        return new OnboardingAudit
        {
            WebhookSubscribed = webhookSubscribed,
            PermissionAuditPassed = permissionAuditPassed,
            BusinessVerificationStatus = businessVerificationStatus,
            PhoneQualityRating = phoneQualityRating,
            PhoneNameStatus = phoneNameStatus,
            MessagingLimitTier = messagingLimitTier,
            AccountHealth = accountHealth,
            Warnings = warnings
        };
    }

    private async Task<DiscoveredWabaAsset?> DiscoverWabaAssetsAsync(string accessToken, CancellationToken ct)
    {
        var meUrl = $"{_options.GraphApiBase}/{_options.ApiVersion}/me?fields=id,name,whatsapp_business_accounts{{id,name,phone_numbers{{id,display_phone_number,verified_name}}}}";
        var direct = await GraphGetAsync(meUrl, accessToken, ct);
        var fromDirect = ParseWabaAssetFromMe(direct);
        if (fromDirect is not null)
        {
            if (string.IsNullOrWhiteSpace(fromDirect.BusinessId))
            {
                var businessId = await FindBusinessForWabaAsync(accessToken, fromDirect.WabaId, ct);
                if (!string.IsNullOrWhiteSpace(businessId))
                {
                    fromDirect = new DiscoveredWabaAsset
                    {
                        BusinessId = businessId,
                        WabaId = fromDirect.WabaId,
                        WabaName = fromDirect.WabaName,
                        PhoneNumberId = fromDirect.PhoneNumberId,
                        DisplayPhoneNumber = fromDirect.DisplayPhoneNumber
                    };
                }
            }

            return fromDirect;
        }

        var businessesUrl = $"{_options.GraphApiBase}/{_options.ApiVersion}/me/businesses?fields=id,name";
        var businessesDoc = await GraphGetAsync(businessesUrl, accessToken, ct);
        var businessIds = ReadIdsFromDataArray(businessesDoc);

        foreach (var businessId in businessIds)
        {
            var ownedWabaUrl = $"{_options.GraphApiBase}/{_options.ApiVersion}/{businessId}/owned_whatsapp_business_accounts?fields=id,name";
            var ownedWabas = await GraphGetAsync(ownedWabaUrl, accessToken, ct);
            var wabas = ReadObjectsFromDataArray(ownedWabas);

            foreach (var waba in wabas)
            {
                var wabaId = TryGetString(waba, "id");
                if (string.IsNullOrWhiteSpace(wabaId)) continue;

                var wabaName = TryGetString(waba, "name");
                var phonesUrl = $"{_options.GraphApiBase}/{_options.ApiVersion}/{wabaId}/phone_numbers?fields=id,display_phone_number,verified_name";
                var phonesDoc = await GraphGetAsync(phonesUrl, accessToken, ct);
                var phones = ReadObjectsFromDataArray(phonesDoc);
                var phone = phones.FirstOrDefault();
                if (phone.ValueKind == JsonValueKind.Undefined) continue;

                var phoneId = TryGetString(phone, "id");
                if (string.IsNullOrWhiteSpace(phoneId)) continue;

                var display = TryGetString(phone, "display_phone_number");
                if (string.IsNullOrWhiteSpace(display)) display = TryGetString(phone, "verified_name");

                return new DiscoveredWabaAsset
                {
                    BusinessId = businessId,
                    WabaId = wabaId,
                    WabaName = string.IsNullOrWhiteSpace(wabaName) ? "WhatsApp Business Account" : wabaName,
                    PhoneNumberId = phoneId,
                    DisplayPhoneNumber = string.IsNullOrWhiteSpace(display) ? "Unknown Number" : display
                };
            }
        }

        return null;
    }

    private async Task<List<DiscoveredWabaAsset>> DiscoverAllWabaAssetsAsync(string accessToken, CancellationToken ct)
    {
        var list = new List<DiscoveredWabaAsset>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var meUrl = $"{_options.GraphApiBase}/{_options.ApiVersion}/me?fields=id,name,whatsapp_business_accounts{{id,name,phone_numbers{{id,display_phone_number,verified_name}}}}";
        var direct = await GraphGetAsync(meUrl, accessToken, ct);
        if (direct is not null &&
            direct.RootElement.TryGetProperty("whatsapp_business_accounts", out var wabasNode) &&
            wabasNode.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Array)
        {
            foreach (var waba in data.EnumerateArray())
            {
                var wabaId = TryGetString(waba, "id");
                if (string.IsNullOrWhiteSpace(wabaId)) continue;
                var wabaName = TryGetString(waba, "name");
                if (!waba.TryGetProperty("phone_numbers", out var phonesNode) ||
                    !phonesNode.TryGetProperty("data", out var phonesData) ||
                    phonesData.ValueKind != JsonValueKind.Array) continue;

                var businessId = await FindBusinessForWabaAsync(accessToken, wabaId, ct);
                foreach (var phone in phonesData.EnumerateArray())
                {
                    var phoneId = TryGetString(phone, "id");
                    if (string.IsNullOrWhiteSpace(phoneId)) continue;
                    var key = $"{wabaId}|{phoneId}";
                    if (!seen.Add(key)) continue;
                    var display = TryGetString(phone, "display_phone_number");
                    if (string.IsNullOrWhiteSpace(display)) display = TryGetString(phone, "verified_name");
                    list.Add(new DiscoveredWabaAsset
                    {
                        BusinessId = businessId,
                        WabaId = wabaId,
                        WabaName = string.IsNullOrWhiteSpace(wabaName) ? "WhatsApp Business Account" : wabaName,
                        PhoneNumberId = phoneId,
                        DisplayPhoneNumber = string.IsNullOrWhiteSpace(display) ? "Unknown Number" : display
                    });
                }
            }
        }

        var businessesUrl = $"{_options.GraphApiBase}/{_options.ApiVersion}/me/businesses?fields=id,name";
        var businessesDoc = await GraphGetAsync(businessesUrl, accessToken, ct);
        var businessIds = ReadIdsFromDataArray(businessesDoc);
        foreach (var businessId in businessIds)
        {
            var ownedWabaUrl = $"{_options.GraphApiBase}/{_options.ApiVersion}/{businessId}/owned_whatsapp_business_accounts?fields=id,name";
            var ownedWabas = await GraphGetAsync(ownedWabaUrl, accessToken, ct);
            var wabas = ReadObjectsFromDataArray(ownedWabas);
            foreach (var waba in wabas)
            {
                var wabaId = TryGetString(waba, "id");
                if (string.IsNullOrWhiteSpace(wabaId)) continue;
                var wabaName = TryGetString(waba, "name");
                var phonesUrl = $"{_options.GraphApiBase}/{_options.ApiVersion}/{wabaId}/phone_numbers?fields=id,display_phone_number,verified_name";
                var phonesDoc = await GraphGetAsync(phonesUrl, accessToken, ct);
                var phones = ReadObjectsFromDataArray(phonesDoc);
                foreach (var phone in phones)
                {
                    var phoneId = TryGetString(phone, "id");
                    if (string.IsNullOrWhiteSpace(phoneId)) continue;
                    var key = $"{wabaId}|{phoneId}";
                    if (!seen.Add(key)) continue;
                    var display = TryGetString(phone, "display_phone_number");
                    if (string.IsNullOrWhiteSpace(display)) display = TryGetString(phone, "verified_name");
                    list.Add(new DiscoveredWabaAsset
                    {
                        BusinessId = businessId,
                        WabaId = wabaId,
                        WabaName = string.IsNullOrWhiteSpace(wabaName) ? "WhatsApp Business Account" : wabaName,
                        PhoneNumberId = phoneId,
                        DisplayPhoneNumber = string.IsNullOrWhiteSpace(display) ? "Unknown Number" : display
                    });
                }
            }
        }

        return list;
    }

    private static DiscoveredWabaAsset? ParseWabaAssetFromMe(JsonDocument? meDoc)
    {
        if (meDoc is null) return null;
        var root = meDoc.RootElement;
        if (!root.TryGetProperty("whatsapp_business_accounts", out var wabasNode)) return null;
        if (!wabasNode.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return null;

        foreach (var waba in data.EnumerateArray())
        {
            var wabaId = TryGetString(waba, "id");
            if (string.IsNullOrWhiteSpace(wabaId)) continue;
            var wabaName = TryGetString(waba, "name");
            if (!waba.TryGetProperty("phone_numbers", out var phonesNode) || !phonesNode.TryGetProperty("data", out var phonesData)) continue;

            foreach (var phone in phonesData.EnumerateArray())
            {
                var phoneId = TryGetString(phone, "id");
                if (string.IsNullOrWhiteSpace(phoneId)) continue;
                var display = TryGetString(phone, "display_phone_number");
                if (string.IsNullOrWhiteSpace(display)) display = TryGetString(phone, "verified_name");

                return new DiscoveredWabaAsset
                {
                    WabaId = wabaId,
                    WabaName = string.IsNullOrWhiteSpace(wabaName) ? "WhatsApp Business Account" : wabaName,
                    PhoneNumberId = phoneId,
                    DisplayPhoneNumber = string.IsNullOrWhiteSpace(display) ? "Unknown Number" : display
                };
            }
        }
        return null;
    }

    private async Task<(bool Ok, int StatusCode, string Body)> GraphGetRawAsync(string url, string accessToken, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            logger.LogWarning("Graph call failed: {Url} status={Status} body={Body}", url, (int)resp.StatusCode, redactor.RedactText(body));
        return (resp.IsSuccessStatusCode, (int)resp.StatusCode, body);
    }

    private async Task<(bool Ok, int StatusCode, string Body)> GraphPostRawAsync(string url, string accessToken, string? payload, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        if (!string.IsNullOrWhiteSpace(payload))
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            logger.LogWarning("Graph POST failed: {Url} status={Status} body={Body}", url, (int)resp.StatusCode, redactor.RedactText(body));
        return (resp.IsSuccessStatusCode, (int)resp.StatusCode, body);
    }

    private async Task<(bool Ok, int StatusCode, string Body)> GraphDeleteRawAsync(string url, string accessToken, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Delete, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            logger.LogWarning("Graph DELETE failed: {Url} status={Status} body={Body}", url, (int)resp.StatusCode, redactor.RedactText(body));
        return (resp.IsSuccessStatusCode, (int)resp.StatusCode, body);
    }

    private async Task<string> FindBusinessForWabaAsync(string accessToken, string wabaId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(wabaId)) return string.Empty;
        var businessesUrl = $"{_options.GraphApiBase}/{_options.ApiVersion}/me/businesses?fields=id,name";
        var businessesDoc = await GraphGetAsync(businessesUrl, accessToken, ct);
        var businessIds = ReadIdsFromDataArray(businessesDoc);
        foreach (var businessId in businessIds)
        {
            var ownedWabaUrl = $"{_options.GraphApiBase}/{_options.ApiVersion}/{businessId}/owned_whatsapp_business_accounts?fields=id";
            var ownedWabas = await GraphGetAsync(ownedWabaUrl, accessToken, ct);
            var wabas = ReadObjectsFromDataArray(ownedWabas);
            if (wabas.Any(x => string.Equals(TryGetString(x, "id"), wabaId, StringComparison.OrdinalIgnoreCase)))
                return businessId;
        }

        return string.Empty;
    }

    private async Task<SystemUserLifecycleResult> ProvisionSystemUserAndPermanentTokenAsync(string bootstrapToken, DiscoveredWabaAsset discovered, CancellationToken ct)
    {
        var warnings = new List<string>();
        var businessId = discovered.BusinessId;
        var systemUserId = string.Empty;
        var systemUserName = string.Empty;
        DateTime? systemUserCreatedAt = null;
        DateTime? assetsAssignedAt = null;
        string permanentToken = string.Empty;
        DateTime? tokenExpiresAt = null;

        if (string.IsNullOrWhiteSpace(businessId))
        {
            businessId = await FindBusinessForWabaAsync(bootstrapToken, discovered.WabaId, ct);
        }

        if (string.IsNullOrWhiteSpace(businessId))
        {
            warnings.Add("Business Manager ID could not be discovered; continuing with exchanged token.");
            return new SystemUserLifecycleResult
            {
                BusinessId = string.Empty,
                AccessToken = string.Empty,
                TokenSource = "embedded_exchange",
                Warnings = warnings
            };
        }

        var systemUserCreate = await CreateSystemUserAsync(bootstrapToken, businessId, ct);
        if (!string.IsNullOrWhiteSpace(systemUserCreate.SystemUserId))
        {
            systemUserId = systemUserCreate.SystemUserId;
            systemUserName = systemUserCreate.SystemUserName;
            systemUserCreatedAt = DateTime.UtcNow;
        }
        else
        {
            warnings.Add("System user creation failed; continuing with exchanged token.");
            if (!string.IsNullOrWhiteSpace(systemUserCreate.Error))
                warnings.Add($"system_user_error={systemUserCreate.Error}");
        }

        if (!string.IsNullOrWhiteSpace(systemUserId))
        {
            var assigned = await AssignSystemUserAssetsAsync(bootstrapToken, systemUserId, discovered.WabaId, discovered.PhoneNumberId, ct);
            if (assigned)
            {
                assetsAssignedAt = DateTime.UtcNow;
            }
            else
            {
                warnings.Add("System user asset assignment failed.");
            }

            var token = await GenerateSystemUserTokenAsync(bootstrapToken, systemUserId, ct);
            if (!string.IsNullOrWhiteSpace(token.AccessToken))
            {
                permanentToken = token.AccessToken;
                tokenExpiresAt = token.ExpiresAtUtc;
            }
            else
            {
                warnings.Add("Permanent system user token generation failed; continuing with exchanged token.");
                if (!string.IsNullOrWhiteSpace(token.Error))
                    warnings.Add($"system_token_error={token.Error}");
            }
        }

        return new SystemUserLifecycleResult
        {
            BusinessId = businessId,
            SystemUserId = systemUserId,
            SystemUserName = systemUserName,
            SystemUserCreatedAtUtc = systemUserCreatedAt,
            AssetsAssignedAtUtc = assetsAssignedAt,
            AccessToken = permanentToken,
            TokenExpiresAtUtc = tokenExpiresAt,
            TokenSource = string.IsNullOrWhiteSpace(permanentToken) ? "embedded_exchange" : "system_user_permanent",
            Warnings = warnings
        };
    }

    private async Task<(string SystemUserId, string SystemUserName, string Error)> CreateSystemUserAsync(string accessToken, string businessId, CancellationToken ct)
    {
        var slugPart = string.IsNullOrWhiteSpace(tenancy.TenantSlug) ? tenancy.TenantId.ToString("N")[..8] : tenancy.TenantSlug;
        var name = $"textzy-{slugPart}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var url = $"{_options.GraphApiBase}/{_options.ApiVersion}/{businessId}/system_users";
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["name"] = name,
            ["role"] = "EMPLOYEE"
        });

        var client = httpClientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        req.Content = content;
        var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("System user create failed: tenant={TenantId} business={BusinessId} status={Status} body={Body}", tenancy.TenantId, businessId, (int)resp.StatusCode, redactor.RedactText(body));
            return (string.Empty, string.Empty, body);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var id = doc.RootElement.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
            return (id, name, string.Empty);
        }
        catch
        {
            return (string.Empty, string.Empty, body);
        }
    }

    private async Task<bool> AssignSystemUserAssetsAsync(string accessToken, string systemUserId, string wabaId, string phoneNumberId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(systemUserId)) return false;
        var client = httpClientFactory.CreateClient();
        async Task<bool> AssignOneAsync(string assetId)
        {
            if (string.IsNullOrWhiteSpace(assetId)) return true;
            var url = $"{_options.GraphApiBase}/{_options.ApiVersion}/{systemUserId}/assigned_assets";
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["asset"] = assetId,
                ["tasks"] = "[\"MANAGE\"]"
            });
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            req.Content = content;
            var resp = await client.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("System user asset assignment failed: tenant={TenantId} su={SystemUserId} asset={AssetId} status={Status} body={Body}", tenancy.TenantId, systemUserId, assetId, (int)resp.StatusCode, redactor.RedactText(body));
            }

            return resp.IsSuccessStatusCode;
        }

        var okWaba = await AssignOneAsync(wabaId);
        var okPhone = await AssignOneAsync(phoneNumberId);
        return okWaba && okPhone;
    }

    private async Task<(string AccessToken, DateTime? ExpiresAtUtc, string Error)> GenerateSystemUserTokenAsync(string accessToken, string systemUserId, CancellationToken ct)
    {
        var url = $"{_options.GraphApiBase}/{_options.ApiVersion}/{systemUserId}/access_tokens";
        var scope = "[\"whatsapp_business_management\",\"whatsapp_business_messaging\",\"business_management\"]";
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["app_id"] = _options.AppId,
            ["scope"] = scope
        });

        var client = httpClientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        req.Content = content;
        var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("System user token generation failed: tenant={TenantId} su={SystemUserId} status={Status} body={Body}", tenancy.TenantId, systemUserId, (int)resp.StatusCode, redactor.RedactText(body));
            return (string.Empty, null, body);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var token = doc.RootElement.TryGetProperty("access_token", out var tokenProp) ? tokenProp.GetString() ?? string.Empty : string.Empty;
            DateTime? expires = null;
            if (doc.RootElement.TryGetProperty("expires_at", out var expProp))
            {
                if (expProp.ValueKind == JsonValueKind.Number && expProp.TryGetInt64(out var expUnix))
                    expires = DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime;
                else if (expProp.ValueKind == JsonValueKind.String && long.TryParse(expProp.GetString(), out var expUnixStr))
                    expires = DateTimeOffset.FromUnixTimeSeconds(expUnixStr).UtcDateTime;
            }
            return (token, expires, string.Empty);
        }
        catch
        {
            return (string.Empty, null, body);
        }
    }

    private async Task<JsonDocument?> GraphGetAsync(string url, string accessToken, CancellationToken ct)
    {
        var raw = await GraphGetRawAsync(url, accessToken, ct);
        if (!raw.Ok || string.IsNullOrWhiteSpace(raw.Body)) return null;
        try { return JsonDocument.Parse(raw.Body); } catch { return null; }
    }

    private async Task<TenantWabaConfig?> GetTenantConfigRowAsync(bool onlyActive, CancellationToken ct)
    {
        return await tenantDb.Set<TenantWabaConfig>()
            .Where(x => x.TenantId == tenancy.TenantId && (!onlyActive || x.IsActive))
            // Prefer active mapping first. This avoids showing "disconnected" when a newer
            // inactive/requested row exists alongside an already connected active row.
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.ConnectedAtUtc)
            .ThenByDescending(x => x.OnboardingStartedAtUtc)
            .ThenByDescending(x => x.CodeReceivedAtUtc)
            .ThenByDescending(x => x.ExchangedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<TenantWabaConfig> GetOrCreateTenantConfigAsync(CancellationToken ct)
    {
        var config = await GetTenantConfigRowAsync(onlyActive: false, ct);
        if (config is not null) return config;
        config = new TenantWabaConfig { Id = Guid.NewGuid(), TenantId = tenancy.TenantId };
        tenantDb.Set<TenantWabaConfig>().Add(config);
        return config;
    }

    private async Task<(Guid TenantId, string TenantSlug, string TenantName)?> FindTenantBindingConflictAsync(string wabaId, string phoneNumberId, CancellationToken ct, Guid? currentTenantId = null)
    {
        if (string.IsNullOrWhiteSpace(wabaId) && string.IsNullOrWhiteSpace(phoneNumberId)) return null;
        var tenantToSkip = currentTenantId ?? tenancy.TenantId;
        var tenants = await controlDb.Tenants.ToListAsync(ct);
        foreach (var tenant in tenants)
        {
            if (tenant.Id == tenantToSkip) continue;
            try
            {
                using var otherDb = SeedData.CreateTenantDbContext(tenant.DataConnectionString);
                var cfg = await otherDb.Set<TenantWabaConfig>()
                    .Where(x => x.TenantId == tenant.Id && x.IsActive)
                    .OrderByDescending(x => x.ConnectedAtUtc)
                    .FirstOrDefaultAsync(ct);
                if (cfg is null) continue;
                var sameWaba = !string.IsNullOrWhiteSpace(wabaId) && string.Equals(cfg.WabaId, wabaId, StringComparison.OrdinalIgnoreCase);
                var samePhone = !string.IsNullOrWhiteSpace(phoneNumberId) && string.Equals(cfg.PhoneNumberId, phoneNumberId, StringComparison.OrdinalIgnoreCase);
                if (sameWaba || samePhone)
                {
                    return (tenant.Id, tenant.Slug, tenant.Name);
                }
            }
            catch
            {
                // Ignore unreachable tenant DB in conflict scan.
            }
        }

        return null;
    }

    private static List<string> ReadIdsFromDataArray(JsonDocument? doc)
    {
        var list = new List<string>();
        if (doc is null) return list;
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return list;
        foreach (var item in data.EnumerateArray())
        {
            var id = TryGetString(item, "id");
            if (!string.IsNullOrWhiteSpace(id)) list.Add(id);
        }
        return list;
    }

    private static List<JsonElement> ReadObjectsFromDataArray(JsonDocument? doc)
    {
        var list = new List<JsonElement>();
        if (doc is null) return list;
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return list;
        list.AddRange(data.EnumerateArray());
        return list;
    }

    private static string TryGetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var v) ? v.GetString() ?? string.Empty : string.Empty;

    public async Task<string> SendSessionMessageAsync(string recipient, string body, CancellationToken ct = default)
    {
        var cfg = await GetTenantConfigAsync(ct) ?? throw new InvalidOperationException("WABA config not connected.");
        if (string.IsNullOrWhiteSpace(cfg.PhoneNumberId)) throw new InvalidOperationException("Phone number ID missing.");
        var token = UnprotectToken(cfg.AccessToken);

        var client = httpClientFactory.CreateClient();
        var url = $"{_options.GraphApiBase}/{_options.ApiVersion}/{cfg.PhoneNumberId}/messages";
        var payload = JsonSerializer.Serialize(new { messaging_product = "whatsapp", to = recipient, type = "text", text = new { body } });

        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var resp = await client.SendAsync(req, ct);
        var responseBody = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("WhatsApp session send failed: tenant={TenantId} status={Status} payload={Payload}", tenancy.TenantId, (int)resp.StatusCode, redactor.RedactText(responseBody));
            throw new InvalidOperationException($"WhatsApp send failed ({(int)resp.StatusCode}): {responseBody}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement.GetProperty("messages")[0].GetProperty("id").GetString() ?? $"wa_{Guid.NewGuid():N}";
    }

    public async Task<JsonElement> ListMetaFlowsAsync(CancellationToken ct = default)
    {
        var cfg = await GetTenantConfigAsync(ct) ?? throw new InvalidOperationException("WABA config not connected.");
        if (string.IsNullOrWhiteSpace(cfg.WabaId)) throw new InvalidOperationException("WABA id missing.");
        var token = UnprotectToken(cfg.AccessToken);
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("WABA access token missing.");

        await WaitForMetaFlowRateLimitAsync(ct);
        var url = $"{_options.GraphApiBase}/{_options.ApiVersion}/{cfg.WabaId}/flows?fields=id,name,status,categories,json_version,data_api_version,validation_errors,updated_time&limit=200";
        var raw = await GraphGetWithMetaRetriesAsync(url, token, ct);
        if (!raw.Ok)
            throw new InvalidOperationException($"Meta flow list failed ({raw.StatusCode}): {raw.Body}");
        if (string.IsNullOrWhiteSpace(raw.Body))
            return JsonDocument.Parse("{\"data\":[]}").RootElement.Clone();
        using var doc = JsonDocument.Parse(raw.Body);
        return doc.RootElement.Clone();
    }

    public async Task<JsonElement> GetMetaFlowAsync(string metaFlowId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(metaFlowId)) throw new InvalidOperationException("Meta flow id is required.");
        var cfg = await GetTenantConfigAsync(ct) ?? throw new InvalidOperationException("WABA config not connected.");
        var token = UnprotectToken(cfg.AccessToken);
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("WABA access token missing.");

        await WaitForMetaFlowRateLimitAsync(ct);
        var flowId = Uri.EscapeDataString(metaFlowId.Trim());
        var fields =
            "id,name,status,categories,json_version,data_api_version,endpoint_uri,validation_errors,updated_time,preview,flow_json,json";
        var url = $"{_options.GraphApiBase}/{_options.ApiVersion}/{flowId}?fields={fields}";
        var raw = await GraphGetWithMetaRetriesAsync(url, token, ct);
        if (!raw.Ok)
            throw new InvalidOperationException($"Meta flow fetch failed ({raw.StatusCode}): {raw.Body}");
        if (string.IsNullOrWhiteSpace(raw.Body))
            throw new InvalidOperationException("Meta flow payload is empty.");
        using var doc = JsonDocument.Parse(raw.Body);
        return doc.RootElement.Clone();
    }

    public async Task<JsonElement> UpdateMetaFlowAsync(string metaFlowId, Dictionary<string, string> fields, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(metaFlowId)) throw new InvalidOperationException("Meta flow id is required.");
        var cfg = await GetTenantConfigAsync(ct) ?? throw new InvalidOperationException("WABA config not connected.");
        var token = UnprotectToken(cfg.AccessToken);
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("WABA access token missing.");
        if (fields.Count == 0) throw new InvalidOperationException("At least one field is required for update.");

        await WaitForMetaFlowRateLimitAsync(ct);
        var flowId = Uri.EscapeDataString(metaFlowId.Trim());
        var url = $"{_options.GraphApiBase}/{_options.ApiVersion}/{flowId}";

        var payload = string.Join("&", fields.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value ?? string.Empty)}"));
        var raw = await GraphPostWithMetaRetriesAsync(url, token, payload, "application/x-www-form-urlencoded", ct);
        if (!raw.Ok)
            throw new InvalidOperationException($"Meta flow update failed ({raw.StatusCode}): {raw.Body}");

        if (string.IsNullOrWhiteSpace(raw.Body))
            return JsonDocument.Parse("{\"success\":true}").RootElement.Clone();
        using var doc = JsonDocument.Parse(raw.Body);
        return doc.RootElement.Clone();
    }

    public async Task<JsonElement> CreateMetaFlowAsync(
        string name,
        string categoriesJson,
        string endpointUri,
        string dataApiVersion,
        string jsonVersion,
        string flowJson,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Flow name is required.");
        var cfg = await GetTenantConfigAsync(ct) ?? throw new InvalidOperationException("WABA config not connected.");
        if (string.IsNullOrWhiteSpace(cfg.WabaId)) throw new InvalidOperationException("WABA id missing.");
        var token = UnprotectToken(cfg.AccessToken);
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("WABA access token missing.");

        await WaitForMetaFlowRateLimitAsync(ct);
        var url = $"{_options.GraphApiBase}/{_options.ApiVersion}/{cfg.WabaId}/flows";
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = name.Trim()
        };
        if (!string.IsNullOrWhiteSpace(categoriesJson)) fields["categories"] = categoriesJson.Trim();
        if (!string.IsNullOrWhiteSpace(endpointUri)) fields["endpoint_uri"] = endpointUri.Trim();
        if (!string.IsNullOrWhiteSpace(dataApiVersion)) fields["data_api_version"] = dataApiVersion.Trim();
        if (!string.IsNullOrWhiteSpace(jsonVersion)) fields["json_version"] = jsonVersion.Trim();
        if (!string.IsNullOrWhiteSpace(flowJson)) fields["flow_json"] = flowJson.Trim();

        var payload = string.Join("&", fields.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value ?? string.Empty)}"));
        var raw = await GraphPostWithMetaRetriesAsync(url, token, payload, "application/x-www-form-urlencoded", ct);
        if (!raw.Ok)
            throw new InvalidOperationException($"Meta flow create failed ({raw.StatusCode}): {raw.Body}");

        if (string.IsNullOrWhiteSpace(raw.Body))
            return JsonDocument.Parse("{\"success\":true}").RootElement.Clone();
        using var doc = JsonDocument.Parse(raw.Body);
        return doc.RootElement.Clone();
    }

    public async Task<JsonElement> PublishMetaFlowAsync(string metaFlowId, string flowJson, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(metaFlowId)) throw new InvalidOperationException("Meta flow id is required.");
        var cfg = await GetTenantConfigAsync(ct) ?? throw new InvalidOperationException("WABA config not connected.");
        var token = UnprotectToken(cfg.AccessToken);
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("WABA access token missing.");

        await WaitForMetaFlowRateLimitAsync(ct);
        var flowId = Uri.EscapeDataString(metaFlowId.Trim());
        var url = $"{_options.GraphApiBase}/{_options.ApiVersion}/{flowId}/publish";
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(flowJson)) fields["flow_json"] = flowJson.Trim();
        var payload = string.Join("&", fields.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value ?? string.Empty)}"));

        var raw = await GraphPostWithMetaRetriesAsync(url, token, payload, "application/x-www-form-urlencoded", ct);
        if (!raw.Ok)
            throw new InvalidOperationException($"Meta flow publish failed ({raw.StatusCode}): {raw.Body}");

        if (string.IsNullOrWhiteSpace(raw.Body))
            return JsonDocument.Parse("{\"success\":true}").RootElement.Clone();
        using var doc = JsonDocument.Parse(raw.Body);
        return doc.RootElement.Clone();
    }

    public async Task<JsonElement> DeleteMetaFlowAsync(string metaFlowId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(metaFlowId)) throw new InvalidOperationException("Meta flow id is required.");
        var cfg = await GetTenantConfigAsync(ct) ?? throw new InvalidOperationException("WABA config not connected.");
        var token = UnprotectToken(cfg.AccessToken);
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("WABA access token missing.");

        await WaitForMetaFlowRateLimitAsync(ct);
        var flowId = Uri.EscapeDataString(metaFlowId.Trim());
        var url = $"{_options.GraphApiBase}/{_options.ApiVersion}/{flowId}";
        var raw = await GraphDeleteWithMetaRetriesAsync(url, token, ct);
        if (!raw.Ok)
            throw new InvalidOperationException($"Meta flow delete failed ({raw.StatusCode}): {raw.Body}");

        if (string.IsNullOrWhiteSpace(raw.Body))
            return JsonDocument.Parse("{\"success\":true}").RootElement.Clone();
        using var doc = JsonDocument.Parse(raw.Body);
        return doc.RootElement.Clone();
    }

    private async Task WaitForMetaFlowRateLimitAsync(CancellationToken ct)
    {
        var maxPerMinute = Math.Clamp(configuration.GetValue("WhatsApp:FlowApiMaxRequestsPerMinute", 30), 5, 120);
        var limiter = MetaFlowLimiters.GetOrAdd(tenancy.TenantId, _ => new TenantMetaFlowLimiter(maxPerMinute));
        limiter.MaxPerMinute = maxPerMinute;
        await limiter.WaitAsync(ct);
    }

    private async Task<(bool Ok, int StatusCode, string Body)> GraphGetWithMetaRetriesAsync(string url, string token, CancellationToken ct)
    {
        var maxAttempts = Math.Clamp(configuration.GetValue("WhatsApp:FlowApiMaxRetries", 3), 1, 5);
        var delayMs = Math.Clamp(configuration.GetValue("WhatsApp:FlowApiRetryDelayMs", 1200), 500, 10000);

        (bool Ok, int StatusCode, string Body) last = (false, 0, string.Empty);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            last = await GraphGetRawAsync(url, token, ct);
            if (last.Ok) return last;

            var retryable = last.StatusCode == 429 || last.StatusCode == 408 || last.StatusCode >= 500;
            if (!retryable || attempt == maxAttempts) break;

            var wait = delayMs * (int)Math.Pow(2, attempt - 1);
            await Task.Delay(wait, ct);
        }

        return last;
    }

    private async Task<(bool Ok, int StatusCode, string Body)> GraphPostWithMetaRetriesAsync(
        string url,
        string token,
        string payload,
        string contentType,
        CancellationToken ct)
    {
        var maxAttempts = Math.Clamp(configuration.GetValue("WhatsApp:FlowApiMaxRetries", 3), 1, 5);
        var delayMs = Math.Clamp(configuration.GetValue("WhatsApp:FlowApiRetryDelayMs", 1200), 500, 10000);

        (bool Ok, int StatusCode, string Body) last = (false, 0, string.Empty);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            last = await GraphPostRawWithContentTypeAsync(url, token, payload, contentType, ct);
            if (last.Ok) return last;

            var retryable = last.StatusCode == 429 || last.StatusCode == 408 || last.StatusCode >= 500;
            if (!retryable || attempt == maxAttempts) break;

            var wait = delayMs * (int)Math.Pow(2, attempt - 1);
            await Task.Delay(wait, ct);
        }

        return last;
    }

    private async Task<(bool Ok, int StatusCode, string Body)> GraphDeleteWithMetaRetriesAsync(string url, string token, CancellationToken ct)
    {
        var maxAttempts = Math.Clamp(configuration.GetValue("WhatsApp:FlowApiMaxRetries", 3), 1, 5);
        var delayMs = Math.Clamp(configuration.GetValue("WhatsApp:FlowApiRetryDelayMs", 1200), 500, 10000);

        (bool Ok, int StatusCode, string Body) last = (false, 0, string.Empty);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            last = await GraphDeleteRawAsync(url, token, ct);
            if (last.Ok) return last;

            var retryable = last.StatusCode == 429 || last.StatusCode == 408 || last.StatusCode >= 500;
            if (!retryable || attempt == maxAttempts) break;

            var wait = delayMs * (int)Math.Pow(2, attempt - 1);
            await Task.Delay(wait, ct);
        }

        return last;
    }

    private async Task<(bool Ok, int StatusCode, string Body)> GraphPostRawWithContentTypeAsync(
        string url,
        string accessToken,
        string payload,
        string contentType,
        CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        req.Content = new StringContent(payload ?? string.Empty, Encoding.UTF8, contentType);
        var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            logger.LogWarning("Graph POST failed: {Url} status={Status} body={Body}", url, (int)resp.StatusCode, redactor.RedactText(body));
        return (resp.IsSuccessStatusCode, (int)resp.StatusCode, body);
    }

    private sealed class TenantMetaFlowLimiter
    {
        private readonly Queue<DateTime> _hits = new();
        private readonly object _gate = new();
        public int MaxPerMinute { get; set; }

        public TenantMetaFlowLimiter(int maxPerMinute)
        {
            MaxPerMinute = maxPerMinute;
        }

        public async Task WaitAsync(CancellationToken ct)
        {
            while (true)
            {
                TimeSpan? delay = null;
                lock (_gate)
                {
                    var now = DateTime.UtcNow;
                    while (_hits.Count > 0 && (now - _hits.Peek()) > TimeSpan.FromMinutes(1))
                        _hits.Dequeue();

                    if (_hits.Count < MaxPerMinute)
                    {
                        _hits.Enqueue(now);
                        return;
                    }

                    var oldest = _hits.Peek();
                    var waitUntil = oldest.AddMinutes(1);
                    delay = waitUntil > now ? (waitUntil - now) : TimeSpan.FromMilliseconds(250);
                }

                await Task.Delay(delay!.Value, ct);
            }
        }
    }

    public async Task<string> SendTemplateMessageAsync(WabaSendTemplateRequest request, CancellationToken ct = default)
    {
        var cfg = await GetTenantConfigAsync(ct) ?? throw new InvalidOperationException("WABA config not connected.");
        var token = UnprotectToken(cfg.AccessToken);
        var client = httpClientFactory.CreateClient();
        var url = $"{_options.GraphApiBase}/{_options.ApiVersion}/{cfg.PhoneNumberId}/messages";

        var components = new[] { new { type = "body", parameters = request.BodyParameters.Select(x => new { type = "text", text = x }).ToArray() } };
        var payload = JsonSerializer.Serialize(new { messaging_product = "whatsapp", to = request.Recipient, type = "template", template = new { name = request.TemplateName, language = new { code = request.LanguageCode }, components } });

        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var resp = await client.SendAsync(req, ct);
        var responseBody = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("WhatsApp template send failed: tenant={TenantId} status={Status} payload={Payload}", tenancy.TenantId, (int)resp.StatusCode, redactor.RedactText(responseBody));
            throw new InvalidOperationException($"WhatsApp template send failed ({(int)resp.StatusCode}): {responseBody}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement.GetProperty("messages")[0].GetProperty("id").GetString() ?? $"wa_tpl_{Guid.NewGuid():N}";
    }

    public async Task<bool> IsSessionWindowOpenAsync(string recipient, CancellationToken ct = default)
    {
        var window = await tenantDb.Set<ConversationWindow>().FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.Recipient == recipient, ct);
        if (window is null) return false;
        return window.LastInboundAtUtc >= DateTime.UtcNow.AddHours(-24);
    }

    private string ProtectToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return string.Empty;
        if (token.StartsWith("enc:", StringComparison.Ordinal)) return token;
        return $"enc:{crypto.Encrypt(token)}";
    }

    private string UnprotectToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return string.Empty;
        if (!token.StartsWith("enc:", StringComparison.Ordinal)) return token;
        var payload = token[4..];
        try
        {
            return crypto.Decrypt(payload);
        }
        catch
        {
            return string.Empty;
        }
    }
}
