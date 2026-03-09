using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;

namespace Textzy.Api.Services;

public class TemplateStatusSyncWorker(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    TenantSchemaGuardService schemaGuard,
    SensitiveDataRedactor redactor,
    ILogger<TemplateStatusSyncWorker> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(Math.Clamp(
        int.TryParse(configuration["TemplateSync:IntervalMinutes"], out var minutes) ? minutes : 1440,
        15,
        1440));

    private readonly WhatsAppOptions _options = configuration.GetSection("WhatsApp").Get<WhatsAppOptions>() ?? new WhatsAppOptions();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Template status sync iteration failed: {Error}", redactor.RedactText(ex.Message));
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task RunSync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var controlDb = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var crypto = scope.ServiceProvider.GetRequiredService<SecretCryptoService>();
        var tenants = await controlDb.Tenants.AsNoTracking().ToListAsync(ct);
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
                if (cfg is null || string.IsNullOrWhiteSpace(cfg.WabaId) || string.IsNullOrWhiteSpace(cfg.AccessToken))
                    continue;

                var token = cfg.AccessToken.StartsWith("enc:", StringComparison.Ordinal)
                    ? SafeDecrypt(cfg.AccessToken[4..], crypto)
                    : cfg.AccessToken;
                if (string.IsNullOrWhiteSpace(token)) continue;

                await SyncTenantTemplatesAsync(tenantDb, controlDb, tenant, cfg, client, token, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Template status sync failed for tenant={TenantId}: {Error}", tenant.Id, redactor.RedactText(ex.Message));
            }
        }

        await controlDb.SaveChangesAsync(ct);
    }

    private async Task SyncTenantTemplatesAsync(
        TenantDbContext tenantDb,
        ControlDbContext controlDb,
        Tenant tenant,
        TenantWabaConfig cfg,
        HttpClient client,
        string token,
        CancellationToken ct)
    {
        var after = string.Empty;
        var pages = 0;
        var now = DateTime.UtcNow;
        var source = new Dictionary<string, (string name, string language, string status, string lifecycle, string category, string body, string headerType, string headerText, string footerText, string buttonsJson, string rejection)>(StringComparer.OrdinalIgnoreCase);

        while (pages < 20)
        {
            pages++;
            var url = $"{_options.GraphApiBase}/{_options.ApiVersion}/{cfg.WabaId}/message_templates?fields=name,status,category,language,rejected_reason,components";
            if (!string.IsNullOrWhiteSpace(after))
                url += $"&after={Uri.EscapeDataString(after)}";

            var response = await GraphGetRawAsync(client, url, token, ct);
            if (!response.ok)
                throw new InvalidOperationException($"meta_sync_failed:{response.statusCode}");

            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(response.body) ? "{}" : response.body);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in data.EnumerateArray())
                {
                    var name = TryGetString(row, "name");
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var language = TryGetString(row, "language");
                    if (string.IsNullOrWhiteSpace(language)) language = "en";
                    var key = $"{name}::{language}";

                    var status = TryGetString(row, "status");
                    var category = TryGetString(row, "category");
                    if (string.IsNullOrWhiteSpace(category)) category = "UTILITY";
                    var rejection = TryGetString(row, "rejected_reason");

                    ParseComponents(row, out var body, out var headerType, out var headerText, out var footerText, out var buttonsJson);
                    source[key] = (name, language, status, MapLifecycleStatus(status), category, body, headerType, headerText, footerText, buttonsJson, rejection);
                }
            }

            after = string.Empty;
            if (doc.RootElement.TryGetProperty("paging", out var paging) &&
                paging.TryGetProperty("cursors", out var cursors) &&
                cursors.TryGetProperty("after", out var afterNode))
            {
                after = afterNode.GetString() ?? string.Empty;
            }
            if (string.IsNullOrWhiteSpace(after)) break;
        }

        var existing = await tenantDb.Templates
            .Where(x => x.TenantId == tenant.Id && x.Channel == ChannelType.WhatsApp)
            .ToListAsync(ct);

        foreach (var row in existing)
        {
            var key = $"{row.Name}::{(row.Language ?? "en")}";
            if (!source.TryGetValue(key, out var synced)) continue;
            var previousStatus = (row.Status ?? string.Empty).ToLowerInvariant();

            row.Status = synced.status;
            row.LifecycleStatus = synced.lifecycle;
            row.Category = synced.category;
            row.Body = string.IsNullOrWhiteSpace(synced.body) ? row.Body : synced.body;
            row.HeaderType = synced.headerType;
            row.HeaderText = synced.headerText;
            row.FooterText = synced.footerText;
            row.ButtonsJson = synced.buttonsJson;
            row.RejectionReason = synced.rejection;

            var currentStatus = (row.Status ?? string.Empty).ToLowerInvariant();
            if ((previousStatus == "approved" || previousStatus == "pending") &&
                (currentStatus == "disabled" || currentStatus == "paused" || currentStatus == "rejected"))
            {
                controlDb.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.Id,
                    ActorUserId = Guid.Empty,
                    Action = "template.status.alert",
                    Details = $"tenant={tenant.Slug}; template={row.Name}; from={previousStatus}; to={currentStatus}; reason={synced.rejection}",
                    CreatedAtUtc = now
                });
            }
        }

        var existingKeys = existing
            .Select(x => $"{x.Name}::{(x.Language ?? "en")}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, synced) in source)
        {
            if (existingKeys.Contains(key)) continue;
            tenantDb.Templates.Add(new Template
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                Name = synced.name,
                Channel = ChannelType.WhatsApp,
                Category = synced.category,
                Language = synced.language,
                Body = string.IsNullOrWhiteSpace(synced.body) ? $"Template: {synced.name}" : synced.body,
                LifecycleStatus = synced.lifecycle,
                Version = 1,
                VariantGroup = $"{synced.name}:{synced.language}",
                Status = synced.status,
                HeaderType = synced.headerType,
                HeaderText = synced.headerText,
                HeaderMediaId = string.Empty,
                HeaderMediaName = string.Empty,
                FooterText = synced.footerText,
                ButtonsJson = synced.buttonsJson,
                RejectionReason = synced.rejection,
                CreatedAtUtc = now
            });
        }

        await tenantDb.SaveChangesAsync(ct);
    }

    private static string SafeDecrypt(string value, SecretCryptoService crypto)
    {
        try { return crypto.Decrypt(value); } catch { return string.Empty; }
    }

    private static async Task<(bool ok, int statusCode, string body)> GraphGetRawAsync(HttpClient client, string url, string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await client.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        return (res.IsSuccessStatusCode, (int)res.StatusCode, body);
    }

    private static string TryGetString(JsonElement node, string name)
    {
        if (!node.TryGetProperty(name, out var child)) return string.Empty;
        return child.ValueKind == JsonValueKind.String ? (child.GetString() ?? string.Empty) : child.ToString();
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

    private static void ParseComponents(
        JsonElement row,
        out string body,
        out string headerType,
        out string headerText,
        out string footerText,
        out string buttonsJson)
    {
        body = string.Empty;
        headerType = "none";
        headerText = string.Empty;
        footerText = string.Empty;
        buttonsJson = "[]";

        if (!row.TryGetProperty("components", out var comps) || comps.ValueKind != JsonValueKind.Array)
            return;

        var buttons = new List<object>();
        foreach (var c in comps.EnumerateArray())
        {
            var type = TryGetString(c, "type").ToUpperInvariant();
            if (type == "BODY")
                body = TryGetString(c, "text");
            else if (type == "HEADER")
            {
                var fmt = TryGetString(c, "format").ToLowerInvariant();
                headerType = string.IsNullOrWhiteSpace(fmt) ? "text" : fmt;
                headerText = TryGetString(c, "text");
            }
            else if (type == "FOOTER")
                footerText = TryGetString(c, "text");
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
}
