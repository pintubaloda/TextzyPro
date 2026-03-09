using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;
using System.Security.Cryptography;
using System.Text;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/waba/webhook")]
public class WabaWebhookController(
    WhatsAppCloudService whatsapp,
    WabaWebhookQueueService queue,
    ControlDbContext controlDb,
    SecretCryptoService crypto,
    IConfiguration config,
    SensitiveDataRedactor redactor,
    ILogger<WabaWebhookController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Verify(
        [FromQuery(Name = "hub.mode")] string mode,
        [FromQuery(Name = "hub.verify_token")] string verifyToken,
        [FromQuery(Name = "hub.challenge")] string challenge,
        CancellationToken ct)
    {
        var expectedFromConfig = config.GetSection("WhatsApp").Get<WhatsAppOptions>()?.VerifyToken ?? string.Empty;
        var expectedFromPlatform = string.Empty;
        try
        {
            var row = await controlDb.PlatformSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Scope == "waba-master" && x.Key == "verifyToken", ct);
            if (row is not null && !string.IsNullOrWhiteSpace(row.ValueEncrypted))
                expectedFromPlatform = crypto.Decrypt(row.ValueEncrypted);
        }
        catch
        {
            // Keep webhook verification resilient even if settings read fails.
        }

        var verified = mode == "subscribe" &&
                       (!string.IsNullOrWhiteSpace(verifyToken)) &&
                       (
                           string.Equals(verifyToken, expectedFromPlatform, StringComparison.Ordinal) ||
                           string.Equals(verifyToken, expectedFromConfig, StringComparison.Ordinal)
                       );

        if (verified) return Content(challenge);
        return Forbid();
    }

    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync(ct);

        var sig = Request.Headers["X-Hub-Signature-256"].ToString();
        if (!await whatsapp.VerifyWebhookSignatureAsync(rawBody, sig, ct))
        {
            logger.LogWarning("WABA webhook signature validation failed. signatureHash={SignatureHash}", ComputeEventKey(sig));
            controlDb.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = null,
                ActorUserId = Guid.Empty,
                Action = "waba.webhook.rejected",
                Details = "reason=signature_invalid",
                CreatedAtUtc = DateTime.UtcNow
            });
            await controlDb.SaveChangesAsync(ct);
            // Always ACK to provider to avoid redelivery storms; event is rejected internally.
            return Ok(new { ok = true, queued = false, rejected = "invalid_signature" });
        }

        var replayKeys = ExtractReplayKeys(rawBody);
        if (replayKeys.Count == 0)
            replayKeys.Add($"hash:{ComputeEventKey(rawBody)}");

        var now = DateTime.UtcNow;
        var duplicateCount = 0;
        foreach (var key in replayKeys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var existing = await controlDb.WebhookReplayGuards
                .FirstOrDefaultAsync(x => x.Provider == "meta" && x.ReplayKey == key, ct);
            if (existing is not null && existing.ExpiresAtUtc > now)
            {
                duplicateCount++;
                continue;
            }

            if (existing is null)
            {
                controlDb.WebhookReplayGuards.Add(new WebhookReplayGuard
                {
                    Id = Guid.NewGuid(),
                    Provider = "meta",
                    ReplayKey = key,
                    FirstSeenAtUtc = now,
                    ExpiresAtUtc = now.AddHours(48)
                });
            }
            else
            {
                existing.FirstSeenAtUtc = now;
                existing.ExpiresAtUtc = now.AddHours(48);
            }
        }
        await controlDb.SaveChangesAsync(ct);
        if (duplicateCount == replayKeys.Count)
        {
            controlDb.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = null,
                ActorUserId = Guid.Empty,
                Action = "waba.webhook.duplicate_replay",
                Details = $"count={duplicateCount}",
                CreatedAtUtc = now
            });
            await controlDb.SaveChangesAsync(ct);
            return Ok(new { ok = true, queued = false, duplicate = true });
        }

        try
        {
            await queue.EnqueueAsync(new WabaWebhookQueueItem
            {
                Provider = "meta",
                EventKey = replayKeys.First(),
                RawBody = rawBody,
                ReceivedAtUtc = DateTime.UtcNow
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogError("WABA webhook enqueue failure: {Error}", redactor.RedactText(ex.Message));
            controlDb.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = null,
                ActorUserId = Guid.Empty,
                Action = "waba.webhook.enqueue_failed",
                Details = $"reason={ex.GetType().Name}; detail={redactor.RedactText(ex.Message)}",
                CreatedAtUtc = DateTime.UtcNow
            });
            await controlDb.SaveChangesAsync(ct);
            return Ok(new { ok = true, queued = false });
        }

        return Ok(new { ok = true, queued = true });
    }

    private static string ComputeEventKey(string rawBody)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawBody ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static List<string> ExtractReplayKeys(string rawBody)
    {
        var keys = new List<string>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            if (!root.TryGetProperty("entry", out var entry) || entry.ValueKind != System.Text.Json.JsonValueKind.Array) return keys;
            foreach (var e in entry.EnumerateArray())
            {
                if (!e.TryGetProperty("changes", out var changes) || changes.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
                foreach (var ch in changes.EnumerateArray())
                {
                    if (!ch.TryGetProperty("value", out var value)) continue;
                    var phone = value.TryGetProperty("metadata", out var metadata) && metadata.TryGetProperty("phone_number_id", out var p) ? p.GetString() ?? "" : "";
                    if (value.TryGetProperty("messages", out var messages) && messages.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var m in messages.EnumerateArray())
                        {
                            var id = m.TryGetProperty("id", out var mid) ? mid.GetString() ?? "" : "";
                            if (!string.IsNullOrWhiteSpace(id)) keys.Add($"msg:{phone}:{id}");
                        }
                    }
                    if (value.TryGetProperty("statuses", out var statuses) && statuses.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var s in statuses.EnumerateArray())
                        {
                            var id = s.TryGetProperty("id", out var sid) ? sid.GetString() ?? "" : "";
                            var st = s.TryGetProperty("status", out var ss) ? ss.GetString() ?? "" : "";
                            var ts = s.TryGetProperty("timestamp", out var tsEl) ? tsEl.GetString() ?? "" : "";
                            if (!string.IsNullOrWhiteSpace(id)) keys.Add($"st:{phone}:{id}:{st}:{ts}");
                        }
                    }
                }
            }
        }
        catch
        {
            // noop
        }

        return keys;
    }
}
