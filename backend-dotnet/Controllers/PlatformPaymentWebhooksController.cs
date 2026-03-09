using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/platform/payment-webhooks")]
public class PlatformPaymentWebhooksController(
    ControlDbContext db,
    SecretCryptoService crypto,
    AuditLogService audit,
    AuthContext auth,
    RbacService rbac,
    IConfiguration config) : ControllerBase
{
    private sealed class WebhookCfg
    {
        public string Provider { get; set; } = "razorpay";
        public string EndpointUrl { get; set; } = string.Empty;
        public string WebhookId { get; set; } = string.Empty;
        public string EventsCsv { get; set; } = "payment.captured,payment.failed";
        public bool IsAutoCreated { get; set; }
        public DateTime? LastSyncedAtUtc { get; set; }
    }

    public sealed class UpsertWebhookRequest
    {
        public string Provider { get; set; } = "razorpay";
        public string EndpointUrl { get; set; } = string.Empty;
        public string WebhookId { get; set; } = string.Empty;
        public string EventsCsv { get; set; } = "payment.captured,payment.failed";
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();
        var list = await ReadList(ct);
        return Ok(list.OrderBy(x => x.Provider));
    }

    [HttpPut]
    public async Task<IActionResult> Upsert([FromBody] UpsertWebhookRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsWrite)) return Forbid();
        string provider;
        string endpointUrl;
        try
        {
            provider = InputGuardService.RequireTrimmed(request.Provider, "Provider", 40).ToLowerInvariant();
            endpointUrl = InputGuardService.RequireTrimmed(request.EndpointUrl, "Endpoint URL", 1000);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttps && endpoint.Scheme != Uri.UriSchemeHttp))
            return BadRequest("Endpoint URL must be a valid absolute URL.");

        var list = await ReadList(ct);
        var row = list.FirstOrDefault(x => x.Provider == provider);
        if (row is null)
        {
            row = new WebhookCfg { Provider = provider };
            list.Add(row);
        }
        row.EndpointUrl = endpointUrl.Trim();
        row.WebhookId = request.WebhookId?.Trim() ?? string.Empty;
        row.EventsCsv = (request.EventsCsv?.Trim() ?? "payment.captured,payment.failed");
        if (row.EventsCsv.Length > 1000) return BadRequest("Events list is too long.");
        row.LastSyncedAtUtc = DateTime.UtcNow;

        await WriteList(list, ct);
        await audit.WriteAsync("payment.webhook.config.upsert", $"provider={provider}; endpoint={row.EndpointUrl}", ct);
        return Ok(row);
    }

    [HttpPost("auto-create")]
    public async Task<IActionResult> AutoCreate([FromBody] Dictionary<string, string> body, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsWrite)) return Forbid();
        var provider = (body.TryGetValue("provider", out var p) ? p : "razorpay").Trim().ToLowerInvariant();
        if (provider.Length > 40 || string.IsNullOrWhiteSpace(provider)) return BadRequest("Invalid provider.");

        var list = await ReadList(ct);
        var existing = list.FirstOrDefault(x => x.Provider == provider);
        if (existing is not null && !string.IsNullOrWhiteSpace(existing.EndpointUrl))
            return Ok(new { created = false, exists = true, config = existing });

        var apiBase = config["PublicApiBaseUrl"]?.Trim();
        if (string.IsNullOrWhiteSpace(apiBase))
            apiBase = $"{Request.Scheme}://{Request.Host}";
        var endpoint = $"{apiBase.TrimEnd('/')}/api/payments/webhook/{provider}";

        var row = existing ?? new WebhookCfg { Provider = provider };
        row.EndpointUrl = endpoint;
        row.EventsCsv = "payment.captured,payment.failed,refund.processed,invoice.paid";
        row.IsAutoCreated = true;
        row.LastSyncedAtUtc = DateTime.UtcNow;

        if (existing is null) list.Add(row);
        await WriteList(list, ct);
        await audit.WriteAsync("payment.webhook.auto_create", $"provider={provider}; endpoint={endpoint}", ct);
        return Ok(new { created = true, exists = false, config = row });
    }

    private async Task<List<WebhookCfg>> ReadList(CancellationToken ct)
    {
        var row = await db.PlatformSettings.FirstOrDefaultAsync(x => x.Scope == "payment-webhooks" && x.Key == "items", ct);
        if (row is null || string.IsNullOrWhiteSpace(row.ValueEncrypted)) return [];
        try
        {
            var json = crypto.Decrypt(row.ValueEncrypted);
            return JsonSerializer.Deserialize<List<WebhookCfg>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task WriteList(List<WebhookCfg> list, CancellationToken ct)
    {
        var row = await db.PlatformSettings.FirstOrDefaultAsync(x => x.Scope == "payment-webhooks" && x.Key == "items", ct);
        if (row is null)
        {
            row = new PlatformSetting
            {
                Id = Guid.NewGuid(),
                Scope = "payment-webhooks",
                Key = "items"
            };
            db.PlatformSettings.Add(row);
        }
        row.ValueEncrypted = crypto.Encrypt(JsonSerializer.Serialize(list));
        row.UpdatedByUserId = auth.UserId;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
