using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/platform/waba/readiness")]
public class PlatformWabaReadinessController(
    ControlDbContext controlDb,
    AuthContext auth,
    RbacService rbac,
    SecretCryptoService crypto,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    OutboundMessageQueueService outboundQueue,
    WabaWebhookQueueService webhookQueue) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] bool deepGraphCheck = false, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        var now = DateTime.UtcNow;
        var from24h = now.AddHours(-24);
        var waOptions = configuration.GetSection("WhatsApp").Get<WhatsAppOptions>() ?? new WhatsAppOptions();
        var graphClient = httpClientFactory.CreateClient();

        var tenants = await controlDb.Tenants
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(ct);

        var rows = new List<object>(tenants.Count);
        foreach (var tenant in tenants)
        {
            try
            {
                using var tenantDb = SeedData.CreateTenantDbContext(tenant.DataConnectionString);
                var cfg = await tenantDb.TenantWabaConfigs
                    .AsNoTracking()
                    .Where(x => x.TenantId == tenant.Id)
                    .OrderByDescending(x => x.IsActive)
                    .ThenByDescending(x => x.ConnectedAtUtc)
                    .FirstOrDefaultAsync(ct);

                if (cfg is null)
                {
                    rows.Add(new
                    {
                        tenantId = tenant.Id,
                        tenantName = tenant.Name,
                        tenantSlug = tenant.Slug,
                        configured = false
                    });
                    continue;
                }

                var templates = await tenantDb.Templates
                    .AsNoTracking()
                    .Where(x => x.TenantId == tenant.Id && x.Channel == ChannelType.WhatsApp)
                    .ToListAsync(ct);
                var statusCounts = templates
                    .GroupBy(x => (x.Status ?? string.Empty).Trim().ToUpperInvariant())
                    .ToDictionary(g => string.IsNullOrWhiteSpace(g.Key) ? "UNKNOWN" : g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

                var events24h = await controlDb.WebhookEvents
                    .AsNoTracking()
                    .Where(x => x.Provider == "meta" && x.TenantId == tenant.Id && x.ReceivedAtUtc >= from24h)
                    .ToListAsync(ct);

                var graphWebhookSubscribed = (bool?)null;
                var graphCheckError = string.Empty;
                if (deepGraphCheck && cfg.IsActive && !string.IsNullOrWhiteSpace(cfg.WabaId))
                {
                    var token = UnprotectToken(cfg.AccessToken, crypto);
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        try
                        {
                            var subUrl = $"{waOptions.GraphApiBase}/{waOptions.ApiVersion}/{cfg.WabaId}/subscribed_apps";
                            using var req = new HttpRequestMessage(HttpMethod.Get, subUrl);
                            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                            var res = await graphClient.SendAsync(req, ct);
                            graphWebhookSubscribed = res.IsSuccessStatusCode;
                            if (!res.IsSuccessStatusCode)
                            {
                                var body = await res.Content.ReadAsStringAsync(ct);
                                graphCheckError = $"subscribed_apps_failed:{(int)res.StatusCode}:{body}";
                            }
                        }
                        catch (Exception ex)
                        {
                            graphWebhookSubscribed = false;
                            graphCheckError = ex.GetType().Name;
                        }
                    }
                }

                rows.Add(new
                {
                    tenantId = tenant.Id,
                    tenantName = tenant.Name,
                    tenantSlug = tenant.Slug,
                    configured = true,
                    waba = new
                    {
                        cfg.IsActive,
                        cfg.OnboardingState,
                        cfg.WabaId,
                        cfg.PhoneNumberId,
                        cfg.PermissionAuditPassed,
                        cfg.WebhookSubscribedAtUtc,
                        cfg.WebhookVerifiedAtUtc,
                        cfg.LastError,
                        graphWebhookSubscribed,
                        graphCheckError
                    },
                    templates = new
                    {
                        total = templates.Count,
                        approved = statusCounts.TryGetValue("APPROVED", out var approved) ? approved : 0,
                        pending = (statusCounts.TryGetValue("PENDING", out var p1) ? p1 : 0) + (statusCounts.TryGetValue("IN_REVIEW", out var p2) ? p2 : 0),
                        rejected = statusCounts.TryGetValue("REJECTED", out var rejected) ? rejected : 0,
                        disabled = (statusCounts.TryGetValue("DISABLED", out var d1) ? d1 : 0) + (statusCounts.TryGetValue("PAUSED", out var d2) ? d2 : 0),
                        syncedAtUtc = cfg.TemplatesSyncedAtUtc
                    },
                    runtime24h = new
                    {
                        received = events24h.Count,
                        processed = events24h.Count(x => string.Equals(x.Status, "Processed", StringComparison.OrdinalIgnoreCase)),
                        retryScheduled = events24h.Count(x => string.Equals(x.Status, "RetryScheduled", StringComparison.OrdinalIgnoreCase)),
                        failed = events24h.Count(x =>
                            string.Equals(x.Status, "DeadLetter", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(x.Status, "Unmapped", StringComparison.OrdinalIgnoreCase)),
                        lastReceivedAtUtc = events24h.Count == 0 ? (DateTime?)null : events24h.Max(x => x.ReceivedAtUtc)
                    }
                });
            }
            catch (Exception ex)
            {
                rows.Add(new
                {
                    tenantId = tenant.Id,
                    tenantName = tenant.Name,
                    tenantSlug = tenant.Slug,
                    configured = false,
                    error = ex.GetType().Name
                });
            }
        }

        var configuredWebhook = (configuration["WebhookQueue:Provider"] ?? "memory").Trim().ToLowerInvariant();
        var configuredOutbound = (configuration["OutboundQueue:Provider"] ?? "memory").Trim().ToLowerInvariant();

        return Ok(new
        {
            generatedAtUtc = now,
            queue = new
            {
                webhook = new { configured = configuredWebhook, active = webhookQueue.ActiveProvider, depth = await webhookQueue.GetDepthAsync(ct) },
                outbound = new { configured = configuredOutbound, active = outboundQueue.ActiveProvider, depth = await outboundQueue.GetDepthAsync(ct) }
            },
            tenants = rows
        });
    }

    private static string UnprotectToken(string token, SecretCryptoService crypto)
    {
        if (string.IsNullOrWhiteSpace(token)) return string.Empty;
        if (!token.StartsWith("enc:", StringComparison.Ordinal)) return token;
        try { return crypto.Decrypt(token[4..]); } catch { return string.Empty; }
    }
}

