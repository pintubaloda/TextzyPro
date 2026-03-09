using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/waba/debug")]
public class WabaDebugController(
    WhatsAppCloudService whatsapp,
    RbacService rbac,
    ControlDbContext db,
    SecretCryptoService crypto,
    IConfiguration config,
    WabaWebhookQueueService queue) : ControllerBase
{
    [HttpPost("graph-probe")]
    public async Task<IActionResult> Probe([FromBody] Dictionary<string, string> request, CancellationToken ct)
    {
        if (!rbac.HasPermission(ApiRead)) return Forbid();
        var token = request.TryGetValue("accessToken", out var v) ? v : string.Empty;
        if (string.IsNullOrWhiteSpace(token)) return BadRequest("accessToken required");

        var result = await whatsapp.DebugProbeAsync(token, ct);
        return Ok(result);
    }

    [HttpGet("tenant-probe")]
    public async Task<IActionResult> TenantProbe(CancellationToken ct)
    {
        if (!rbac.HasPermission(ApiRead)) return Forbid();
        var result = await whatsapp.DebugTenantProbeAsync(ct);
        return Ok(result);
    }

    [HttpGet("webhook-health")]
    public async Task<IActionResult> WebhookHealth(CancellationToken ct)
    {
        if (!rbac.HasPermission(ApiRead)) return Forbid();

        var rows = await db.PlatformSettings
            .Where(x => x.Scope == "waba-master")
            .ToListAsync(ct);
        var values = rows.ToDictionary(x => x.Key, x => crypto.Decrypt(x.ValueEncrypted), StringComparer.OrdinalIgnoreCase);

        var verifyToken = values.TryGetValue("verifyToken", out var vt) ? (vt ?? string.Empty).Trim() : string.Empty;
        var callbackUrl = values.TryGetValue("webhookUrl", out var wu) ? (wu ?? string.Empty).Trim() : string.Empty;
        var appSecretFromUi = values.TryGetValue("appSecret", out var sec) ? (sec ?? string.Empty).Trim() : string.Empty;
        var appSecretFromConfig = config.GetSection("WhatsApp").Get<WhatsAppOptions>()?.AppSecret ?? string.Empty;
        var appSecretConfigured = !string.IsNullOrWhiteSpace(appSecretFromUi) || !string.IsNullOrWhiteSpace(appSecretFromConfig);

        if (string.IsNullOrWhiteSpace(callbackUrl))
        {
            var apiBase = (config["PublicApiBaseUrl"] ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(apiBase))
                callbackUrl = $"{apiBase.TrimEnd('/')}/api/waba/webhook";
        }

        var now = DateTime.UtcNow;
        var since24 = now.AddHours(-24);
        var q = db.WebhookEvents.AsNoTracking().Where(x => x.Provider == "meta");
        var total24h = await q.CountAsync(x => x.ReceivedAtUtc >= since24, ct);
        var processed24h = await q.CountAsync(x => x.ReceivedAtUtc >= since24 && x.Status == "Processed", ct);
        var failed24h = await q.CountAsync(x => x.ReceivedAtUtc >= since24 && (x.Status == "DeadLetter" || x.Status == "Unmapped"), ct);
        var queuedNow = await q.CountAsync(x => x.Status == "Queued" || x.Status == "RetryScheduled" || x.Status == "Processing", ct);
        var lastEvent = await q.OrderByDescending(x => x.ReceivedAtUtc).Select(x => x.ReceivedAtUtc).FirstOrDefaultAsync(ct);

        var queueDepth = await queue.GetDepthAsync(ct);

        return Ok(new
        {
            configured = new
            {
                verifyToken = !string.IsNullOrWhiteSpace(verifyToken),
                appSecret = appSecretConfigured,
                callbackUrl = !string.IsNullOrWhiteSpace(callbackUrl),
                callbackUrlValue = callbackUrl
            },
            queue = new
            {
                provider = queue.ActiveProvider,
                depth = queueDepth
            },
            events = new
            {
                total24h,
                processed24h,
                failed24h,
                queuedNow,
                lastReceivedAtUtc = lastEvent == default ? (DateTime?)null : lastEvent
            }
        });
    }
}
