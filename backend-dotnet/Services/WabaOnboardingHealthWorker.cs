using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;

namespace Textzy.Api.Services;

public class WabaOnboardingHealthWorker(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    TenantSchemaGuardService schemaGuard,
    SensitiveDataRedactor redactor,
    ILogger<WabaOnboardingHealthWorker> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(Math.Clamp(
        int.TryParse(configuration["WabaHealth:IntervalMinutes"], out var minutes) ? minutes : 30,
        5,
        240));
    private readonly TimeSpan _tenantCacheTtl = TimeSpan.FromMinutes(15);
    private readonly WhatsAppOptions _options = configuration.GetSection("WhatsApp").Get<WhatsAppOptions>() ?? new WhatsAppOptions();
    private readonly SemaphoreSlim _tenantCacheLock = new(1, 1);
    private List<TenantScanTarget> _tenantCache = [];
    private DateTime _tenantCacheExpiresUtc = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunHealthScan(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning("WABA onboarding health worker iteration failed: {Error}", redactor.RedactText(ex.Message));
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task RunHealthScan(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var controlDb = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var tenants = await GetTenantsAsync(controlDb, ct);
        var client = httpClientFactory.CreateClient();

        foreach (var tenant in tenants)
        {
            try
            {
                var tenantConn = string.IsNullOrWhiteSpace(tenant.DataConnectionString)
                    ? controlDb.Database.GetConnectionString()
                    : tenant.DataConnectionString;
                if (string.IsNullOrWhiteSpace(tenantConn)) continue;
                await schemaGuard.EnsureContactEncryptionColumnsAsync(tenant.Id, tenantConn, ct);

                using var tenantDb = SeedData.CreateTenantDbContext(tenantConn);
                var cfg = await tenantDb.TenantWabaConfigs
                    .Where(x => x.TenantId == tenant.Id && x.IsActive)
                    .OrderByDescending(x => x.ConnectedAtUtc)
                    .FirstOrDefaultAsync(ct);
                if (cfg is null || string.IsNullOrWhiteSpace(cfg.AccessToken) || string.IsNullOrWhiteSpace(cfg.WabaId) || string.IsNullOrWhiteSpace(cfg.PhoneNumberId))
                    continue;

                var crypto = scope.ServiceProvider.GetRequiredService<SecretCryptoService>();
                var token = UnprotectToken(cfg.AccessToken, crypto);
                if (string.IsNullOrWhiteSpace(token)) continue;

                var wabaOk = await GraphGetAsync(client, $"{_options.GraphApiBase}/{_options.ApiVersion}/{cfg.WabaId}?fields=id,business_verification_status", token, ct);
                var phoneOk = await GraphGetAsync(client, $"{_options.GraphApiBase}/{_options.ApiVersion}/{cfg.PhoneNumberId}?fields=id,quality_rating,name_status", token, ct);
                var webhooksOk = await GraphGetAsync(client, $"{_options.GraphApiBase}/{_options.ApiVersion}/{cfg.WabaId}/subscribed_apps", token, ct);
                if (!webhooksOk.Ok)
                {
                    var subscribeOk = await GraphPostAsync(client, $"{_options.GraphApiBase}/{_options.ApiVersion}/{cfg.WabaId}/subscribed_apps", token, ct);
                    if (subscribeOk.Ok)
                        webhooksOk = await GraphGetAsync(client, $"{_options.GraphApiBase}/{_options.ApiVersion}/{cfg.WabaId}/subscribed_apps", token, ct);
                }

                var pass = wabaOk.Ok && phoneOk.Ok && webhooksOk.Ok;
                if (wabaOk.Ok && wabaOk.Root.TryGetProperty("business_verification_status", out var bvs))
                    cfg.BusinessVerificationStatus = bvs.GetString() ?? cfg.BusinessVerificationStatus;
                if (phoneOk.Ok && phoneOk.Root.TryGetProperty("quality_rating", out var qr))
                    cfg.PhoneQualityRating = qr.GetString() ?? cfg.PhoneQualityRating;
                if (phoneOk.Ok && phoneOk.Root.TryGetProperty("name_status", out var ns))
                    cfg.PhoneNameStatus = ns.GetString() ?? cfg.PhoneNameStatus;

                cfg.PermissionAuditPassed = pass;
                cfg.WebhookVerifiedAtUtc = pass ? DateTime.UtcNow : cfg.WebhookVerifiedAtUtc;
                cfg.OnboardingState = pass ? "ready" : "degraded";
                cfg.LastError = pass ? string.Empty : $"health_check_failed:waba={wabaOk.StatusCode};phone={phoneOk.StatusCode};webhook={webhooksOk.StatusCode}";
                await tenantDb.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning("WABA health scan failed for tenant {TenantId}: {Error}", tenant.Id, redactor.RedactText(ex.Message));
            }
        }
    }

    private static string UnprotectToken(string token, SecretCryptoService crypto)
    {
        if (string.IsNullOrWhiteSpace(token)) return string.Empty;
        if (!token.StartsWith("enc:", StringComparison.Ordinal)) return token;
        try { return crypto.Decrypt(token[4..]); } catch { return string.Empty; }
    }

    private static async Task<(bool Ok, int StatusCode, JsonElement Root)> GraphGetAsync(HttpClient client, string url, string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await client.SendAsync(req, ct);
        var code = (int)res.StatusCode;
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            using var failDoc = JsonDocument.Parse("{}");
            return (false, code, failDoc.RootElement.Clone());
        }
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        return (true, code, doc.RootElement.Clone());
    }

    private static async Task<(bool Ok, int StatusCode)> GraphPostAsync(HttpClient client, string url, string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await client.SendAsync(req, ct);
        return (res.IsSuccessStatusCode, (int)res.StatusCode);
    }

    private async Task<List<TenantScanTarget>> GetTenantsAsync(ControlDbContext controlDb, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (_tenantCache.Count == 0 || now >= _tenantCacheExpiresUtc)
        {
            await _tenantCacheLock.WaitAsync(ct);
            try
            {
                if (_tenantCache.Count == 0 || now >= _tenantCacheExpiresUtc)
                {
                    _tenantCache = await controlDb.Tenants.AsNoTracking()
                        .OrderBy(x => x.CreatedAtUtc)
                        .Select(x => new TenantScanTarget
                        {
                            Id = x.Id,
                            Slug = x.Slug,
                            DataConnectionString = x.DataConnectionString
                        })
                        .ToListAsync(ct);
                    _tenantCacheExpiresUtc = now.Add(_tenantCacheTtl);
                }
            }
            finally
            {
                _tenantCacheLock.Release();
            }
        }

        return _tenantCache;
    }

    private sealed class TenantScanTarget
    {
        public Guid Id { get; init; }
        public string Slug { get; init; } = string.Empty;
        public string DataConnectionString { get; init; } = string.Empty;
    }
}
