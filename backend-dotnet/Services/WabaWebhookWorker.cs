using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Textzy.Api.Data;
using Textzy.Api.DTOs;
using Textzy.Api.Models;
using WebPush;

namespace Textzy.Api.Services;

public class WabaWebhookWorker(
    WabaWebhookQueueService queue,
    IServiceScopeFactory scopeFactory,
    IHubContext<InboxHub> hub,
    UserPresenceService presence,
    IConfiguration config,
    SecretCryptoService crypto,
    TenantSchemaGuardService schemaGuard,
    SensitiveDataRedactor redactor,
    ILogger<WabaWebhookWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PresenceTtl = TimeSpan.FromMinutes(2);
    private readonly SemaphoreSlim _fcmTokenLock = new(1, 1);
    private string _fcmAccessToken = string.Empty;
    private DateTime _fcmAccessTokenExpiryUtc = DateTime.MinValue;
    private sealed class TriggerInboundContext
    {
        public string MessageId { get; init; } = string.Empty;
        public string From { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string MessageText { get; init; } = string.Empty;
        public string MatchKey { get; init; } = string.Empty;
        public string ContextMessageId { get; init; } = string.Empty;
        public bool IsInteractiveReply { get; init; }
    }

    private sealed class FlowNode
    {
        public string Id { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public string Next { get; set; } = string.Empty;
        public string OnTrue { get; set; } = string.Empty;
        public string OnFalse { get; set; } = string.Empty;
        public string OnSuccess { get; init; } = string.Empty;
        public string OnFailure { get; init; } = string.Empty;
        public Dictionary<string, object?> Config { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class InboundItem
    {
        public string MessageId { get; init; } = string.Empty;
        public string From { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Body { get; init; } = string.Empty;
        public string MessageType { get; init; } = string.Empty;
        public DateTime? AtUtc { get; init; }
        public string MediaId { get; init; } = string.Empty;
        public string MediaMimeType { get; init; } = string.Empty;
        public string MediaSha256 { get; init; } = string.Empty;
        public string MediaCaption { get; init; } = string.Empty;
        public string MediaFileName { get; init; } = string.Empty;
        public string ButtonPayload { get; init; } = string.Empty;
        public string ButtonText { get; init; } = string.Empty;
        public string InteractiveType { get; init; } = string.Empty;
        public string ListReplyId { get; init; } = string.Empty;
        public string ListReplyTitle { get; init; } = string.Empty;
        public string LocationSummary { get; init; } = string.Empty;
        public string ContextMessageId { get; init; } = string.Empty;
        public string ReferralSourceUrl { get; init; } = string.Empty;
        public string ReferralHeadline { get; init; } = string.Empty;
        public string ReactionEmoji { get; init; } = string.Empty;
        public string ReactionMessageId { get; init; } = string.Empty;
        public string OrderSummary { get; init; } = string.Empty;
        public string ContactsSummary { get; init; } = string.Empty;
        public string UnsupportedSummary { get; init; } = string.Empty;
        public string FlowEventType { get; init; } = string.Empty;
        public string FlowId { get; init; } = string.Empty;
        public string FlowToken { get; init; } = string.Empty;
        public string FlowScreenId { get; init; } = string.Empty;
        public string FlowPayloadJson { get; init; } = "{}";
        public string RawJson { get; init; } = "{}";
    }

    private sealed class StatusItem
    {
        public string MessageId { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public DateTime? AtUtc { get; init; }
        public string RecipientId { get; init; } = string.Empty;
        public string ConversationId { get; init; } = string.Empty;
        public string ConversationOriginType { get; init; } = string.Empty;
        public DateTime? ConversationExpirationUtc { get; init; }
        public bool? PricingBillable { get; init; }
        public string PricingCategory { get; init; } = string.Empty;
        public string ErrorCode { get; init; } = string.Empty;
        public string ErrorTitle { get; init; } = string.Empty;
        public string ErrorDetail { get; init; } = string.Empty;
        public string RawJson { get; init; } = "{}";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in queue.ReadAllAsync(stoppingToken))
        {
            using var scope = scopeFactory.CreateScope();
                var controlDb = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
                var tenantResolver = scope.ServiceProvider.GetRequiredService<WabaTenantResolver>();
                var contactPii = scope.ServiceProvider.GetRequiredService<ContactPiiService>();
                var eventRow = await controlDb.WebhookEvents
                .FirstOrDefaultAsync(x => x.Provider == item.Provider && x.EventKey == item.EventKey, stoppingToken);

            if (eventRow is not null && eventRow.Status == "Processed")
            {
                continue;
            }

            if (eventRow is null)
            {
                eventRow = new WebhookEvent
                {
                    Id = item.Id,
                    Provider = item.Provider,
                    EventKey = item.EventKey,
                    PayloadJson = item.RawBody,
                    Status = "Queued",
                    RetryCount = Math.Max(item.Attempt - 1, 0),
                    MaxRetries = item.MaxAttempts,
                    ReceivedAtUtc = item.ReceivedAtUtc
                };
                controlDb.WebhookEvents.Add(eventRow);
                await controlDb.SaveChangesAsync(stoppingToken);
            }
            else
            {
                eventRow.PayloadJson = item.RawBody;
                eventRow.RetryCount = Math.Max(item.Attempt - 1, eventRow.RetryCount);
                eventRow.MaxRetries = item.MaxAttempts;
                eventRow.Status = "Processing";
                eventRow.LastError = string.Empty;
                await controlDb.SaveChangesAsync(stoppingToken);
            }

            try
            {
                var parse = ParsePayload(item.RawBody);
                if (!parse.Ok)
                {
                    eventRow.Status = "DeadLetter";
                    eventRow.LastError = $"parse_failed:{parse.Error}";
                    eventRow.DeadLetteredAtUtc = DateTime.UtcNow;
                    await controlDb.SaveChangesAsync(stoppingToken);
                    controlDb.AuditLogs.Add(new AuditLog
                    {
                        Id = Guid.NewGuid(),
                        TenantId = null,
                        ActorUserId = Guid.Empty,
                        Action = "waba.webhook.dead_letter",
                        Details = $"queueId={item.Id}; attempt={item.Attempt}; reason=parse_failed:{parse.Error}",
                        CreatedAtUtc = DateTime.UtcNow
                    });
                    await controlDb.SaveChangesAsync(stoppingToken);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(parse.PhoneNumberId) || (parse.Inbound.Count == 0 && parse.Statuses.Count == 0))
                {
                    eventRow.Status = "Ignored";
                    eventRow.LastError = "missing_phone_or_events";
                    eventRow.ProcessedAtUtc = DateTime.UtcNow;
                    await controlDb.SaveChangesAsync(stoppingToken);
                    controlDb.AuditLogs.Add(new AuditLog
                    {
                        Id = Guid.NewGuid(),
                        TenantId = null,
                        ActorUserId = Guid.Empty,
                        Action = "waba.webhook.ignored",
                        Details = $"queueId={item.Id}; attempt={item.Attempt}; reason=missing_phone_or_events",
                        CreatedAtUtc = DateTime.UtcNow
                    });
                    await controlDb.SaveChangesAsync(stoppingToken);
                    continue;
                }

                var resolved = await tenantResolver.ResolveByPhoneNumberIdAsync(parse.PhoneNumberId, stoppingToken);
                if (resolved is null)
                {
                    eventRow.Status = "Unmapped";
                    eventRow.PhoneNumberId = parse.PhoneNumberId;
                    eventRow.LastError = "tenant_not_mapped";
                    eventRow.ProcessedAtUtc = DateTime.UtcNow;
                    await controlDb.SaveChangesAsync(stoppingToken);
                    controlDb.AuditLogs.Add(new AuditLog
                    {
                        Id = Guid.NewGuid(),
                        TenantId = null,
                        ActorUserId = Guid.Empty,
                        Action = "waba.webhook.unmapped",
                        Details = $"provider=meta; queueId={item.Id}; attempt={item.Attempt}; phoneNumberId={parse.PhoneNumberId}",
                        CreatedAtUtc = DateTime.UtcNow
                    });                    
                    await controlDb.SaveChangesAsync(stoppingToken);
                    continue;
                }

                await schemaGuard.EnsureContactEncryptionColumnsAsync(resolved.TenantId, resolved.DataConnectionString, stoppingToken);
                using var tenantDb = SeedData.CreateTenantDbContext(resolved.DataConnectionString);
                var cfg = await tenantDb.Set<TenantWabaConfig>()
                    .FirstOrDefaultAsync(x => x.TenantId == resolved.TenantId && x.IsActive && x.PhoneNumberId == parse.PhoneNumberId, stoppingToken);
                if (cfg is null)
                {
                    await tenantResolver.InvalidateAsync(parse.PhoneNumberId, stoppingToken);
                    throw new InvalidOperationException("cached_mapping_stale");
                }

                cfg.WebhookVerifiedAtUtc = DateTime.UtcNow;
                if (cfg.WebhookSubscribedAtUtc.HasValue && cfg.PermissionAuditPassed)
                    cfg.OnboardingState = "ready";
                eventRow.TenantId = resolved.TenantId;
                eventRow.PhoneNumberId = parse.PhoneNumberId;

                foreach (var status in parse.Statuses)
                {
                    var statusName = (status.Status ?? string.Empty).Trim();
                    var statusRecipientId = status.RecipientId ?? string.Empty;
                    var statusConversationId = status.ConversationId ?? string.Empty;
                    var statusConversationOriginType = status.ConversationOriginType ?? string.Empty;
                    var normalizedStatus = MessageStateMachine.NormalizeWebhookStatus(statusName);
                    var msg = await tenantDb.Set<Message>()
                        .FirstOrDefaultAsync(x => x.TenantId == resolved.TenantId && x.ProviderMessageId == status.MessageId, stoppingToken);
                    if (msg is not null)
                    {
                        ApplyStatusTransition(msg, status, controlDb);
                        if (!string.IsNullOrWhiteSpace(msg.MessageType) &&
                            msg.MessageType.StartsWith("interactive:flow:", StringComparison.OrdinalIgnoreCase))
                        {
                            var metaFlowId = msg.MessageType["interactive:flow:".Length..];
                            var flowEventType = normalizedStatus switch
                            {
                                "FailedPermanent" or "FailedRetryable" => "flow.error",
                                "Delivered" => "flow.delivered",
                                "Read" => "flow.read",
                                _ => "flow.status"
                            };
                            tenantDb.FlowRuntimeEvents.Add(new FlowRuntimeEvent
                            {
                                Id = Guid.NewGuid(),
                                TenantId = resolved.TenantId,
                                FlowId = null,
                                MetaFlowId = metaFlowId,
                                ConversationExternalId = statusConversationId,
                                CustomerPhone = statusRecipientId,
                                EventType = flowEventType,
                                EventSource = "status_webhook",
                                Success = !(normalizedStatus is "FailedPermanent" or "FailedRetryable"),
                                StatusCode = 200,
                                DurationMs = 0,
                                ScreenId = string.Empty,
                                ActionName = statusName,
                                PayloadJson = status.RawJson,
                                ErrorDetail = $"{status.ErrorCode} {status.ErrorTitle} {status.ErrorDetail}".Trim(),
                                CreatedAtUtc = status.AtUtc ?? DateTime.UtcNow
                            });
                        }
                    }
                    tenantDb.MessageEvents.Add(new MessageEvent
                    {
                        Id = Guid.NewGuid(),
                        TenantId = resolved.TenantId,
                        MessageId = msg?.Id,
                        ProviderMessageId = status.MessageId,
                        Direction = "outbound",
                        EventType = $"status.{statusName.ToLowerInvariant()}",
                        State = normalizedStatus,
                        StatePriority = MessageStateMachine.Priority(normalizedStatus),
                        EventTimestampUtc = status.AtUtc ?? DateTime.UtcNow,
                        RecipientId = statusRecipientId,
                        CustomerPhone = statusRecipientId,
                        ConversationId = statusConversationId,
                        ConversationOriginType = statusConversationOriginType,
                        ConversationExpirationUtc = status.ConversationExpirationUtc,
                        PricingBillable = status.PricingBillable,
                        PricingCategory = status.PricingCategory,
                        MessageType = msg?.MessageType ?? "session",
                        RawPayloadJson = status.RawJson,
                        CreatedAtUtc = DateTime.UtcNow
                    });
                }

                foreach (var inbound in parse.Inbound)
                {
                    var inboundFrom = inbound.From ?? string.Empty;
                    var inboundName = inbound.Name ?? string.Empty;
                    var inboundMessageType = inbound.MessageType ?? string.Empty;
                    var inboundInteractiveType = inbound.InteractiveType ?? string.Empty;
                    var inboundListReplyId = inbound.ListReplyId ?? string.Empty;
                    var inboundListReplyTitle = inbound.ListReplyTitle ?? string.Empty;
                    var inboundButtonPayload = inbound.ButtonPayload ?? string.Empty;
                    var inboundButtonText = inbound.ButtonText ?? string.Empty;
                    var inboundContextMessageId = inbound.ContextMessageId ?? string.Empty;
                    TriggerInboundContext? triggerInbound = null;
                    var window = await tenantDb.Set<ConversationWindow>()
                        .FirstOrDefaultAsync(x => x.TenantId == resolved.TenantId && x.Recipient == inboundFrom, stoppingToken);
                    if (window is null)
                    {
                        window = new ConversationWindow
                        {
                            Id = Guid.NewGuid(),
                            TenantId = resolved.TenantId,
                            Recipient = inboundFrom,
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

                    var convo = await tenantDb.Set<Conversation>()
                        .FirstOrDefaultAsync(x => x.TenantId == resolved.TenantId && x.CustomerPhone == inboundFrom, stoppingToken);
                    if (convo is null)
                    {
                        convo = new Conversation
                        {
                            Id = Guid.NewGuid(),
                            TenantId = resolved.TenantId,
                            CustomerPhone = inboundFrom,
                            CustomerName = string.IsNullOrWhiteSpace(inboundName) ? inboundFrom : inboundName,
                            Status = "Open",
                            LastMessageAtUtc = DateTime.UtcNow,
                            CreatedAtUtc = DateTime.UtcNow
                        };
                        tenantDb.Set<Conversation>().Add(convo);
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(inboundName)) convo.CustomerName = inboundName;
                        convo.Status = "Open";
                        convo.LastMessageAtUtc = DateTime.UtcNow;
                    }

                    var inboundPhoneHash = contactPii.IsEnabled ? contactPii.ComputePhoneHash(inboundFrom) : string.Empty;
                    var existingContact = contactPii.IsEnabled
                        ? await tenantDb.Set<Contact>()
                            .FirstOrDefaultAsync(x => x.TenantId == resolved.TenantId && x.PhoneHash == inboundPhoneHash, stoppingToken)
                        : await tenantDb.Set<Contact>()
                            .FirstOrDefaultAsync(x => x.TenantId == resolved.TenantId && x.Phone == inboundFrom, stoppingToken);
                    if (existingContact is null)
                    {
                        var defaultSegment = await tenantDb.Set<ContactSegment>()
                            .FirstOrDefaultAsync(x => x.TenantId == resolved.TenantId && x.Name.ToLower() == "new", stoppingToken);
                        if (defaultSegment is null)
                        {
                            defaultSegment = new ContactSegment
                            {
                                Id = Guid.NewGuid(),
                                TenantId = resolved.TenantId,
                                Name = "New",
                                RuleJson = "{}",
                                CreatedAtUtc = DateTime.UtcNow
                            };
                            tenantDb.Set<ContactSegment>().Add(defaultSegment);
                        }

                        var newContact = new Contact
                        {
                            Id = Guid.NewGuid(),
                            TenantId = resolved.TenantId,
                            Name = string.IsNullOrWhiteSpace(inboundName) ? inboundFrom : inboundName,
                            Phone = inboundFrom,
                            SegmentId = defaultSegment.Id,
                            TagsCsv = "New",
                            OptInStatus = "opted_in",
                            CreatedAtUtc = DateTime.UtcNow
                        };
                        contactPii.Protect(newContact);
                        tenantDb.Set<Contact>().Add(newContact);
                    }
                    else if (!string.IsNullOrWhiteSpace(inboundName))
                    {
                        existingContact.Name = inboundName;
                        contactPii.Protect(existingContact);
                    }

                    var inboundProviderId = string.IsNullOrWhiteSpace(inbound.MessageId) ? $"wa_in_{Guid.NewGuid():N}" : inbound.MessageId;
                    var existingInbound = await tenantDb.Set<Message>()
                        .FirstOrDefaultAsync(x => x.TenantId == resolved.TenantId && x.ProviderMessageId == inboundProviderId, stoppingToken);
                    if (existingInbound is null)
                    {
                        var resolvedInboundMessageType = IsMediaType(inboundMessageType) ? $"media:{inboundMessageType}" : "session";
                        var normalizedInboundBody = IsMediaType(inboundMessageType)
                            ? ComposeInboundMediaBody(inbound)
                            : ComposeInboundBody(inbound);
                        if (!string.IsNullOrWhiteSpace(inboundContextMessageId))
                        {
                            var replyPrefix = await BuildInboundReplyPrefixAsync(
                                tenantDb,
                                resolved.TenantId,
                                inboundContextMessageId,
                                string.IsNullOrWhiteSpace(inboundName) ? "Customer" : inboundName,
                                stoppingToken);
                            if (!string.IsNullOrWhiteSpace(replyPrefix) && !IsMediaType(inboundMessageType))
                            {
                                normalizedInboundBody = $"{replyPrefix}\n{normalizedInboundBody}".Trim();
                            }
                        }
                        tenantDb.Set<Message>().Add(new Message
                        {
                            Id = Guid.NewGuid(),
                            TenantId = resolved.TenantId,
                            Channel = ChannelType.WhatsApp,
                            Recipient = inboundFrom,
                            Body = normalizedInboundBody,
                            MessageType = resolvedInboundMessageType,
                            Status = "Received",
                            ProviderMessageId = inboundProviderId,
                            CreatedAtUtc = inbound.AtUtc ?? DateTime.UtcNow
                        });
                        triggerInbound = new TriggerInboundContext
                        {
                            MessageId = inboundProviderId,
                            From = inboundFrom,
                            Name = inboundName,
                            MessageText = ComposeInboundBody(inbound),
                            MatchKey = !string.IsNullOrWhiteSpace(inboundButtonPayload)
                                ? inboundButtonPayload
                                : !string.IsNullOrWhiteSpace(inboundListReplyId)
                                    ? inboundListReplyId
                                    : !string.IsNullOrWhiteSpace(inboundButtonText)
                                        ? inboundButtonText
                                        : inboundListReplyTitle,
                            ContextMessageId = inboundContextMessageId,
                            IsInteractiveReply =
                                !string.IsNullOrWhiteSpace(inboundButtonText) ||
                                !string.IsNullOrWhiteSpace(inboundListReplyTitle) ||
                                string.Equals(inboundInteractiveType, "button_reply", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(inboundInteractiveType, "list_reply", StringComparison.OrdinalIgnoreCase)
                        };
                    }
                    tenantDb.MessageEvents.Add(new MessageEvent
                    {
                        Id = Guid.NewGuid(),
                        TenantId = resolved.TenantId,
                        MessageId = existingInbound?.Id,
                        ProviderMessageId = inboundProviderId,
                        Direction = "inbound",
                        EventType = "received",
                        State = MessageStateMachine.Received,
                        StatePriority = MessageStateMachine.Priority(MessageStateMachine.Received),
                        EventTimestampUtc = inbound.AtUtc ?? DateTime.UtcNow,
                        RecipientId = inboundFrom,
                        CustomerPhone = inboundFrom,
                        MessageType = string.IsNullOrWhiteSpace(inboundMessageType) ? "text" : inboundMessageType,
                        MediaId = inbound.MediaId,
                        MediaMimeType = inbound.MediaMimeType,
                        MediaSha256 = inbound.MediaSha256,
                        ButtonPayload = inboundButtonPayload,
                        ButtonText = inboundButtonText,
                        InteractiveType = inboundInteractiveType,
                        ListReplyId = inboundListReplyId,
                        ListReplyTitle = inboundListReplyTitle,
                        RawPayloadJson = inbound.RawJson,
                        CreatedAtUtc = DateTime.UtcNow
                    });

                    if (!string.IsNullOrWhiteSpace(inbound.FlowEventType))
                    {
                        tenantDb.FlowRuntimeEvents.Add(new FlowRuntimeEvent
                        {
                            Id = Guid.NewGuid(),
                            TenantId = resolved.TenantId,
                            FlowId = null,
                            MetaFlowId = inbound.FlowId ?? string.Empty,
                            ConversationExternalId = convo.Id.ToString("N"),
                            CustomerPhone = inboundFrom,
                            EventType = inbound.FlowEventType,
                            EventSource = "webhook",
                            Success = true,
                            StatusCode = 200,
                            DurationMs = 0,
                            ScreenId = inbound.FlowScreenId ?? string.Empty,
                            ActionName = inbound.InteractiveType ?? string.Empty,
                            PayloadJson = string.IsNullOrWhiteSpace(inbound.FlowPayloadJson) ? "{}" : inbound.FlowPayloadJson,
                            ErrorDetail = string.Empty,
                            CreatedAtUtc = inbound.AtUtc ?? DateTime.UtcNow
                        });
                    }

                    if (triggerInbound is not null)
                    {
                        await RunTriggeredAutomationsAsync(
                            scope.ServiceProvider,
                            controlDb,
                            tenantDb,
                            resolved.TenantId,
                            resolved.TenantSlug,
                            resolved.DataConnectionString,
                            parse.PhoneNumberId,
                            triggerInbound,
                            stoppingToken);
                    }
                }

                await tenantDb.SaveChangesAsync(stoppingToken);
                eventRow.Status = "Processed";
                eventRow.ProcessedAtUtc = DateTime.UtcNow;
                eventRow.LastError = string.Empty;
                await controlDb.SaveChangesAsync(stoppingToken);
                controlDb.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    TenantId = resolved.TenantId,
                    ActorUserId = Guid.Empty,
                    Action = "waba.webhook.processed",
                    Details = $"provider=meta; queueId={item.Id}; attempt={item.Attempt}; inboundCount={parse.Inbound.Count}; statusCount={parse.Statuses.Count}",
                    CreatedAtUtc = DateTime.UtcNow
                });
                await controlDb.SaveChangesAsync(stoppingToken);

                await hub.Clients.Group($"tenant:{resolved.TenantSlug}")
                    .SendAsync("webhook.inbound", new { phoneNumberId = parse.PhoneNumberId, inboundCount = parse.Inbound.Count, statusCount = parse.Statuses.Count }, stoppingToken);

                if (parse.Inbound.Count > 0)
                {
                    var latestInbound = parse.Inbound
                        .OrderByDescending(x => x.AtUtc ?? DateTime.MinValue)
                        .FirstOrDefault();
                    if (latestInbound is not null)
                    {
                        await TrySendBrowserPushAsync(
                            controlDb,
                            resolved.TenantId,
                            resolved.TenantSlug,
                            latestInbound,
                            stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError("WABA webhook worker failed for queue item {QueueId} attempt={Attempt}: {Error}", item.Id, item.Attempt, redactor.RedactText(ex.Message));
                if (item.Attempt < item.MaxAttempts)
                {
                    eventRow.Status = "RetryScheduled";
                    eventRow.RetryCount = item.Attempt;
                    eventRow.LastError = redactor.RedactText(ex.GetType().Name);
                    await controlDb.SaveChangesAsync(stoppingToken);
                    controlDb.AuditLogs.Add(new AuditLog
                    {
                        Id = Guid.NewGuid(),
                        TenantId = null,
                        ActorUserId = Guid.Empty,
                        Action = "waba.webhook.retry",
                        Details = $"queueId={item.Id}; attempt={item.Attempt}; nextAttempt={item.Attempt + 1}; error={ex.GetType().Name}",
                        CreatedAtUtc = DateTime.UtcNow
                    });
                    await controlDb.SaveChangesAsync(stoppingToken);
                    await queue.EnqueueAsync(new WabaWebhookQueueItem
                    {
                        Id = item.Id,
                        Provider = item.Provider,
                        EventKey = item.EventKey,
                        RawBody = item.RawBody,
                        ReceivedAtUtc = item.ReceivedAtUtc,
                        Attempt = item.Attempt + 1,
                        MaxAttempts = item.MaxAttempts
                    }, stoppingToken);
                }
                else
                {
                    eventRow.Status = "DeadLetter";
                    eventRow.RetryCount = item.Attempt;
                    eventRow.LastError = redactor.RedactText($"{ex.GetType().Name}: {ex.Message}");
                    eventRow.DeadLetteredAtUtc = DateTime.UtcNow;
                    await controlDb.SaveChangesAsync(stoppingToken);
                    controlDb.AuditLogs.Add(new AuditLog
                    {
                        Id = Guid.NewGuid(),
                        TenantId = null,
                        ActorUserId = Guid.Empty,
                        Action = "waba.webhook.dead_letter",
                        Details = $"queueId={item.Id}; attempts={item.Attempt}; error={redactor.RedactText(ex.GetType().Name)}; message={redactor.RedactText(ex.Message)}",
                        CreatedAtUtc = DateTime.UtcNow
                    });
                    await controlDb.SaveChangesAsync(stoppingToken);
                }
            }
        }
    }

    private async Task TrySendBrowserPushAsync(
        ControlDbContext controlDb,
        Guid tenantId,
        string tenantSlug,
        InboundItem inbound,
        CancellationToken ct)
    {
        var pushSettings = await controlDb.PlatformSettings
            .Where(x => x.Scope == "mobile-app" &&
                (x.Key == "webPushPublicKey" || x.Key == "webPushPrivateKey" || x.Key == "webPushSubject"))
            .ToListAsync(ct);
        var pushValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in pushSettings)
        {
            pushValues[row.Key] = crypto.Decrypt(row.ValueEncrypted);
        }

        var vapidPublicKey = (pushValues.TryGetValue("webPushPublicKey", out var dbPublicKey) ? dbPublicKey : config["Push:VapidPublicKey"] ?? string.Empty).Trim();
        var vapidPrivateKey = (pushValues.TryGetValue("webPushPrivateKey", out var dbPrivateKey) ? dbPrivateKey : config["Push:VapidPrivateKey"] ?? string.Empty).Trim();
        var vapidSubject = (pushValues.TryGetValue("webPushSubject", out var dbSubject) ? dbSubject : config["Push:VapidSubject"] ?? "mailto:support@textzy.local").Trim();
        var fcmProjectId = (config["Push:FcmProjectId"] ?? string.Empty).Trim();
        var fcmSaJson = (config["Push:FcmServiceAccountJson"] ?? string.Empty).Trim();
        var fcmSaPath = (config["Push:FcmServiceAccountPath"] ?? string.Empty).Trim();
        var webPushEnabled = !string.IsNullOrWhiteSpace(vapidPublicKey) && !string.IsNullOrWhiteSpace(vapidPrivateKey);
        var fcmEnabled = !string.IsNullOrWhiteSpace(fcmProjectId) && (!string.IsNullOrWhiteSpace(fcmSaJson) || !string.IsNullOrWhiteSpace(fcmSaPath));
        if (!webPushEnabled && !fcmEnabled) return;

        var tenantUsers = await controlDb.TenantUsers
            .Where(x => x.TenantId == tenantId)
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(ct);
        if (tenantUsers.Count == 0) return;

        var users = await controlDb.Users
            .Where(x => tenantUsers.Contains(x.Id))
            .Select(x => new { x.Id, x.Email })
            .ToListAsync(ct);
        if (users.Count == 0) return;

        var activeMap = BuildPresenceMap(tenantSlug);
        var targetUserIds = users
            .Where(u => !IsUserActive(activeMap, u.Email))
            .Select(u => u.Id)
            .ToHashSet();
        if (targetUserIds.Count == 0) return;

        var subscriptions = await controlDb.UserPushSubscriptions
            .Where(x => x.TenantId == tenantId && x.IsActive && targetUserIds.Contains(x.UserId))
            .ToListAsync(ct);
        if (subscriptions.Count == 0) return;

        var bodyText = string.IsNullOrWhiteSpace(inbound.Body)
            ? ComposeInboundBody(inbound)
            : inbound.Body;
        if (bodyText.Length > 120) bodyText = $"{bodyText[..117]}...";

        var payload = JsonSerializer.Serialize(new
        {
            title = inbound.Name,
            body = bodyText,
            tag = $"textzy-inbox-{tenantId:N}",
            data = new
            {
                tenantSlug,
                from = inbound.From,
                messageId = inbound.MessageId,
                at = (inbound.AtUtc ?? DateTime.UtcNow).ToString("O")
            }
        });

        var webPushClient = webPushEnabled ? new WebPushClient() : null;
        var vapid = webPushEnabled ? new VapidDetails(vapidSubject, vapidPublicKey, vapidPrivateKey) : null;
        var http = fcmEnabled ? new HttpClient() : null;
        foreach (var sub in subscriptions)
        {
            try
            {
                if (string.Equals(sub.Provider, "fcm", StringComparison.OrdinalIgnoreCase))
                {
                    if (http is null) continue;
                    var bearer = await GetFcmBearerTokenAsync(fcmSaJson, fcmSaPath, ct);
                    if (string.IsNullOrWhiteSpace(bearer)) continue;
                    using var req = new HttpRequestMessage(HttpMethod.Post, $"https://fcm.googleapis.com/v1/projects/{fcmProjectId}/messages:send");
                    req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearer}");
                    req.Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        message = new
                        {
                            token = sub.Endpoint,
                            notification = new { title = inbound.Name, body = bodyText },
                            data = new
                            {
                                tenantSlug,
                                from = inbound.From,
                                messageId = inbound.MessageId,
                                at = (inbound.AtUtc ?? DateTime.UtcNow).ToString("O")
                            }
                        }
                    }));
                    req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                    using var resp = await http.SendAsync(req, ct);
                    if (!resp.IsSuccessStatusCode)
                    {
                        var txt = await resp.Content.ReadAsStringAsync(ct);
                        if (txt.Contains("UNREGISTERED", StringComparison.OrdinalIgnoreCase) || txt.Contains("registration-token-not-registered", StringComparison.OrdinalIgnoreCase) || txt.Contains("INVALID_ARGUMENT", StringComparison.OrdinalIgnoreCase))
                        {
                            sub.IsActive = false;
                        }
                        else
                        {
                            logger.LogWarning("FCM send failed tenant={TenantId} user={UserId} status={Status}: {Error}",
                                tenantId, sub.UserId, (int)resp.StatusCode, redactor.RedactText(txt));
                        }
                    }
                }
                else
                {
                    if (!webPushEnabled || webPushClient is null || vapid is null) continue;
                    var pushSub = new PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
                    await webPushClient.SendNotificationAsync(pushSub, payload, vapid);
                }
                sub.LastSeenAtUtc = DateTime.UtcNow;
                sub.UpdatedAtUtc = DateTime.UtcNow;
            }
            catch (WebPushException ex) when ((int?)ex.StatusCode is 404 or 410)
            {
                sub.IsActive = false;
                sub.UpdatedAtUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Push send failed tenant={TenantId} user={UserId}: {Error}",
                    tenantId, sub.UserId, redactor.RedactText(ex.Message));
            }
        }

        await controlDb.SaveChangesAsync(ct);
    }

    private async Task<string> GetFcmBearerTokenAsync(string serviceAccountJson, string serviceAccountPath, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_fcmAccessToken) && DateTime.UtcNow < _fcmAccessTokenExpiryUtc.AddMinutes(-2))
            return _fcmAccessToken;

        await _fcmTokenLock.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrWhiteSpace(_fcmAccessToken) && DateTime.UtcNow < _fcmAccessTokenExpiryUtc.AddMinutes(-2))
                return _fcmAccessToken;

            GoogleCredential cred;
            if (!string.IsNullOrWhiteSpace(serviceAccountJson))
            {
                cred = GoogleCredential.FromJson(serviceAccountJson);
            }
            else if (!string.IsNullOrWhiteSpace(serviceAccountPath))
            {
                cred = GoogleCredential.FromFile(serviceAccountPath);
            }
            else
            {
                return string.Empty;
            }

            cred = cred.CreateScoped("https://www.googleapis.com/auth/firebase.messaging");
            var token = await cred.UnderlyingCredential.GetAccessTokenForRequestAsync(null, ct);
            if (string.IsNullOrWhiteSpace(token)) return string.Empty;
            _fcmAccessToken = token;
            _fcmAccessTokenExpiryUtc = DateTime.UtcNow.AddMinutes(50);
            return _fcmAccessToken;
        }
        catch (Exception ex)
        {
            logger.LogWarning("FCM access token fetch failed: {Error}", redactor.RedactText(ex.Message));
            return string.Empty;
        }
        finally
        {
            _fcmTokenLock.Release();
        }
    }

    private Dictionary<string, bool> BuildPresenceMap(string tenantSlug)
    {
        var now = DateTime.UtcNow;
        return presence.Snapshot(tenantSlug)
            .Where(x => !string.IsNullOrWhiteSpace(x.UserKey))
            .GroupBy(x => x.UserKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Any(x => x.IsOnline && x.IsTabActive && now - x.LastHeartbeatUtc <= PresenceTtl),
                StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsUserActive(Dictionary<string, bool> activeMap, string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        return activeMap.TryGetValue(email.Trim().ToLowerInvariant(), out var isActive) && isActive;
    }

    private static string NormalizeMatchText(string value)
    {
        var s = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var chars = s.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray();
        return new string(chars).Trim();
    }

    private static bool MatchByMode(string text, string keyword, string mode)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(keyword)) return false;
        var t = NormalizeMatchText(text);
        var k = NormalizeMatchText(keyword);
        if (string.IsNullOrWhiteSpace(t) || string.IsNullOrWhiteSpace(k)) return false;
        return mode switch
        {
            "exact" => string.Equals(t, k, StringComparison.OrdinalIgnoreCase),
            "starts" or "starts_with" => t.StartsWith(k, StringComparison.OrdinalIgnoreCase),
            _ => t.Contains(k, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static (bool Matched, string Reason) EvaluateTriggerMatch(AutomationFlow flow, string inboundText, string? definitionJson = null)
    {
        var triggerType = (flow.TriggerType ?? string.Empty).Trim().ToLowerInvariant();
        if (triggerType is not ("keyword" or "intent")) return (false, "unsupported_trigger_type");
        var text = (inboundText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text)) return (false, "empty_inbound_text");

        static bool TryMatchFromJson(string json, string inbound, out bool matched, out string reason)
        {
            matched = false;
            reason = "no_match";
            using var cfgDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            var root = cfgDoc.RootElement;
            var mode = root.TryGetProperty("match", out var modeNode)
                ? (modeNode.GetString() ?? string.Empty).Trim().ToLowerInvariant()
                : "contains";

            if (root.TryGetProperty("keywords", out var keywordsNode) && keywordsNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var k in keywordsNode.EnumerateArray())
                {
                    if (MatchByMode(inbound, k.ToString() ?? string.Empty, mode))
                    {
                        matched = true;
                        reason = $"matched_keywords_array:{mode}";
                        return true;
                    }
                }
            }
            if (root.TryGetProperty("keywords", out var keywordsCsvNode) && keywordsCsvNode.ValueKind == JsonValueKind.String)
            {
                foreach (var raw in (keywordsCsvNode.GetString() ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (MatchByMode(inbound, raw, mode))
                    {
                        matched = true;
                        reason = $"matched_keywords_csv:{mode}";
                        return true;
                    }
                }
            }
            if (root.TryGetProperty("keyword", out var keywordNode))
            {
                if (MatchByMode(inbound, keywordNode.ToString() ?? string.Empty, mode))
                {
                    matched = true;
                    reason = $"matched_keyword:{mode}";
                    return true;
                }
            }
            if (root.TryGetProperty("triggerKeywords", out var triggerKeywordsNode) && triggerKeywordsNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var k in triggerKeywordsNode.EnumerateArray())
                {
                    if (MatchByMode(inbound, k.ToString() ?? string.Empty, mode))
                    {
                        matched = true;
                        reason = $"matched_trigger_keywords:{mode}";
                        return true;
                    }
                }
            }
            reason = $"no_keyword_match:{mode}";
            return true;
        }

        try
        {
            if (TryMatchFromJson(flow.TriggerConfigJson, text, out var byFlow, out var flowReason) && byFlow)
                return (true, flowReason);

            if (!string.IsNullOrWhiteSpace(definitionJson))
            {
                using var defDoc = JsonDocument.Parse(definitionJson);
                var root = defDoc.RootElement;
                if (root.TryGetProperty("trigger", out var triggerNode))
                {
                    if (TryMatchFromJson(triggerNode.GetRawText(), text, out var byDef, out var defReason) && byDef)
                        return (true, $"fallback_definition:{defReason}");
                    return (false, $"no_match_in_definition:{defReason}");
                }
                return (false, $"no_match_in_flow_config:{flowReason}");
            }
            return (false, $"no_match_in_flow_config:{flowReason}");
        }
        catch
        {
            return (false, "trigger_parse_error");
        }
    }

    private static Dictionary<string, object?> BuildPayload(TriggerInboundContext inbound)
    {
        var normalizedMessage = NormalizeInboundMessageText(inbound.MessageText);
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["recipient"] = inbound.From,
            ["name"] = string.IsNullOrWhiteSpace(inbound.Name) ? inbound.From : inbound.Name,
            ["message"] = normalizedMessage,
            ["message_raw"] = inbound.MessageText,
            ["message_key"] = inbound.MatchKey,
            ["inbound_message_id"] = inbound.MessageId
        };
    }

    private static string NormalizeInboundMessageText(string? input)
    {
        var value = (input ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (value.StartsWith("Button reply:", StringComparison.OrdinalIgnoreCase))
            return value["Button reply:".Length..].Trim();
        if (value.StartsWith("Interactive reply:", StringComparison.OrdinalIgnoreCase))
            return value["Interactive reply:".Length..].Trim();
        return value;
    }

    private static (Guid? FlowId, string SourceNodeId) TryResolveFlowResumeFromIdempotencyKey(string? idempotencyKey)
    {
        var key = (idempotencyKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key)) return (null, string.Empty);
        // auto-msg:{flowId}:{inboundMessageId}:{nodeId}
        if (!key.StartsWith("auto-msg:", StringComparison.OrdinalIgnoreCase)) return (null, string.Empty);
        var parts = key.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return (null, string.Empty);
        var flowId = Guid.TryParse(parts[1], out var parsedFlowId) ? parsedFlowId : (Guid?)null;
        var sourceNodeId = parts.Length >= 4 ? parts[3] : string.Empty;
        return (flowId, sourceNodeId);
    }

    private static string ResolveResumeNodeId(
        IReadOnlyDictionary<string, FlowNode> nodes,
        string startNodeId,
        bool isInteractiveReply)
    {
        if (!isInteractiveReply) return startNodeId;
        if (string.IsNullOrWhiteSpace(startNodeId) || !nodes.ContainsKey(startNodeId)) return startNodeId;
        var cursor = startNodeId;
        var guard = 0;
        while (!string.IsNullOrWhiteSpace(cursor) && guard < 64)
        {
            guard++;
            if (!nodes.TryGetValue(cursor, out var node)) break;
            var nodeType = (node.Type ?? string.Empty).Trim().ToLowerInvariant().Replace("-", "_");
            if (nodeType is "condition" or "split") return node.Id;
            if (string.IsNullOrWhiteSpace(node.Next)) break;
            cursor = node.Next;
        }
        return startNodeId;
    }

    private static (Dictionary<string, FlowNode> nodes, string startNodeId) ParseFlowDefinition(string definitionJson)
    {
        var nodes = new Dictionary<string, FlowNode>(StringComparer.OrdinalIgnoreCase);
        var startNodeId = string.Empty;
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(definitionJson) ? "{}" : definitionJson);
        var root = doc.RootElement;
        if (root.TryGetProperty("startNodeId", out var startNode)) startNodeId = startNode.ToString();
        if (!root.TryGetProperty("nodes", out var nodesNode) || nodesNode.ValueKind != JsonValueKind.Array)
            return (nodes, startNodeId);

        foreach (var item in nodesNode.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idNode) ? idNode.ToString() : Guid.NewGuid().ToString("N");
            var type = item.TryGetProperty("type", out var typeNode) ? typeNode.ToString() : "text";
            var node = new FlowNode
            {
                Id = id,
                Type = type,
                Next = item.TryGetProperty("next", out var nextNode) ? nextNode.ToString() : string.Empty,
                OnTrue = item.TryGetProperty("onTrue", out var onTrueNode) ? onTrueNode.ToString() : string.Empty,
                OnFalse = item.TryGetProperty("onFalse", out var onFalseNode) ? onFalseNode.ToString() : string.Empty,
                OnSuccess = item.TryGetProperty("onSuccess", out var onSuccessNode) ? onSuccessNode.ToString() : string.Empty,
                OnFailure = item.TryGetProperty("onFailure", out var onFailureNode) ? onFailureNode.ToString() : string.Empty,
                Config = item.TryGetProperty("config", out var cfgNode) && cfgNode.ValueKind == JsonValueKind.Object
                    ? cfgNode.EnumerateObject().ToDictionary(x => x.Name, x => ToClrValue(x.Value), StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            };
            nodes[id] = node;
            if (string.IsNullOrWhiteSpace(startNodeId) && string.Equals(type, "start", StringComparison.OrdinalIgnoreCase))
                startNodeId = id;
        }
        if (root.TryGetProperty("edges", out var edgesNode) && edgesNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var edge in edgesNode.EnumerateArray())
            {
                var from = edge.TryGetProperty("from", out var fromNode) ? fromNode.ToString() : string.Empty;
                var to = edge.TryGetProperty("to", out var toNode) ? toNode.ToString() : string.Empty;
                if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) continue;
                if (!nodes.TryGetValue(from, out var source)) continue;
                var label = edge.TryGetProperty("label", out var labelNode)
                    ? (labelNode.ToString() ?? string.Empty).Trim().ToLowerInvariant()
                    : string.Empty;
                if (label is "true")
                {
                    if (string.IsNullOrWhiteSpace(source.OnTrue)) source.OnTrue = to;
                    continue;
                }
                if (label is "false")
                {
                    if (string.IsNullOrWhiteSpace(source.OnFalse)) source.OnFalse = to;
                    continue;
                }
                if (string.IsNullOrWhiteSpace(source.Next)) source.Next = to;
            }
        }
        if (string.IsNullOrWhiteSpace(startNodeId) && nodes.Count > 0) startNodeId = nodes.Keys.First();
        return (nodes, startNodeId);
    }

    private static object? ToClrValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(x => x.Name, x => ToClrValue(x.Value), StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => element.EnumerateArray().Select(ToClrValue).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l :
                                    element.TryGetDecimal(out var d) ? d :
                                    element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }

    private static bool EvaluateCondition(Dictionary<string, object?> config, Dictionary<string, object?> payload)
    {
        var field = config.TryGetValue("field", out var f) ? f?.ToString() ?? string.Empty : string.Empty;
        var @operator = config.TryGetValue("operator", out var op) ? op?.ToString()?.ToLowerInvariant() ?? "equals" : "equals";
        var expected = config.TryGetValue("value", out var v) ? v?.ToString() ?? string.Empty : string.Empty;
        var actual = payload.TryGetValue(field, out var a) ? a?.ToString() ?? string.Empty : string.Empty;
        var normalizedActual = string.Equals(field, "message", StringComparison.OrdinalIgnoreCase)
            ? NormalizeInboundMessageText(actual)
            : actual;

        return @operator switch
        {
            "contains" => normalizedActual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            "starts_with" => normalizedActual.StartsWith(expected, StringComparison.OrdinalIgnoreCase),
            "ends_with" => normalizedActual.EndsWith(expected, StringComparison.OrdinalIgnoreCase),
            "not_equals" => !string.Equals(normalizedActual, expected, StringComparison.OrdinalIgnoreCase),
            "regex" => System.Text.RegularExpressions.Regex.IsMatch(normalizedActual, expected),
            _ => string.Equals(normalizedActual, expected, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static string Interpolate(string text, Dictionary<string, object?> payload)
    {
        var output = text ?? string.Empty;
        foreach (var pair in payload)
        {
            output = output.Replace($"{{{{{pair.Key}}}}}", pair.Value?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        return output;
    }

    private static string ResolveValue(Dictionary<string, object?> config, Dictionary<string, object?> payload, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (config.TryGetValue(key, out var raw))
            {
                var val = raw?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(val)) return Interpolate(val, payload);
            }
            if (payload.TryGetValue(key, out var fromPayload))
            {
                var val = fromPayload?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }
        }
        return string.Empty;
    }

    private static string ResolveNodeReplyText(string nodeType, Dictionary<string, object?> config, Dictionary<string, object?> payload)
    {
        static List<string> normalizeOptions(object? raw)
        {
            if (raw is null) return [];
            if (raw is IEnumerable<object?> objList)
            {
                var list = new List<string>();
                foreach (var item in objList)
                {
                    if (item is IDictionary<string, object?> map)
                    {
                        var title = map.TryGetValue("title", out var t) ? t?.ToString() : null;
                        var subtitle = map.TryGetValue("subtitle", out var s) ? s?.ToString() : null;
                        var merged = string.IsNullOrWhiteSpace(subtitle) ? title : $"{title} - {subtitle}";
                        if (!string.IsNullOrWhiteSpace(merged)) list.Add(merged.Trim());
                        continue;
                    }
                    var v = item?.ToString()?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(v)) list.Add(v);
                }
                return list;
            }
            if (raw is JsonElement j)
            {
                try
                {
                    if (j.ValueKind == JsonValueKind.Array)
                    {
                        var list = new List<string>();
                        foreach (var it in j.EnumerateArray())
                        {
                            var v = (it.ValueKind == JsonValueKind.Object && it.TryGetProperty("title", out var title))
                                ? title.ToString()
                                : it.ToString();
                            if (!string.IsNullOrWhiteSpace(v)) list.Add(v.Trim());
                        }
                        return list;
                    }
                }
                catch
                {
                    return [];
                }
            }
            return [];
        }

        static string appendOptions(string baseText, IReadOnlyList<string> options)
        {
            if (options.Count == 0) return baseText;
            var lines = string.Join("\n", options.Select((x, i) => $"{i + 1}. {x}"));
            return string.IsNullOrWhiteSpace(baseText) ? lines : $"{baseText}\n\n{lines}";
        }

        if (nodeType == "bot_reply")
        {
            var replyMode = ResolveValue(config, payload, "replyMode");
            if (string.Equals(replyMode, "media", StringComparison.OrdinalIgnoreCase))
            {
                var mediaText = ResolveValue(config, payload, "mediaText", "body", "message");
                if (!string.IsNullOrWhiteSpace(mediaText)) return mediaText;
            }
            var simpleText = ResolveValue(config, payload, "simpleText", "body", "message", "question", "prompt");
            if (!string.IsNullOrWhiteSpace(simpleText))
            {
                var advancedType = ResolveValue(config, payload, "advancedType");
                var options = new List<string>();
                if (string.Equals(replyMode, "advanced", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(advancedType, "list", StringComparison.OrdinalIgnoreCase)
                        && config.TryGetValue("listItems", out var listItems))
                    {
                        options = normalizeOptions(listItems);
                    }
                    else if (config.TryGetValue("buttons", out var buttons))
                    {
                        options = normalizeOptions(buttons);
                    }
                }
                return appendOptions(simpleText, options);
            }
        }

        if (nodeType is "buttons" or "list" or "cta_url" or "media")
        {
            var body = ResolveValue(config, payload, "body", "message", "question", "prompt");
            if (!string.IsNullOrWhiteSpace(body))
            {
                if (nodeType is "buttons" && config.TryGetValue("buttons", out var buttons))
                    return appendOptions(body, normalizeOptions(buttons));
                if (nodeType is "list" && config.TryGetValue("listItems", out var listItems))
                    return appendOptions(body, normalizeOptions(listItems));
                return body;
            }
        }

        return ResolveValue(config, payload, "body", "message", "question", "prompt");
    }

    private static List<string> ReadStringOptions(object? raw, int max = 3)
    {
        var result = new List<string>();
        if (raw is null) return result;
        if (raw is IEnumerable<object?> objList)
        {
            foreach (var item in objList)
            {
                if (result.Count >= max) break;
                if (item is IDictionary<string, object?> map)
                {
                    var title = map.TryGetValue("title", out var t) ? t?.ToString() : null;
                    var subtitle = map.TryGetValue("subtitle", out var s) ? s?.ToString() : null;
                    var merged = string.IsNullOrWhiteSpace(subtitle) ? title : $"{title} - {subtitle}";
                    if (!string.IsNullOrWhiteSpace(merged)) result.Add(merged.Trim());
                    continue;
                }
                var v = item?.ToString()?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(v)) result.Add(v);
            }
            return result.Distinct(StringComparer.OrdinalIgnoreCase).Take(max).ToList();
        }
        if (raw is JsonElement j)
        {
            try
            {
                if (j.ValueKind == JsonValueKind.Array)
                {
                    foreach (var it in j.EnumerateArray())
                    {
                        if (result.Count >= max) break;
                        var v = (it.ValueKind == JsonValueKind.Object && it.TryGetProperty("title", out var title))
                            ? title.ToString()
                            : it.ToString();
                        if (!string.IsNullOrWhiteSpace(v)) result.Add(v.Trim());
                    }
                    return result.Distinct(StringComparer.OrdinalIgnoreCase).Take(max).ToList();
                }
            }
            catch
            {
                return result;
            }
        }
        return result;
    }

    private async Task RunTriggeredAutomationsAsync(
        IServiceProvider sp,
        ControlDbContext controlDb,
        TenantDbContext tenantDb,
        Guid tenantId,
        string tenantSlug,
        string tenantConnectionString,
        string phoneNumberId,
        TriggerInboundContext inbound,
        CancellationToken ct)
    {
        if (await TryAutoHandoffAsync(sp, controlDb, tenantDb, tenantId, phoneNumberId, inbound, ct))
            return;

        var flows = await tenantDb.AutomationFlows
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive && (x.PublishedVersionId != null || x.CurrentVersionId != null))
            .ToListAsync(ct);
        if (flows.Count == 0) return;

        var runtime = sp.GetRequiredService<IOptions<WorkflowRuntimeOptions>>().Value;
        var shadowEnabled = string.Equals(runtime.EngineMode, "shadow", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(runtime.EngineMode, "new", StringComparison.OrdinalIgnoreCase) ||
                            runtime.ShadowLogOnly;

        Dictionary<Guid, TriggerEvaluationService.ShadowMatch>? shadowMatches = null;
        if (shadowEnabled)
        {
            var targetVersionIds = flows
                .Select(x => x.PublishedVersionId ?? x.CurrentVersionId)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .ToList();
            var versions = await tenantDb.AutomationFlowVersions
                .AsNoTracking()
                .Where(x => x.TenantId == tenantId && targetVersionIds.Contains(x.Id))
                .ToListAsync(ct);
            var definitionByFlowId = versions
                .GroupBy(x => x.FlowId)
                .ToDictionary(
                    x => x.Key,
                    x => x.OrderByDescending(v => v.PublishedAtUtc ?? v.CreatedAtUtc).First().DefinitionJson);
            var triggerEvalService = sp.GetRequiredService<TriggerEvaluationService>();
            shadowMatches = triggerEvalService.EvaluateShadowMatches(flows, definitionByFlowId, inbound.MessageText);
        }

        Guid? resumeFlowId = null;
        var resumeSourceNodeId = string.Empty;
        if (inbound.IsInteractiveReply && !string.IsNullOrWhiteSpace(inbound.ContextMessageId))
        {
            var contextOutMessage = await tenantDb.Set<Message>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ProviderMessageId == inbound.ContextMessageId, ct);
            var resume = TryResolveFlowResumeFromIdempotencyKey(contextOutMessage?.IdempotencyKey);
            resumeFlowId = resume.FlowId;
            resumeSourceNodeId = resume.SourceNodeId;
        }

        var tenancy = sp.GetRequiredService<TenancyContext>();
        tenancy.SetTenant(tenantId, tenantSlug, tenantConnectionString);
        var anyMatchedFlow = false;

        foreach (var flow in flows)
        {
            if (resumeFlowId.HasValue && flow.Id != resumeFlowId.Value) continue;

            var targetVersionId = flow.PublishedVersionId ?? flow.CurrentVersionId;
            if (!targetVersionId.HasValue)
            {
                controlDb.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ActorUserId = Guid.Empty,
                    Action = "waba.workflow.trigger_eval",
                    Details = $"phoneNumberId={phoneNumberId}; inboundMessageId={inbound.MessageId}; flowId={flow.Id}; matched=false; reason=missing_target_version; matched_flow_id=",
                    CreatedAtUtc = DateTime.UtcNow
                });
                await controlDb.SaveChangesAsync(ct);
                continue;
            }
            var version = await tenantDb.AutomationFlowVersions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.FlowId == flow.Id && x.Id == targetVersionId.Value, ct);
            if (version is null)
            {
                controlDb.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ActorUserId = Guid.Empty,
                    Action = "waba.workflow.trigger_eval",
                    Details = $"phoneNumberId={phoneNumberId}; inboundMessageId={inbound.MessageId}; flowId={flow.Id}; matched=false; reason=version_not_found; matched_flow_id=",
                    CreatedAtUtc = DateTime.UtcNow
                });
                await controlDb.SaveChangesAsync(ct);
                continue;
            }
            if (!string.Equals((flow.Channel ?? "waba").Trim(), "waba", StringComparison.OrdinalIgnoreCase))
            {
                controlDb.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ActorUserId = Guid.Empty,
                    Action = "waba.workflow.trigger_eval",
                    Details = $"phoneNumberId={phoneNumberId}; inboundMessageId={inbound.MessageId}; flowId={flow.Id}; matched=false; reason=channel_not_waba; matched_flow_id=",
                    CreatedAtUtc = DateTime.UtcNow
                });
                await controlDb.SaveChangesAsync(ct);
                continue;
            }
            var triggerEval = EvaluateTriggerMatch(flow, inbound.MessageText, version.DefinitionJson);
            var bypassTriggerForResume = inbound.IsInteractiveReply && resumeFlowId.HasValue && resumeFlowId.Value == flow.Id;

            if (shadowEnabled && shadowMatches is not null && shadowMatches.TryGetValue(flow.Id, out var shadow))
            {
                var legacyMatched = triggerEval.Matched || bypassTriggerForResume;
                try
                {
                    await tenantDb.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO "TriggerEvaluationAudit"
                        ("Id","TenantId","FlowId","InboundMessageId","ConversationId","MessageText","TriggerType","IsMatch","MatchScore","Reason","EvaluatedAtUtc")
                        VALUES
                        ({Guid.NewGuid()},{tenantId},{flow.Id},{inbound.MessageId},{(Guid?)null},{NormalizeInboundMessageText(inbound.MessageText)},{flow.TriggerType ?? "keyword"},{shadow.Matched},{shadow.MatchScore},{shadow.Reason}, {DateTime.UtcNow})
                        """, ct);
                }
                catch
                {
                    // Keep runtime safe if TriggerEvaluationAudit table is absent on older tenant DBs.
                }

                if (legacyMatched != shadow.Matched)
                {
                    controlDb.AuditLogs.Add(new AuditLog
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        ActorUserId = Guid.Empty,
                        Action = "waba.workflow.trigger_shadow_mismatch",
                        Details = $"phoneNumberId={phoneNumberId}; inboundMessageId={inbound.MessageId}; flowId={flow.Id}; legacyMatched={legacyMatched}; shadowMatched={shadow.Matched}; legacyReason={(bypassTriggerForResume ? "interactive_resume_context" : triggerEval.Reason)}; shadowReason={shadow.Reason}",
                        CreatedAtUtc = DateTime.UtcNow
                    });
                    await controlDb.SaveChangesAsync(ct);
                }
            }

            if (!triggerEval.Matched && !bypassTriggerForResume)
            {
                controlDb.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ActorUserId = Guid.Empty,
                    Action = "waba.workflow.trigger_eval",
                    Details = $"phoneNumberId={phoneNumberId}; inboundMessageId={inbound.MessageId}; flowId={flow.Id}; matched=false; reason={triggerEval.Reason}; matched_flow_id=",
                    CreatedAtUtc = DateTime.UtcNow
                });
                await controlDb.SaveChangesAsync(ct);
                continue;
            }
            controlDb.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ActorUserId = Guid.Empty,
                Action = "waba.workflow.trigger_eval",
                Details = $"phoneNumberId={phoneNumberId}; inboundMessageId={inbound.MessageId}; flowId={flow.Id}; matched=true; reason={(bypassTriggerForResume ? "interactive_resume_context" : triggerEval.Reason)}; matched_flow_id={flow.Id}",
                CreatedAtUtc = DateTime.UtcNow
            });
            await controlDb.SaveChangesAsync(ct);
            anyMatchedFlow = true;

            var idempotencyKey = $"auto:{flow.Id}:{inbound.MessageId}";
            var existing = await tenantDb.AutomationRuns
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.FlowId == flow.Id && x.IdempotencyKey == idempotencyKey, ct);
            if (existing is not null) continue;

            var payload = BuildPayload(inbound);
            var run = new AutomationRun
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                FlowId = flow.Id,
                VersionId = version.Id,
                Mode = "live",
                TriggerType = string.IsNullOrWhiteSpace(flow.TriggerType) ? "keyword" : flow.TriggerType,
                IdempotencyKey = idempotencyKey,
                TriggerPayloadJson = JsonSerializer.Serialize(payload),
                Status = "running",
                StartedAtUtc = DateTime.UtcNow
            };
            tenantDb.AutomationRuns.Add(run);
            await tenantDb.SaveChangesAsync(ct);

            var executionEngine = sp.GetRequiredService<WorkflowExecutionEngine>();
            await executionEngine.ExecuteAsync(new WorkflowExecutionEngine.ExecuteRequest
            {
                TenantId = tenantId,
                FlowId = flow.Id,
                PhoneNumberId = phoneNumberId,
                InboundMessageId = inbound.MessageId,
                InboundRecipient = inbound.From,
                InboundMessageText = inbound.MessageText,
                InboundMatchKey = inbound.MatchKey,
                DefinitionJson = version.DefinitionJson,
                Run = run,
                Payload = payload,
                IsInteractiveResume = bypassTriggerForResume,
                ResumeSourceNodeId = resumeSourceNodeId
            }, ct);
            await tenantDb.SaveChangesAsync(ct);
        }

        if (!anyMatchedFlow && !inbound.IsInteractiveReply)
        {
            await TryFaqAutoReplyAsync(sp, controlDb, tenantDb, tenantId, phoneNumberId, inbound, ct);
        }
    }

    private async Task<bool> TryAutoHandoffAsync(
        IServiceProvider sp,
        ControlDbContext controlDb,
        TenantDbContext tenantDb,
        Guid tenantId,
        string phoneNumberId,
        TriggerInboundContext inbound,
        CancellationToken ct)
    {
        var normalized = NormalizeMatchText(NormalizeInboundMessageText(inbound.MessageText));
        if (!ContainsHandoffIntent(normalized)) return false;

        var conversation = await tenantDb.Set<Conversation>()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.CustomerPhone == inbound.From, ct);
        if (conversation is not null)
        {
            conversation.Status = "Open";
            conversation.AssignedUserId = string.Empty;
            conversation.AssignedUserName = string.Empty;
            conversation.LabelsCsv = MergeLabels(conversation.LabelsCsv, "needs_human");
            conversation.LastMessageAtUtc = DateTime.UtcNow;

            tenantDb.Set<ConversationNote>().Add(new ConversationNote
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ConversationId = conversation.Id,
                Body = "Auto-handoff requested by customer message intent.",
                CreatedByUserId = Guid.Empty,
                CreatedByName = "system",
                CreatedAtUtc = DateTime.UtcNow
            });
            await tenantDb.SaveChangesAsync(ct);
        }

        try
        {
            var messaging = sp.GetRequiredService<MessagingService>();
            await messaging.EnqueueAsync(new SendMessageRequest
            {
                Recipient = inbound.From,
                Body = "I understand. I am connecting you with a support agent now.",
                Channel = ChannelType.WhatsApp,
                IdempotencyKey = $"auto-handoff:{tenantId}:{inbound.MessageId}"
            }, ct);
        }
        catch
        {
            // Keep escalation path resilient even if auto-reply fails.
        }

        controlDb.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorUserId = Guid.Empty,
            Action = "waba.workflow.auto_handoff",
            Details = $"phoneNumberId={phoneNumberId}; inboundMessageId={inbound.MessageId}; from={inbound.From}; reason=handoff_intent_keyword",
            CreatedAtUtc = DateTime.UtcNow
        });
        await controlDb.SaveChangesAsync(ct);
        return true;
    }

    private static bool ContainsHandoffIntent(string normalizedMessage)
    {
        if (string.IsNullOrWhiteSpace(normalizedMessage)) return false;
        var triggers = new[]
        {
            "talk to agent",
            "need agent",
            "human",
            "customer support",
            "connect me to support",
            "not satisfied",
            "not helpful",
            "this didnt help",
            "issue not solved",
            "still not working",
            "complaint"
        };
        return triggers.Any(t => normalizedMessage.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    private static string MergeLabels(string existingCsv, string label)
    {
        var labels = (existingCsv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        labels.Add(label);
        return string.Join(",", labels.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
    }

    private async Task<bool> TryFaqAutoReplyAsync(
        IServiceProvider sp,
        ControlDbContext controlDb,
        TenantDbContext tenantDb,
        Guid tenantId,
        string phoneNumberId,
        TriggerInboundContext inbound,
        CancellationToken ct)
    {
        var inboundText = NormalizeInboundMessageText(inbound.MessageText);
        var normalizedInbound = NormalizeMatchText(inboundText);
        if (string.IsNullOrWhiteSpace(normalizedInbound)) return false;

        List<FaqKnowledgeItem> faqs;
        try
        {
            faqs = await tenantDb.FaqKnowledgeItems
                .AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.IsActive)
                .OrderByDescending(x => x.UpdatedAtUtc)
                .ToListAsync(ct);
        }
        catch
        {
            // Keep webhook runtime safe on tenants where FAQ table might not exist yet.
            return false;
        }

        var best = faqs
            .Select(f =>
            {
                var q = NormalizeMatchText(f.Question ?? string.Empty);
                if (string.IsNullOrWhiteSpace(q)) return new { faq = f, score = 0, reason = "empty_question" };
                if (string.Equals(normalizedInbound, q, StringComparison.OrdinalIgnoreCase))
                    return new { faq = f, score = 100, reason = "exact" };
                if (normalizedInbound.StartsWith(q, StringComparison.OrdinalIgnoreCase) || q.StartsWith(normalizedInbound, StringComparison.OrdinalIgnoreCase))
                    return new { faq = f, score = 80, reason = "starts_with" };
                if (normalizedInbound.Contains(q, StringComparison.OrdinalIgnoreCase) || q.Contains(normalizedInbound, StringComparison.OrdinalIgnoreCase))
                    return new { faq = f, score = 60, reason = "contains" };
                return new { faq = f, score = 0, reason = "no_match" };
            })
            .Where(x => x.score > 0 && !string.IsNullOrWhiteSpace(x.faq.Answer))
            .OrderByDescending(x => x.score)
            .ThenByDescending(x => x.faq.UpdatedAtUtc)
            .FirstOrDefault();

        if (best is null) return false;

        try
        {
            var messaging = sp.GetRequiredService<MessagingService>();
            await messaging.EnqueueAsync(new SendMessageRequest
            {
                Recipient = inbound.From,
                Body = best.faq.Answer.Trim(),
                Channel = ChannelType.WhatsApp,
                IdempotencyKey = $"auto-faq:{best.faq.Id}:{inbound.MessageId}"
            }, ct);

            controlDb.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ActorUserId = Guid.Empty,
                Action = "waba.workflow.faq_match",
                Details = $"phoneNumberId={phoneNumberId}; inboundMessageId={inbound.MessageId}; faqId={best.faq.Id}; score={best.score}; reason={best.reason}",
                CreatedAtUtc = DateTime.UtcNow
            });
            await controlDb.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            controlDb.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ActorUserId = Guid.Empty,
                Action = "waba.workflow.faq_match_failed",
                Details = $"phoneNumberId={phoneNumberId}; inboundMessageId={inbound.MessageId}; faqId={best.faq.Id}; error={redactor.RedactText(ex.Message)}",
                CreatedAtUtc = DateTime.UtcNow
            });
            await controlDb.SaveChangesAsync(ct);
            return false;
        }
    }

    private static (bool Ok, string Error, string PhoneNumberId, List<InboundItem> Inbound, List<StatusItem> Statuses) ParsePayload(string rawBody)
    {
        var phoneNumberId = string.Empty;
        var inbound = new List<InboundItem>();
        var statuses = new List<StatusItem>();
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            if (!doc.RootElement.TryGetProperty("entry", out var entries))
                return (false, "entry_missing", string.Empty, inbound, statuses);

            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("changes", out var changes)) continue;
                foreach (var change in changes.EnumerateArray())
                {
                    if (!change.TryGetProperty("value", out var value)) continue;
                    if (value.TryGetProperty("metadata", out var metadata) && metadata.TryGetProperty("phone_number_id", out var pni))
                        phoneNumberId = pni.GetString() ?? string.Empty;

                    if (value.TryGetProperty("statuses", out var statusesNode) && statusesNode.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var s in statusesNode.EnumerateArray())
                        {
                            var messageId = s.TryGetProperty("id", out var idNode) ? idNode.GetString() ?? string.Empty : string.Empty;
                            var status = s.TryGetProperty("status", out var statusNode) ? statusNode.GetString() ?? string.Empty : string.Empty;
                            DateTime? atUtc = null;
                            if (s.TryGetProperty("timestamp", out var tsNode) && long.TryParse(tsNode.GetString(), out var unixTs))
                                atUtc = DateTimeOffset.FromUnixTimeSeconds(unixTs).UtcDateTime;
                            var recipientId = s.TryGetProperty("recipient_id", out var recNode) ? recNode.GetString() ?? string.Empty : string.Empty;
                            var conversationId = string.Empty;
                            var conversationOriginType = string.Empty;
                            DateTime? conversationExpirationUtc = null;
                            if (s.TryGetProperty("conversation", out var convNode))
                            {
                                conversationId = convNode.TryGetProperty("id", out var convIdNode) ? convIdNode.GetString() ?? string.Empty : string.Empty;
                                if (convNode.TryGetProperty("origin", out var originNode))
                                    conversationOriginType = originNode.TryGetProperty("type", out var typeNode) ? typeNode.GetString() ?? string.Empty : string.Empty;
                                if (convNode.TryGetProperty("expiration_timestamp", out var expNode) && long.TryParse(expNode.GetString(), out var expUnix))
                                    conversationExpirationUtc = DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime;
                            }
                            bool? pricingBillable = null;
                            var pricingCategory = string.Empty;
                            if (s.TryGetProperty("pricing", out var pricingNode))
                            {
                                if (pricingNode.TryGetProperty("billable", out var billNode) && (billNode.ValueKind == JsonValueKind.True || billNode.ValueKind == JsonValueKind.False))
                                    pricingBillable = billNode.GetBoolean();
                                pricingCategory = pricingNode.TryGetProperty("category", out var catNode) ? catNode.GetString() ?? string.Empty : string.Empty;
                            }
                            var errorCode = string.Empty;
                            var errorTitle = string.Empty;
                            var errorDetail = string.Empty;
                            if (s.TryGetProperty("errors", out var errorsNode) && errorsNode.ValueKind == JsonValueKind.Array && errorsNode.GetArrayLength() > 0)
                            {
                                var err = errorsNode[0];
                                errorCode = err.TryGetProperty("code", out var cNode) ? cNode.ToString() : string.Empty;
                                errorTitle = err.TryGetProperty("title", out var tNode) ? tNode.GetString() ?? string.Empty : string.Empty;
                                errorDetail = err.TryGetProperty("details", out var dNode) ? dNode.GetString() ?? string.Empty : string.Empty;
                                if (string.IsNullOrWhiteSpace(errorDetail) && err.TryGetProperty("message", out var mNode))
                                    errorDetail = mNode.GetString() ?? string.Empty;
                            }
                            if (!string.IsNullOrWhiteSpace(messageId) && !string.IsNullOrWhiteSpace(status))
                                statuses.Add(new StatusItem
                                {
                                    MessageId = messageId,
                                    Status = status,
                                    AtUtc = atUtc,
                                    RecipientId = recipientId,
                                    ConversationId = conversationId,
                                    ConversationOriginType = conversationOriginType,
                                    ConversationExpirationUtc = conversationExpirationUtc,
                                    PricingBillable = pricingBillable,
                                    PricingCategory = pricingCategory,
                                    ErrorCode = errorCode,
                                    ErrorTitle = errorTitle,
                                    ErrorDetail = errorDetail,
                                    RawJson = s.GetRawText()
                                });
                        }
                    }

                    if (!value.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array) continue;
                    foreach (var msg in messages.EnumerateArray())
                    {
                        if (!msg.TryGetProperty("from", out var fromProp)) continue;
                        var from = fromProp.GetString() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(from)) continue;
                        var textBody = msg.TryGetProperty("text", out var textNode) && textNode.TryGetProperty("body", out var bodyNode)
                            ? bodyNode.GetString() ?? string.Empty
                            : string.Empty;
                        var type = msg.TryGetProperty("type", out var typeNode) ? typeNode.GetString() ?? string.Empty : string.Empty;
                        DateTime? atUtc = null;
                        if (msg.TryGetProperty("timestamp", out var msgTsNode) && long.TryParse(msgTsNode.GetString(), out var msgUnixTs))
                            atUtc = DateTimeOffset.FromUnixTimeSeconds(msgUnixTs).UtcDateTime;
                        var mediaId = string.Empty;
                        var mediaMime = string.Empty;
                        var mediaSha = string.Empty;
                        var mediaCaption = string.Empty;
                        var mediaFileName = string.Empty;
                        if (type is "image" or "video" or "audio" or "document" or "sticker")
                        {
                            if (msg.TryGetProperty(type, out var mediaNode))
                            {
                                mediaId = mediaNode.TryGetProperty("id", out var mId) ? mId.GetString() ?? string.Empty : string.Empty;
                                mediaMime = mediaNode.TryGetProperty("mime_type", out var mMime) ? mMime.GetString() ?? string.Empty : string.Empty;
                                mediaSha = mediaNode.TryGetProperty("sha256", out var mSha) ? mSha.GetString() ?? string.Empty : string.Empty;
                                mediaCaption = mediaNode.TryGetProperty("caption", out var mCap) ? mCap.GetString() ?? string.Empty : string.Empty;
                                mediaFileName = mediaNode.TryGetProperty("filename", out var mName) ? mName.GetString() ?? string.Empty : string.Empty;
                            }
                        }
                        var buttonPayload = string.Empty;
                        var buttonText = string.Empty;
                        if (msg.TryGetProperty("button", out var buttonNode))
                        {
                            buttonPayload = buttonNode.TryGetProperty("payload", out var pNode) ? pNode.GetString() ?? string.Empty : string.Empty;
                            buttonText = buttonNode.TryGetProperty("text", out var btNode) ? btNode.GetString() ?? string.Empty : string.Empty;
                        }
                        var interactiveType = string.Empty;
                        var listReplyId = string.Empty;
                        var listReplyTitle = string.Empty;
                        var flowEventType = string.Empty;
                        var flowId = string.Empty;
                        var flowToken = string.Empty;
                        var flowScreenId = string.Empty;
                        var flowPayloadJson = "{}";
                        if (msg.TryGetProperty("interactive", out var interNode))
                        {
                            interactiveType = interNode.TryGetProperty("type", out var iTypeNode) ? iTypeNode.GetString() ?? string.Empty : string.Empty;
                            if (interNode.TryGetProperty("list_reply", out var listReply))
                            {
                                listReplyId = listReply.TryGetProperty("id", out var lrId) ? lrId.GetString() ?? string.Empty : string.Empty;
                                listReplyTitle = listReply.TryGetProperty("title", out var lrTitle) ? lrTitle.GetString() ?? string.Empty : string.Empty;
                            }
                            if (interNode.TryGetProperty("button_reply", out var buttonReply))
                            {
                                if (string.IsNullOrWhiteSpace(listReplyId))
                                    listReplyId = buttonReply.TryGetProperty("id", out var brId) ? brId.GetString() ?? string.Empty : string.Empty;
                                if (string.IsNullOrWhiteSpace(listReplyTitle))
                                    listReplyTitle = buttonReply.TryGetProperty("title", out var brTitle) ? brTitle.GetString() ?? string.Empty : string.Empty;
                            }
                            if (interNode.TryGetProperty("nfm_reply", out var nfmReply) && nfmReply.ValueKind == JsonValueKind.Object)
                            {
                                flowEventType = "flow.submission";
                                flowId = nfmReply.TryGetProperty("flow_id", out var fIdNode) ? fIdNode.GetString() ?? string.Empty : string.Empty;
                                flowToken = nfmReply.TryGetProperty("flow_token", out var fTokNode) ? fTokNode.GetString() ?? string.Empty : string.Empty;
                                flowScreenId = nfmReply.TryGetProperty("screen", out var fScreenNode) ? fScreenNode.GetString() ?? string.Empty : string.Empty;
                                if (nfmReply.TryGetProperty("response_json", out var respNode))
                                {
                                    flowPayloadJson = respNode.GetRawText();
                                    var completed = false;
                                    if (respNode.ValueKind == JsonValueKind.Object)
                                    {
                                        if (respNode.TryGetProperty("completed", out var compNode) && compNode.ValueKind == JsonValueKind.True)
                                            completed = true;
                                        if (respNode.TryGetProperty("flow_status", out var statusNode) &&
                                            string.Equals(statusNode.GetString(), "completed", StringComparison.OrdinalIgnoreCase))
                                            completed = true;
                                    }
                                    if (completed) flowEventType = "flow.completion";
                                }
                            }
                            if (interNode.TryGetProperty("flow_reply", out var flowReply) && flowReply.ValueKind == JsonValueKind.Object)
                            {
                                if (string.IsNullOrWhiteSpace(flowEventType)) flowEventType = "flow.submission";
                                if (string.IsNullOrWhiteSpace(flowId))
                                    flowId = flowReply.TryGetProperty("flow_id", out var ffIdNode) ? ffIdNode.GetString() ?? string.Empty : string.Empty;
                                if (string.IsNullOrWhiteSpace(flowToken))
                                    flowToken = flowReply.TryGetProperty("flow_token", out var ffTokNode) ? ffTokNode.GetString() ?? string.Empty : string.Empty;
                                if (string.IsNullOrWhiteSpace(flowScreenId))
                                    flowScreenId = flowReply.TryGetProperty("screen", out var ffScreenNode) ? ffScreenNode.GetString() ?? string.Empty : string.Empty;
                                if (flowReply.TryGetProperty("response_json", out var frNode))
                                    flowPayloadJson = frNode.GetRawText();
                            }
                        }
                        var locationSummary = string.Empty;
                        if (msg.TryGetProperty("location", out var locNode))
                        {
                            var nameText = locNode.TryGetProperty("name", out var nm) ? nm.GetString() ?? string.Empty : string.Empty;
                            var addr = locNode.TryGetProperty("address", out var ad) ? ad.GetString() ?? string.Empty : string.Empty;
                            var lat = locNode.TryGetProperty("latitude", out var latNode) ? latNode.ToString() : string.Empty;
                            var lng = locNode.TryGetProperty("longitude", out var lngNode) ? lngNode.ToString() : string.Empty;
                            locationSummary = $"Location: {nameText} {addr} ({lat},{lng})".Trim();
                        }
                        var contextMessageId = string.Empty;
                        if (msg.TryGetProperty("context", out var contextNode))
                        {
                            contextMessageId = contextNode.TryGetProperty("id", out var ctxId) ? ctxId.GetString() ?? string.Empty : string.Empty;
                        }

                        var referralSourceUrl = string.Empty;
                        var referralHeadline = string.Empty;
                        if (msg.TryGetProperty("referral", out var referralNode))
                        {
                            referralSourceUrl = referralNode.TryGetProperty("source_url", out var rUrl) ? rUrl.GetString() ?? string.Empty : string.Empty;
                            referralHeadline = referralNode.TryGetProperty("headline", out var rHead) ? rHead.GetString() ?? string.Empty : string.Empty;
                        }

                        var reactionEmoji = string.Empty;
                        var reactionMessageId = string.Empty;
                        if (msg.TryGetProperty("reaction", out var reactionNode))
                        {
                            reactionEmoji = reactionNode.TryGetProperty("emoji", out var rEmoji) ? rEmoji.GetString() ?? string.Empty : string.Empty;
                            reactionMessageId = reactionNode.TryGetProperty("message_id", out var rMid) ? rMid.GetString() ?? string.Empty : string.Empty;
                        }

                        var orderSummary = string.Empty;
                        if (msg.TryGetProperty("order", out var orderNode))
                        {
                            var catalogId = orderNode.TryGetProperty("catalog_id", out var cId) ? cId.GetString() ?? string.Empty : string.Empty;
                            var text = orderNode.TryGetProperty("text", out var oText) ? oText.GetString() ?? string.Empty : string.Empty;
                            var productCount = orderNode.TryGetProperty("product_items", out var itemsNode) && itemsNode.ValueKind == JsonValueKind.Array
                                ? itemsNode.GetArrayLength()
                                : 0;
                            orderSummary = $"Order: catalog={catalogId}; items={productCount}; text={text}".Trim();
                        }

                        var contactsSummary = string.Empty;
                        if (msg.TryGetProperty("contacts", out var msgContactsNode) && msgContactsNode.ValueKind == JsonValueKind.Array)
                        {
                            contactsSummary = $"Shared contacts: {msgContactsNode.GetArrayLength()}";
                        }
                        var unsupportedSummary = ExtractUnsupportedSummary(msg);
                        var name = value.TryGetProperty("contacts", out var contactsNode)
                            && contactsNode.ValueKind == JsonValueKind.Array
                            && contactsNode.GetArrayLength() > 0
                            && contactsNode[0].TryGetProperty("profile", out var profileNode)
                            && profileNode.TryGetProperty("name", out var nameNode)
                            ? nameNode.GetString() ?? string.Empty
                            : string.Empty;
                        var messageId = msg.TryGetProperty("id", out var msgIdNode) ? msgIdNode.GetString() ?? string.Empty : string.Empty;
                        inbound.Add(new InboundItem
                        {
                            MessageId = messageId,
                            From = from,
                            Name = name,
                            Body = textBody,
                            MessageType = type,
                            AtUtc = atUtc,
                            MediaId = mediaId,
                            MediaMimeType = mediaMime,
                            MediaSha256 = mediaSha,
                            MediaCaption = mediaCaption,
                            MediaFileName = mediaFileName,
                            ButtonPayload = buttonPayload,
                            ButtonText = buttonText,
                            InteractiveType = interactiveType,
                            ListReplyId = listReplyId,
                            ListReplyTitle = listReplyTitle,
                            LocationSummary = locationSummary,
                            ContextMessageId = contextMessageId,
                            ReferralSourceUrl = referralSourceUrl,
                            ReferralHeadline = referralHeadline,
                            ReactionEmoji = reactionEmoji,
                            ReactionMessageId = reactionMessageId,
                            OrderSummary = orderSummary,
                            ContactsSummary = contactsSummary,
                            UnsupportedSummary = unsupportedSummary,
                            FlowEventType = flowEventType,
                            FlowId = flowId,
                            FlowToken = flowToken,
                            FlowScreenId = flowScreenId,
                            FlowPayloadJson = flowPayloadJson,
                            RawJson = msg.GetRawText()
                        });
                    }
                }
            }

            return (true, string.Empty, phoneNumberId, inbound, statuses);
        }
        catch (Exception ex)
        {
            return (false, ex.GetType().Name, string.Empty, inbound, statuses);
        }
    }

    private static string ComposeInboundBody(InboundItem inbound)
    {
        if (!string.IsNullOrWhiteSpace(inbound.Body)) return inbound.Body;
        if (!string.IsNullOrWhiteSpace(inbound.UnsupportedSummary)) return inbound.UnsupportedSummary;
        if (!string.IsNullOrWhiteSpace(inbound.ButtonText)) return $"Button reply: {inbound.ButtonText}";
        if (!string.IsNullOrWhiteSpace(inbound.ListReplyTitle)) return $"Interactive reply: {inbound.ListReplyTitle}";
        if (!string.IsNullOrWhiteSpace(inbound.ReactionEmoji)) return $"Reaction: {inbound.ReactionEmoji}";
        if (!string.IsNullOrWhiteSpace(inbound.OrderSummary)) return inbound.OrderSummary;
        if (!string.IsNullOrWhiteSpace(inbound.LocationSummary)) return inbound.LocationSummary;
        if (!string.IsNullOrWhiteSpace(inbound.ContactsSummary)) return inbound.ContactsSummary;
        if (!string.IsNullOrWhiteSpace(inbound.ReferralHeadline)) return $"Referral: {inbound.ReferralHeadline}";
        if (string.Equals(inbound.MessageType, "unsupported", StringComparison.OrdinalIgnoreCase))
            return "Unsupported incoming WhatsApp message type.";
        if (!string.IsNullOrWhiteSpace(inbound.MessageType)) return $"Inbound {inbound.MessageType} message";
        return "[Inbound message]";
    }

    private static string ExtractUnsupportedSummary(JsonElement msg)
    {
        if (!msg.TryGetProperty("unsupported", out var unsupportedNode) || unsupportedNode.ValueKind != JsonValueKind.Object)
            return string.Empty;

        string pickValue(params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!unsupportedNode.TryGetProperty(key, out var node)) continue;
                var value = node.ValueKind == JsonValueKind.String ? node.GetString() ?? string.Empty : node.ToString();
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return string.Empty;
        }

        var title = pickValue("title", "reason");
        var detail = pickValue("message", "description", "details", "type");
        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(detail))
            return $"Unsupported message: {title} ({detail})";
        if (!string.IsNullOrWhiteSpace(title))
            return $"Unsupported message: {title}";
        if (!string.IsNullOrWhiteSpace(detail))
            return $"Unsupported message: {detail}";
        return "Unsupported incoming WhatsApp message type.";
    }

    private static bool IsMediaType(string? messageType)
    {
        var t = (messageType ?? string.Empty).Trim().ToLowerInvariant();
        return t is "image" or "video" or "audio" or "document" or "sticker";
    }

    private static string ComposeInboundMediaBody(InboundItem inbound)
    {
        return JsonSerializer.Serialize(new
        {
            mediaId = inbound.MediaId ?? string.Empty,
            mimeType = inbound.MediaMimeType ?? string.Empty,
            caption = inbound.MediaCaption ?? string.Empty,
            fileName = inbound.MediaFileName ?? string.Empty,
            kind = inbound.MessageType ?? "media"
        });
    }

    private static async Task<string> BuildInboundReplyPrefixAsync(
        TenantDbContext tenantDb,
        Guid tenantId,
        string contextMessageId,
        string customerName,
        CancellationToken ct)
    {
        var targetProviderId = (contextMessageId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(targetProviderId)) return string.Empty;
        var target = await tenantDb.Set<Message>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ProviderMessageId == targetProviderId, ct);
        if (target is null)
        {
            var fallbackLabel = string.IsNullOrWhiteSpace(customerName) ? "Customer" : customerName;
            return $"↪ Reply to ({fallbackLabel}): [Referenced message]";
        }

        var label = string.Equals(target.Status, "Received", StringComparison.OrdinalIgnoreCase)
            ? (string.IsNullOrWhiteSpace(customerName) ? "Customer" : customerName)
            : "Agent";
        var at = target.CreatedAtUtc.ToLocalTime().ToString("HH:mm");
        var preview = BuildMessagePreview(target);
        if (string.IsNullOrWhiteSpace(preview)) preview = "Message";
        return $"↪ Reply to ({label} {at}): {preview}";
    }

    private static string BuildMessagePreview(Message message)
    {
        if (message.MessageType.StartsWith("media:", StringComparison.OrdinalIgnoreCase))
        {
            var kind = message.MessageType["media:".Length..];
            try
            {
                using var doc = JsonDocument.Parse(message.Body ?? "{}");
                var root = doc.RootElement;
                var fileName = root.TryGetProperty("fileName", out var fNode) ? (fNode.GetString() ?? string.Empty).Trim() : string.Empty;
                var caption = root.TryGetProperty("caption", out var cNode) ? (cNode.GetString() ?? string.Empty).Trim() : string.Empty;
                if (!string.IsNullOrWhiteSpace(caption)) return $"{kind}: {caption}";
                if (!string.IsNullOrWhiteSpace(fileName)) return $"{kind}: {fileName}";
                return $"{kind} attachment";
            }
            catch
            {
                return $"{kind} attachment";
            }
        }
        var body = (message.Body ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(body)) return "Message";
        var firstLine = body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLine)) return "Message";
        return firstLine.Length <= 120 ? firstLine : $"{firstLine[..120]}...";
    }

    private static void ApplyStatusTransition(Message msg, StatusItem incoming, ControlDbContext controlDb)
    {
        var next = MessageStateMachine.NormalizeWebhookStatus(incoming.Status);
        if (string.IsNullOrWhiteSpace(next)) return;
        if (!MessageStateMachine.CanTransition(msg.Status, next)) return;

        msg.Status = next;
        if (next == "Delivered" && incoming.AtUtc.HasValue) msg.DeliveredAtUtc = incoming.AtUtc.Value;
        if (next == "Read" && incoming.AtUtc.HasValue) msg.ReadAtUtc = incoming.AtUtc.Value;
        if (next.StartsWith("Failed", StringComparison.OrdinalIgnoreCase))
        {
            var reasonType = IsRetryableError(controlDb, incoming.ErrorCode, incoming.ErrorTitle, incoming.ErrorDetail) ? "retryable" : "permanent";
            msg.LastError = $"{reasonType}; code={incoming.ErrorCode}; title={incoming.ErrorTitle}; details={incoming.ErrorDetail}".Trim();
        }
    }

    private static bool IsRetryableError(ControlDbContext controlDb, string code, string title, string details)
    {
        var policy = controlDb.WabaErrorPolicies.FirstOrDefault(x => x.Code == (code ?? string.Empty) && x.IsActive);
        if (policy is not null) return string.Equals(policy.Classification, "retryable", StringComparison.OrdinalIgnoreCase);

        var raw = $"{code} {title} {details}".ToLowerInvariant();
        if (raw.Contains("rate limit") || raw.Contains("temporar") || raw.Contains("timeout") || raw.Contains("server") || raw.Contains("5xx")) return true;
        if (raw.Contains("invalid") || raw.Contains("permission") || raw.Contains("policy") || raw.Contains("rejected") || raw.Contains("not registered") || raw.Contains("token")) return false;
        return false;
    }
}
