using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Providers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Textzy.Api.Services;

public class OutboundMessageWorker(
    OutboundMessageQueueService queue,
    IServiceScopeFactory scopeFactory,
    IHubContext<InboxHub> hub,
    SensitiveDataRedactor redactor,
    DeliveryDebugBuffer debug,
    ILogger<OutboundMessageWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var job = await queue.DequeueAsync(stoppingToken);
            if (job is null)
            {
                await Task.Delay(150, stoppingToken);
                continue;
            }

            try
            {
                using var scope = scopeFactory.CreateScope();
                var controlDb = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
                var crypto = scope.ServiceProvider.GetRequiredService<SecretCryptoService>();
                var security = scope.ServiceProvider.GetRequiredService<SecurityControlService>();
                var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
                var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                var waOptions = configuration.GetSection("WhatsApp").Get<WhatsAppOptions>() ?? new WhatsAppOptions();
                var tenant = await controlDb.Tenants.FirstOrDefaultAsync(x => x.Id == job.TenantId, stoppingToken);
                if (tenant is null) continue;

                using var tenantDb = SeedData.CreateTenantDbContext(tenant.DataConnectionString);
                var message = await tenantDb.Messages.FirstOrDefaultAsync(x => x.Id == job.MessageId, stoppingToken);
                if (message is null) continue;
                if (message.Status is "Accepted" or "AcceptedByMeta" or "Sent" or "Delivered" or "Read") continue;
                if (await security.IsCircuitBreakerOpenAsync(job.TenantId, stoppingToken))
                {
                    message.Status = MessageStateMachine.RetryScheduled;
                    message.LastError = "circuit_breaker_active";
                    message.NextRetryAtUtc = DateTime.UtcNow.AddMinutes(5);
                    await tenantDb.SaveChangesAsync(stoppingToken);
                    continue;
                }
                if (message.NextRetryAtUtc.HasValue && message.NextRetryAtUtc.Value > DateTime.UtcNow)
                {
                    await queue.EnqueueAsync(job, stoppingToken);
                    continue;
                }

                var provider = scope.ServiceProvider.GetRequiredService<IMessageProvider>();
                var whatsapp = scope.ServiceProvider.GetRequiredService<WhatsAppCloudService>();
                message.Status = MessageStateMachine.Processing;
                tenantDb.MessageEvents.Add(MessageStateMachine.BuildEvent(tenant.Id, message.Id, message.ProviderMessageId, "outbound", "processing", MessageStateMachine.Processing));
                await tenantDb.SaveChangesAsync(stoppingToken);

                var queueLagMs = Math.Max(0, (DateTime.UtcNow - message.CreatedAtUtc).TotalMilliseconds);
                debug.Add(new DeliveryDebugSample(
                    AtUtc: DateTime.UtcNow,
                    TenantId: tenant.Id,
                    TenantSlug: tenant.Slug,
                    Kind: "outbound.dequeue",
                    CorrelationId: message.Id.ToString("N"),
                    DurationMs: queueLagMs,
                    Detail: $"channel={message.Channel}"));

                try
                {
                    var sendSw = System.Diagnostics.Stopwatch.StartNew();
                    string providerId;
                    if (message.Channel == ChannelType.WhatsApp)
                    {
                        var wabaCfg = await tenantDb.Set<TenantWabaConfig>()
                            .Where(x => x.TenantId == tenant.Id && x.IsActive)
                            .OrderByDescending(x => x.ConnectedAtUtc)
                            .FirstOrDefaultAsync(stoppingToken);
                        if (wabaCfg is null) throw new InvalidOperationException("WABA config not connected.");
                        if (string.IsNullOrWhiteSpace(wabaCfg.PhoneNumberId)) throw new InvalidOperationException("Phone number ID missing.");
                        var accessToken = UnprotectToken(wabaCfg.AccessToken, crypto);
                        if (string.IsNullOrWhiteSpace(accessToken)) throw new InvalidOperationException("WABA access token missing.");

                        var useTemplate = message.MessageType == "template";
                        if (useTemplate)
                        {
                            var name = ExtractTemplateName(message.Body);
                            var languageCode = ExtractTemplateLanguage(message.Body);
                            var bodyParams = ExtractTemplateParams(message.Body);
                            var template = await tenantDb.Templates
                                .AsNoTracking()
                                .FirstOrDefaultAsync(x =>
                                    x.TenantId == tenant.Id &&
                                    x.Channel == ChannelType.WhatsApp &&
                                    x.Name == name &&
                                    x.Language == languageCode, stoppingToken);
                            if (template is null)
                                throw new InvalidOperationException($"Template '{name}' ({languageCode}) not found.");
                            var approved = string.Equals(template.Status, "approved", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(template.LifecycleStatus, "approved", StringComparison.OrdinalIgnoreCase);
                            if (!approved)
                                throw new InvalidOperationException($"Template '{name}' is not approved.");

                            providerId = await SendWhatsAppTemplateAsync(httpClientFactory, waOptions, wabaCfg.PhoneNumberId, accessToken, message.Recipient, name, languageCode, bodyParams, stoppingToken);
                        }
                        else if (message.MessageType.StartsWith("media:", StringComparison.OrdinalIgnoreCase))
                        {
                            var mediaType = message.MessageType["media:".Length..].Trim().ToLowerInvariant();
                            var mediaPayload = ParseMediaPayload(message.Body);
                            providerId = await SendWhatsAppMediaAsync(
                                httpClientFactory,
                                waOptions,
                                wabaCfg.PhoneNumberId,
                                accessToken,
                                message.Recipient,
                                mediaType,
                                mediaPayload.mediaId,
                                mediaPayload.caption,
                                stoppingToken);
                        }
                        else if (message.MessageType.StartsWith("interactive:", StringComparison.OrdinalIgnoreCase))
                        {
                            var interactive = ParseInteractivePayload(message.MessageType, message.Body);
                            if (string.Equals(interactive.kind, "flow", StringComparison.OrdinalIgnoreCase))
                            {
                                if (string.IsNullOrWhiteSpace(interactive.flowId))
                                    throw new InvalidOperationException("Interactive flow message missing flow id.");
                                providerId = await SendWhatsAppInteractiveFlowAsync(
                                    httpClientFactory,
                                    waOptions,
                                    wabaCfg.PhoneNumberId,
                                    accessToken,
                                    message.Recipient,
                                    interactive.body,
                                    interactive.flowId,
                                    interactive.flowCta,
                                    interactive.flowToken,
                                    interactive.flowAction,
                                    interactive.flowScreen,
                                    interactive.flowMessageVersion,
                                    interactive.flowData,
                                    stoppingToken);
                            }
                            else
                            {
                                if (interactive.buttons.Count == 0)
                                    throw new InvalidOperationException("Interactive message has no buttons.");
                                providerId = await SendWhatsAppInteractiveButtonsAsync(
                                    httpClientFactory,
                                    waOptions,
                                    wabaCfg.PhoneNumberId,
                                    accessToken,
                                    message.Recipient,
                                    interactive.body,
                                    interactive.buttons,
                                    stoppingToken);
                            }
                        }
                        else
                        {
                            var window = await tenantDb.Set<ConversationWindow>()
                                .FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.Recipient == message.Recipient, stoppingToken);
                            var isOpen = window is not null && window.LastInboundAtUtc >= DateTime.UtcNow.AddHours(-24);
                            if (!isOpen)
                                throw new InvalidOperationException("24-hour WhatsApp session closed. Use template message.");
                            providerId = await SendWhatsAppSessionAsync(httpClientFactory, waOptions, wabaCfg.PhoneNumberId, accessToken, message.Recipient, message.Body, stoppingToken);
                        }
                    }
                    else
                    {
                        providerId = await provider.SendAsync(
                            message.Channel,
                            message.Recipient,
                            message.Body,
                            new SmsSendContext
                            {
                                TenantId = tenant.Id,
                                MessageType = message.MessageType
                            },
                            stoppingToken);
                    }
                    sendSw.Stop();
                    debug.Add(new DeliveryDebugSample(
                        AtUtc: DateTime.UtcNow,
                        TenantId: tenant.Id,
                        TenantSlug: tenant.Slug,
                        Kind: "outbound.send",
                        CorrelationId: message.Id.ToString("N"),
                        DurationMs: sendSw.Elapsed.TotalMilliseconds,
                        Detail: $"providerMessageId={(string.IsNullOrWhiteSpace(providerId) ? "" : providerId)}"));

                    message.ProviderMessageId = providerId;
                    message.Status = MessageStateMachine.AcceptedByMeta;
                    message.LastError = string.Empty;
                    message.NextRetryAtUtc = null;
                    if (message.Channel == ChannelType.Sms)
                    {
                        var smsLedger = await tenantDb.SmsBillingLedgers.FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.MessageId == message.Id, stoppingToken);
                        if (smsLedger is not null)
                        {
                            smsLedger.ProviderMessageId = providerId;
                            smsLedger.DeliveryState = "submitted";
                            smsLedger.UpdatedAtUtc = DateTime.UtcNow;
                            smsLedger.Notes = "Accepted by SMS provider; awaiting DLR callback.";
                        }
                    }
                    var idem = await tenantDb.IdempotencyKeys.FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.MessageId == message.Id, stoppingToken);
                    if (idem is not null)
                    {
                        idem.Status = "accepted";
                        idem.ExpiresAtUtc = DateTime.UtcNow.AddHours(24);
                    }
                    tenantDb.MessageEvents.Add(MessageStateMachine.BuildEvent(tenant.Id, message.Id, message.ProviderMessageId, "outbound", "accepted", MessageStateMachine.AcceptedByMeta));
                    await tenantDb.SaveChangesAsync(stoppingToken);
                    logger.LogInformation(
                        "Outbound accepted: messageId={MessageId} tenant={TenantSlug} channel={Channel} lagMs={LagMs:0.0} sendMs={SendMs:0.0}",
                        message.Id,
                        tenant.Slug,
                        message.Channel,
                        queueLagMs,
                        sendSw.Elapsed.TotalMilliseconds);
                    await hub.Clients.Group($"tenant:{tenant.Slug}").SendAsync("message.sent", new
                    {
                        message.Id,
                        message.Recipient,
                        message.Body,
                        message.Channel,
                        message.Status,
                        message.CreatedAtUtc
                    }, stoppingToken);
                    debug.Add(new DeliveryDebugSample(
                        AtUtc: DateTime.UtcNow,
                        TenantId: tenant.Id,
                        TenantSlug: tenant.Slug,
                        Kind: "hub.message.sent",
                        CorrelationId: message.Id.ToString("N"),
                        DurationMs: 0,
                        Detail: $"group=tenant:{tenant.Slug}"));
                }
                catch (Exception ex)
                {
                    var retryable = IsRetryable(ex, controlDb, out var reason, out var errorCode, out var errorTitle, out var errorDetail);
                    message.RetryCount += 1;
                    message.LastError = Truncate($"{reason}: {ex.Message}", 1500);

                    if (!retryable || message.RetryCount >= 5)
                    {
                        message.Status = MessageStateMachine.Failed;
                        message.NextRetryAtUtc = null;
                        if (message.Channel == ChannelType.Sms)
                        {
                            var smsLedger = await tenantDb.SmsBillingLedgers.FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.MessageId == message.Id, stoppingToken);
                            if (smsLedger is not null)
                            {
                                smsLedger.DeliveryState = "failed";
                                smsLedger.UpdatedAtUtc = DateTime.UtcNow;
                                smsLedger.Notes = Truncate($"Provider send failed before DLR: {ex.Message}", 900);
                            }
                        }
                        var idem = await tenantDb.IdempotencyKeys.FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.MessageId == message.Id, stoppingToken);
                        if (idem is not null)
                        {
                            idem.Status = "failed";
                            idem.ExpiresAtUtc = DateTime.UtcNow.AddDays(7);
                        }
                        tenantDb.MessageEvents.Add(MessageStateMachine.BuildEvent(tenant.Id, message.Id, message.ProviderMessageId, "outbound", "failed", MessageStateMachine.Failed));
                        tenantDb.OutboundDeadLetters.Add(new OutboundDeadLetter
                        {
                            Id = Guid.NewGuid(),
                            TenantId = tenant.Id,
                            MessageId = message.Id,
                            IdempotencyKey = message.IdempotencyKey,
                            AttemptCount = message.RetryCount,
                            Classification = retryable ? "exhausted" : "permanent",
                            ErrorCode = errorCode,
                            ErrorTitle = errorTitle,
                            ErrorDetail = string.IsNullOrWhiteSpace(errorDetail) ? Truncate(redactor.RedactText(ex.Message), 1500) : redactor.RedactText(errorDetail),
                            PayloadJson = JsonSerializer.Serialize(new
                            {
                                message.Id,
                                message.Recipient,
                                message.Channel,
                                message.MessageType,
                                message.Body,
                                message.ProviderMessageId
                            }),
                            CreatedAtUtc = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        message.Status = MessageStateMachine.RetryScheduled;
                        var delay = RetryDelay(message.RetryCount);
                        message.NextRetryAtUtc = DateTime.UtcNow.Add(delay);
                        tenantDb.MessageEvents.Add(MessageStateMachine.BuildEvent(tenant.Id, message.Id, message.ProviderMessageId, "outbound", "retry_scheduled", MessageStateMachine.RetryScheduled));
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(delay, stoppingToken);
                                await queue.EnqueueAsync(job, stoppingToken);
                            }
                            catch { }
                        }, stoppingToken);
                    }

                    await tenantDb.SaveChangesAsync(stoppingToken);
                    logger.LogWarning("Outbound send failed for message {MessageId}. retryable={Retryable}; reason={Reason}; error={Error}", message.Id, retryable, reason, redactor.RedactText(ex.Message));
                }
            }
            catch (Exception ex)
            {
                logger.LogError("Outbound worker failed for tenant {TenantId} message {MessageId}: {Error}", job.TenantId, job.MessageId, redactor.RedactText(ex.Message));
            }
        }
    }

    private static bool IsRetryable(Exception ex, ControlDbContext controlDb, out string reason, out string errorCode, out string errorTitle, out string errorDetail)
    {
        var msg = (ex.Message ?? string.Empty).ToLowerInvariant();
        errorCode = ExtractErrorCode(ex.Message);
        errorTitle = string.Empty;
        errorDetail = string.Empty;
        var retryableCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "1", "2", "4", "131000" };
        var permanentCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "190", "200", "131026", "131005", "132000" };
        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            var code = errorCode;
            var policy = controlDb.WabaErrorPolicies.AsNoTracking().FirstOrDefault(x => x.Code == code && x.IsActive);
            if (policy is not null)
            {
                reason = $"policy_{policy.Classification.ToLowerInvariant()}";
                return string.Equals(policy.Classification, "retryable", StringComparison.OrdinalIgnoreCase);
            }
            if (retryableCodes.Contains(errorCode))
            {
                reason = "retryable_meta_code";
                return true;
            }
            if (permanentCodes.Contains(errorCode))
            {
                reason = "permanent_meta_code";
                return false;
            }
        }

        if (msg.Contains("invalid oauth") || msg.Contains("(401)") || msg.Contains("(403)") || msg.Contains("permission") || msg.Contains("policy") || msg.Contains("session closed") || msg.Contains("recipient not valid") || msg.Contains("template") && msg.Contains("not approved") || msg.Contains("dlt") && (msg.Contains("reject") || msg.Contains("invalid") || msg.Contains("mismatch")))
        {
            reason = "non_retryable_auth_or_policy";
            return false;
        }
        if (msg.Contains("(429)") || msg.Contains("timeout") || msg.Contains("(500)") || msg.Contains("(502)") || msg.Contains("(503)") || msg.Contains("(504)"))
        {
            reason = "retryable_transient";
            return true;
        }

        reason = "non_retryable_unknown";
        return false;
    }

    private static string ExtractErrorCode(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return string.Empty;
        var pattern = "\"code\"\\s*:\\s*(\\d+)";
        var m = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value;

        var fallback = Regex.Match(message, @"\b(190|200|131026|131000|131005|132000)\b");
        return fallback.Success ? fallback.Groups[1].Value : string.Empty;
    }

    private static TimeSpan RetryDelay(int retryCount)
    {
        return retryCount switch
        {
            1 => TimeSpan.FromSeconds(1),
            2 => TimeSpan.FromSeconds(5),
            3 => TimeSpan.FromSeconds(30),
            4 => TimeSpan.FromMinutes(2),
            _ => TimeSpan.FromMinutes(10)
        };
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
    private static string UnprotectToken(string token, SecretCryptoService crypto)
    {
        if (string.IsNullOrWhiteSpace(token)) return string.Empty;
        if (!token.StartsWith("enc:", StringComparison.Ordinal)) return token;
        try { return crypto.Decrypt(token[4..]); } catch { return string.Empty; }
    }

    private static string ExtractTemplateName(string payload)
    {
        var split = (payload ?? string.Empty).Split('|', 3);
        return split.Length > 0 && !string.IsNullOrWhiteSpace(split[0]) ? split[0] : "welcome_customer";
    }

    private static List<string> ExtractTemplateParams(string payload)
    {
        var split = (payload ?? string.Empty).Split('|', 3);
        if (split.Length < 2 || string.IsNullOrWhiteSpace(split[1])) return [];
        return split[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static string ExtractTemplateLanguage(string payload)
    {
        var split = (payload ?? string.Empty).Split('|', 3);
        if (split.Length < 3 || string.IsNullOrWhiteSpace(split[2])) return "en";
        return split[2].Trim().ToLowerInvariant();
    }

    private static (string mediaId, string caption) ParseMediaPayload(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload ?? "{}");
            var root = doc.RootElement;
            var mediaId = root.TryGetProperty("mediaId", out var mid) ? (mid.GetString() ?? string.Empty) : string.Empty;
            var caption = root.TryGetProperty("caption", out var cap) ? (cap.GetString() ?? string.Empty) : string.Empty;
            return (mediaId, caption);
        }
        catch
        {
            return (string.Empty, string.Empty);
        }
    }

    private static (string kind, string body, List<string> buttons, string flowId, string flowCta, string flowToken, string flowAction, string flowScreen, int flowMessageVersion, JsonElement? flowData) ParseInteractivePayload(string messageType, string body)
    {
        var kind = "button";
        var flowId = string.Empty;
        var flowCta = "Open";
        var flowToken = string.Empty;
        var flowAction = "navigate";
        var flowScreen = string.Empty;
        var flowMessageVersion = 3;
        JsonElement? flowData = null;
        var buttons = new List<string>();
        try
        {
            // Format button: interactive:button:Support~Sales~Accounts
            // Format flow: interactive:flow:<flowId>
            var parts = (messageType ?? string.Empty).Split(':', 3, StringSplitOptions.TrimEntries);
            kind = parts.Length >= 2 ? parts[1].Trim().ToLowerInvariant() : "button";
            if (kind == "flow")
            {
                flowId = parts.Length >= 3 ? (parts[2] ?? string.Empty).Trim() : string.Empty;
                if (!string.IsNullOrWhiteSpace(body))
                {
                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("body", out var txt))
                        body = txt.GetString() ?? string.Empty;
                    if (root.TryGetProperty("flowId", out var fid) && !string.IsNullOrWhiteSpace(fid.GetString()))
                        flowId = fid.GetString() ?? flowId;
                    if (root.TryGetProperty("flowCta", out var cta))
                        flowCta = string.IsNullOrWhiteSpace(cta.GetString()) ? flowCta : cta.GetString()!;
                    if (root.TryGetProperty("flowToken", out var token))
                        flowToken = token.GetString() ?? string.Empty;
                    if (root.TryGetProperty("flowAction", out var action))
                        flowAction = string.IsNullOrWhiteSpace(action.GetString()) ? flowAction : action.GetString()!;
                    if (root.TryGetProperty("flowScreen", out var screen))
                        flowScreen = screen.GetString() ?? string.Empty;
                    if (root.TryGetProperty("flowMessageVersion", out var mv) && mv.ValueKind == JsonValueKind.Number && mv.TryGetInt32(out var parsed))
                        flowMessageVersion = parsed;
                    if (root.TryGetProperty("flowData", out var data))
                        flowData = data.Clone();
                }
            }
            else if (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2]))
            {
                buttons = parts[2]
                        .Split('~', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(3)
                        .ToList();
            }
        }
        catch
        {
            buttons = [];
        }
        return (
            string.IsNullOrWhiteSpace(kind) ? "button" : kind,
            body ?? string.Empty,
            buttons,
            flowId,
            flowCta,
            flowToken,
            flowAction,
            flowScreen,
            flowMessageVersion is < 1 or > 9 ? 3 : flowMessageVersion,
            flowData
        );
    }

    private static async Task<string> SendWhatsAppSessionAsync(
        IHttpClientFactory httpClientFactory,
        WhatsAppOptions options,
        string phoneNumberId,
        string accessToken,
        string recipient,
        string body,
        CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient();
        var url = $"{options.GraphApiBase}/{options.ApiVersion}/{phoneNumberId}/messages";
        var payload = JsonSerializer.Serialize(new { messaging_product = "whatsapp", to = recipient, type = "text", text = new { body } });
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        var resp = await client.SendAsync(req, ct);
        var responseBody = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"WhatsApp send failed ({(int)resp.StatusCode}): {responseBody}");
        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement.GetProperty("messages")[0].GetProperty("id").GetString() ?? $"wa_{Guid.NewGuid():N}";
    }

    private static async Task<string> SendWhatsAppInteractiveButtonsAsync(
        IHttpClientFactory httpClientFactory,
        WhatsAppOptions options,
        string phoneNumberId,
        string accessToken,
        string recipient,
        string body,
        IReadOnlyList<string> buttons,
        CancellationToken ct)
    {
        var url = $"{options.GraphApiBase}/{options.ApiVersion}/{phoneNumberId}/messages";
        var interactiveButtons = buttons
            .Select((x, i) => new
            {
                type = "reply",
                reply = new
                {
                    id = BuildInteractiveReplyId(i + 1, x),
                    title = x.Length > 20 ? x[..20] : x
                }
            })
            .ToArray();

        var payload = JsonSerializer.Serialize(new
        {
            messaging_product = "whatsapp",
            to = recipient,
            type = "interactive",
            interactive = new
            {
                type = "button",
                body = new { text = string.IsNullOrWhiteSpace(body) ? "Please choose an option:" : body },
                action = new { buttons = interactiveButtons }
            }
        });
        var http = httpClientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var resp = await http.SendAsync(req, ct);
        var responseBody = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"WhatsApp interactive send failed ({(int)resp.StatusCode}): {responseBody}");
        using var doc = JsonDocument.Parse(responseBody);
        var id = doc.RootElement.GetProperty("messages")[0].GetProperty("id").GetString();
        return id ?? string.Empty;
    }

    private static async Task<string> SendWhatsAppInteractiveFlowAsync(
        IHttpClientFactory httpClientFactory,
        WhatsAppOptions options,
        string phoneNumberId,
        string accessToken,
        string recipient,
        string body,
        string flowId,
        string flowCta,
        string flowToken,
        string flowAction,
        string flowScreen,
        int flowMessageVersion,
        JsonElement? flowData,
        CancellationToken ct)
    {
        var url = $"{options.GraphApiBase}/{options.ApiVersion}/{phoneNumberId}/messages";
        object? flowActionPayload = null;
        var hasScreen = !string.IsNullOrWhiteSpace(flowScreen);
        var hasData = flowData.HasValue && flowData.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
        if (hasScreen || hasData)
        {
            flowActionPayload = new
            {
                screen = hasScreen ? flowScreen : null,
                data = hasData ? flowData!.Value : (JsonElement?)null
            };
        }

        var payload = JsonSerializer.Serialize(new
        {
            messaging_product = "whatsapp",
            to = recipient,
            type = "interactive",
            interactive = new
            {
                type = "flow",
                body = new { text = string.IsNullOrWhiteSpace(body) ? "Please continue:" : body },
                action = new
                {
                    name = "flow",
                    parameters = new
                    {
                        flow_message_version = flowMessageVersion.ToString(),
                        flow_id = flowId,
                        flow_cta = string.IsNullOrWhiteSpace(flowCta) ? "Open" : flowCta,
                        flow_token = string.IsNullOrWhiteSpace(flowToken) ? null : flowToken,
                        flow_action = string.IsNullOrWhiteSpace(flowAction) ? "navigate" : flowAction,
                        flow_action_payload = flowActionPayload
                    }
                }
            }
        });
        var http = httpClientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var resp = await http.SendAsync(req, ct);
        var responseBody = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"WhatsApp interactive flow send failed ({(int)resp.StatusCode}): {responseBody}");
        using var doc = JsonDocument.Parse(responseBody);
        var id = doc.RootElement.GetProperty("messages")[0].GetProperty("id").GetString();
        return id ?? string.Empty;
    }

    private static string BuildInteractiveReplyId(int index, string title)
    {
        var raw = Regex.Replace((title ?? string.Empty).ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
        if (string.IsNullOrWhiteSpace(raw)) raw = $"opt{index}";
        var value = $"btn_{index}_{raw}";
        return value.Length <= 24 ? value : value[..24].TrimEnd('_');
    }

    private static async Task<string> SendWhatsAppTemplateAsync(
        IHttpClientFactory httpClientFactory,
        WhatsAppOptions options,
        string phoneNumberId,
        string accessToken,
        string recipient,
        string templateName,
        string languageCode,
        List<string> bodyParameters,
        CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient();
        var url = $"{options.GraphApiBase}/{options.ApiVersion}/{phoneNumberId}/messages";
        var components = new[] { new { type = "body", parameters = bodyParameters.Select(x => new { type = "text", text = x }).ToArray() } };
        var payload = JsonSerializer.Serialize(new { messaging_product = "whatsapp", to = recipient, type = "template", template = new { name = templateName, language = new { code = languageCode }, components } });
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        var resp = await client.SendAsync(req, ct);
        var responseBody = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"WhatsApp template send failed ({(int)resp.StatusCode}): {responseBody}");
        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement.GetProperty("messages")[0].GetProperty("id").GetString() ?? $"wa_tpl_{Guid.NewGuid():N}";
    }

    private static async Task<string> SendWhatsAppMediaAsync(
        IHttpClientFactory httpClientFactory,
        WhatsAppOptions options,
        string phoneNumberId,
        string accessToken,
        string recipient,
        string mediaType,
        string mediaId,
        string caption,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mediaId)) throw new InvalidOperationException("Media id is required.");
        if (mediaType is not ("image" or "video" or "audio" or "document"))
            throw new InvalidOperationException($"Unsupported media type '{mediaType}'.");

        var client = httpClientFactory.CreateClient();
        var url = $"{options.GraphApiBase}/{options.ApiVersion}/{phoneNumberId}/messages";
        object mediaObj = mediaType switch
        {
            "audio" => new { id = mediaId },
            _ => new { id = mediaId, caption = caption ?? string.Empty }
        };
        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["messaging_product"] = "whatsapp",
            ["to"] = recipient,
            ["type"] = mediaType,
            [mediaType] = mediaObj
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        var resp = await client.SendAsync(req, ct);
        var responseBody = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"WhatsApp media send failed ({(int)resp.StatusCode}): {responseBody}");
        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement.GetProperty("messages")[0].GetProperty("id").GetString() ?? $"wa_media_{Guid.NewGuid():N}";
    }
}
