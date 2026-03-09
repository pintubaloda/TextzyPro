using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/sms/webhook")]
public class SmsWebhookController(
    ControlDbContext controlDb,
    SecretCryptoService crypto,
    TenantSchemaGuardService tenantSchemaGuard,
    SensitiveDataRedactor redactor,
    ILogger<SmsWebhookController> logger) : ControllerBase
{
    [HttpPost("tata")]
    [AllowAnonymous]
    public async Task<IActionResult> Tata([FromQuery] string tenantSlug = "", CancellationToken ct = default)
    {
        var settings = await controlDb.PlatformSettings.AsNoTracking()
            .Where(x => x.Scope == "sms-gateway")
            .ToDictionaryAsync(x => x.Key, x => crypto.Decrypt(x.ValueEncrypted), StringComparer.OrdinalIgnoreCase, ct);
        var expectedSecret = settings.TryGetValue("webhookSecret", out var s) ? s?.Trim() : string.Empty;
        var receivedSecret = Request.Headers["X-SMS-Webhook-Secret"].FirstOrDefault()
                             ?? Request.Query["secret"].FirstOrDefault()
                             ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(expectedSecret) && !string.Equals(expectedSecret, receivedSecret, StringComparison.Ordinal))
            return Unauthorized("Invalid webhook secret.");

        string rawBody;
        using (var reader = new StreamReader(Request.Body))
            rawBody = await reader.ReadToEndAsync(ct);

        var query = Request.Query.ToDictionary(x => x.Key, x => x.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var payload = ParsePayload(rawBody, query);
        var providerMessageId = (
            payload.TryGetValue("jobId", out var id5) ? id5 :
            payload.TryGetValue("jobid", out var id9) ? id9 :
            payload.TryGetValue("job_id", out var id6) ? id6 :
            payload.TryGetValue("msgid", out var id1) ? id1 :
            payload.TryGetValue("messageid", out var id2) ? id2 :
            payload.TryGetValue("campaignId", out var id3) ? id3 :
            payload.TryGetValue("campaign_id", out var id4) ? id4 :
            payload.TryGetValue("cusTmId", out var id7) ? id7 :
            payload.TryGetValue("custmId", out var id8) ? id8 :
            string.Empty
        )?.Trim();
        var statusRaw = (payload.TryGetValue("status", out var st1) ? st1 : payload.TryGetValue("dlrstatus", out var st2) ? st2 : string.Empty)?.Trim();
        var phone = (payload.TryGetValue("recipient", out var ph1) ? ph1 : payload.TryGetValue("mobile", out var ph2) ? ph2 : string.Empty)?.Trim();
        var reason = (payload.TryGetValue("reason", out var rs1) ? rs1 : payload.TryGetValue("error", out var rs2) ? rs2 : string.Empty)?.Trim();

        if (string.IsNullOrWhiteSpace(providerMessageId))
            return BadRequest("Missing provider message id.");

        Tenant? tenant;
        if (!string.IsNullOrWhiteSpace(tenantSlug))
        {
            tenant = await controlDb.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == tenantSlug, ct);
        }
        else
        {
            tenant = await ResolveTenantForTataWebhookAsync(providerMessageId, phone ?? string.Empty, ct);
            if (tenant is not null) tenantSlug = tenant.Slug;
        }

        if (tenant is null)
            return BadRequest("Tenant could not be resolved for webhook. Pass tenantSlug or ensure provider message id is logged.");

        await tenantSchemaGuard.EnsureContactEncryptionColumnsAsync(tenant.Id, tenant.DataConnectionString, ct);
        using var tenantDb = SeedData.CreateTenantDbContext(tenant.DataConnectionString);

        var message = await tenantDb.Messages.FirstOrDefaultAsync(
            x => x.TenantId == tenant.Id && (x.ProviderMessageId == providerMessageId || x.ProviderMessageId.EndsWith("_" + providerMessageId)), ct);
        if (message is null && !string.IsNullOrWhiteSpace(phone))
        {
            // Fallback for providers that returned an unparseable send response id:
            // map by recipient + latest pending SMS message.
            var recentFrom = DateTime.UtcNow.AddDays(-3);
            message = await tenantDb.Messages
                .Where(x => x.TenantId == tenant.Id &&
                            x.Channel == ChannelType.Sms &&
                            x.Recipient == phone &&
                            x.CreatedAtUtc >= recentFrom &&
                            x.Status != MessageStateMachine.Delivered &&
                            x.Status != MessageStateMachine.Failed)
                .OrderByDescending(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(ct);
        }
        if (message is null)
        {
            logger.LogWarning("TATA DLR ignored: message not found tenant={TenantSlug} providerMessageId={ProviderMessageId}", tenantSlug, providerMessageId);
            return Ok(new { ok = true, ignored = "message_not_found" });
        }

        var normalized = NormalizeDeliveryState(statusRaw ?? string.Empty, reason ?? string.Empty);
        var deliveryMessage = BuildDeliveryMessage(normalized, statusRaw ?? string.Empty, reason ?? string.Empty);
        if (normalized == "delivered")
            message.Status = MessageStateMachine.Delivered;
        else if (normalized == "failed")
            message.Status = MessageStateMachine.Failed;
        else if (normalized == "submitted")
            message.Status = MessageStateMachine.AcceptedByMeta;

        if (string.IsNullOrWhiteSpace(message.ProviderMessageId) ||
            !message.ProviderMessageId.EndsWith("_" + providerMessageId, StringComparison.OrdinalIgnoreCase))
        {
            message.ProviderMessageId = $"tata_{providerMessageId}";
        }

        if (!string.IsNullOrWhiteSpace(reason))
            message.LastError = redactor.RedactText(reason);

        tenantDb.MessageEvents.Add(new MessageEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            MessageId = message.Id,
            ProviderMessageId = message.ProviderMessageId,
            Direction = "outbound",
            EventType = $"sms.dlr.{normalized}",
            State = normalized,
            StatePriority = normalized == "delivered" ? 50 : normalized == "failed" ? 90 : 40,
            EventTimestampUtc = DateTime.UtcNow,
            RecipientId = phone ?? string.Empty,
            CustomerPhone = phone ?? string.Empty,
            MessageType = message.MessageType,
            RawPayloadJson = JsonSerializer.Serialize(new
            {
                provider = "tata",
                normalizedState = normalized,
                deliveryMessage,
                statusRaw,
                reason,
                payload = payload
            }),
            CreatedAtUtc = DateTime.UtcNow
        });

        var ledger = await tenantDb.SmsBillingLedgers.FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.MessageId == message.Id, ct);
        if (ledger is not null)
        {
            if (string.IsNullOrWhiteSpace(ledger.ProviderMessageId) ||
                !ledger.ProviderMessageId.EndsWith("_" + providerMessageId, StringComparison.OrdinalIgnoreCase))
                ledger.ProviderMessageId = $"tata_{providerMessageId}";
            ledger.DeliveryState = normalized;
            ledger.UpdatedAtUtc = DateTime.UtcNow;
            ledger.Notes = string.IsNullOrWhiteSpace(reason) ? deliveryMessage : $"{deliveryMessage} ({reason})";
        }

        await tenantDb.SaveChangesAsync(ct);
        return Ok(new { ok = true, state = normalized, deliveryMessage, messageId = message.Id, providerMessageId });
    }

    [HttpPost("tata/inbound")]
    [AllowAnonymous]
    public async Task<IActionResult> TataInbound([FromQuery] string tenantSlug = "", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantSlug))
            return BadRequest("tenantSlug is required.");
        var tenant = await controlDb.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == tenantSlug, ct);
        if (tenant is null) return NotFound("Tenant not found.");
        await tenantSchemaGuard.EnsureContactEncryptionColumnsAsync(tenant.Id, tenant.DataConnectionString, ct);

        string rawBody;
        using (var reader = new StreamReader(Request.Body))
            rawBody = await reader.ReadToEndAsync(ct);
        var query = Request.Query.ToDictionary(x => x.Key, x => x.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var payload = ParsePayload(rawBody, query);
        var phone = (payload.TryGetValue("recipient", out var ph1) ? ph1 : payload.TryGetValue("mobile", out var ph2) ? ph2 : string.Empty)?.Trim() ?? string.Empty;
        var text = (payload.TryGetValue("message", out var m1) ? m1 : payload.TryGetValue("msg", out var m2) ? m2 : string.Empty)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(phone))
            return BadRequest("Phone is required.");

        var normalizedText = text.ToLowerInvariant();
        var stopKeywords = new[] { "stop", "unsubscribe", "optout", "cancel", "quit", "end" };
        var matchedStop = stopKeywords.Any(k => normalizedText.Contains(k));

        using var tenantDb = SeedData.CreateTenantDbContext(tenant.DataConnectionString);
        if (matchedStop)
        {
            var existing = await tenantDb.SmsOptOuts.FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.Phone == phone, ct);
            if (existing is null)
            {
                tenantDb.SmsOptOuts.Add(new SmsOptOut
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.Id,
                    Phone = phone,
                    Reason = "STOP keyword inbound",
                    Source = "inbound_sms",
                    OptedOutAtUtc = DateTime.UtcNow,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }
            else
            {
                existing.IsActive = true;
                existing.Reason = "STOP keyword inbound";
                existing.Source = "inbound_sms";
                existing.OptedOutAtUtc = DateTime.UtcNow;
            }
            await tenantDb.SaveChangesAsync(ct);
            return Ok(new { ok = true, action = "opted_out", phone });
        }

        return Ok(new { ok = true, action = "ignored", phone });
    }

    private static Dictionary<string, string> ParsePayload(string rawBody, Dictionary<string, string> query)
    {
        var map = new Dictionary<string, string>(query, StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(rawBody))
        {
            try
            {
                using var doc = JsonDocument.Parse(rawBody);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in doc.RootElement.EnumerateObject())
                        map[p.Name] = p.Value.ToString();
                    return map;
                }
            }
            catch
            {
                // ignore and try form urlencoded parsing
            }

            if (rawBody.Contains('='))
            {
                var pairs = rawBody.Split('&', StringSplitOptions.RemoveEmptyEntries);
                foreach (var pair in pairs)
                {
                    var kv = pair.Split('=', 2);
                    var k = Uri.UnescapeDataString(kv[0] ?? string.Empty);
                    var v = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : string.Empty;
                    if (!string.IsNullOrWhiteSpace(k))
                        map[k] = v;
                }
            }
        }

        return map;
    }

    private static string NormalizeDeliveryState(string status, string reason)
    {
        var s = (status ?? string.Empty).Trim().ToLowerInvariant();
        var r = (reason ?? string.Empty).Trim().ToLowerInvariant();
        var full = $"{s} {r}";
        // Common DLR states across Indian gateways:
        // delivered/delivrd | failed/undeliv/rejectd/expired
        if (full.Contains("deliver") || full.Contains("deliv")) return "delivered";
        if (full.Contains("failed") || full.Contains("fail") || full.Contains("reject") || full.Contains("undeliv") || full.Contains("invalid") || full.Contains("expired") || full.Contains("dlt"))
            return "failed";
        if (full.Contains("submit") || full.Contains("accept") || full.Contains("sent") || full.Contains("queued") || full.Contains("process"))
            return "submitted";
        return "unknown";
    }

    private static string BuildDeliveryMessage(string normalized, string statusRaw, string reason)
    {
        var statusPart = string.IsNullOrWhiteSpace(statusRaw) ? string.Empty : $"provider={statusRaw.Trim()}";
        var reasonPart = string.IsNullOrWhiteSpace(reason) ? string.Empty : $"reason={reason.Trim()}";
        var suffix = string.Join(", ", new[] { statusPart, reasonPart }.Where(x => !string.IsNullOrWhiteSpace(x)));

        var baseText = normalized switch
        {
            "delivered" => "SMS delivered to handset.",
            "submitted" => "SMS accepted by operator and awaiting final DLR.",
            "failed" => "SMS delivery failed at operator/network stage.",
            _ => "SMS DLR received with unclassified state."
        };

        return string.IsNullOrWhiteSpace(suffix) ? baseText : $"{baseText} ({suffix})";
    }

    private async Task<Tenant?> ResolveTenantForTataWebhookAsync(string providerMessageId, string recipient, CancellationToken ct)
    {
        var candidates = await controlDb.SmsGatewayRequestLogs.AsNoTracking()
            .Where(x => x.Provider == "tata" &&
                        (x.ProviderMessageId == providerMessageId ||
                         x.ProviderMessageId.EndsWith("_" + providerMessageId)))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => x.TenantId)
            .Where(x => x.HasValue)
            .ToListAsync(ct);

        var tenantId = candidates.Select(x => x!.Value).FirstOrDefault();
        if (tenantId != Guid.Empty)
            return await controlDb.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId, ct);

        // Fallback: resolve by recipient on recent logs if unique tenant in recent window.
        if (!string.IsNullOrWhiteSpace(recipient))
        {
            var since = DateTime.UtcNow.AddHours(-6);
            var recipientTenants = await controlDb.SmsGatewayRequestLogs.AsNoTracking()
                .Where(x => x.Provider == "tata" &&
                            x.CreatedAtUtc >= since &&
                            x.Recipient == recipient &&
                            x.TenantId.HasValue)
                .Select(x => x.TenantId!.Value)
                .Distinct()
                .ToListAsync(ct);

            if (recipientTenants.Count == 1)
                return await controlDb.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == recipientTenants[0], ct);
        }

        return null;
    }
}
