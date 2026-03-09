using Textzy.Api.Data;
using Textzy.Api.DTOs;
using Textzy.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Textzy.Api.Services;

public class MessagingService(
    TenantDbContext db,
    TenancyContext tenancy,
    OutboundMessageQueueService queue,
    BillingGuardService billingGuard,
    SecurityControlService security,
    ContactPiiService contactPii,
    TemplateVariableResolverService templateVariables)
{
    public async Task<Message> EnqueueAsync(SendMessageRequest request, CancellationToken ct = default)
    {
        await billingGuard.RotateMonthlyBucketAsync(tenancy.TenantId, ct);
        if (await security.IsCircuitBreakerOpenAsync(tenancy.TenantId, ct))
            throw new InvalidOperationException("Tenant circuit breaker is active. Outbound messaging is paused.");

        var idempotencyKey = (request.IdempotencyKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new InvalidOperationException("Idempotency-Key header is required.");
        if (idempotencyKey.Length > 180) throw new InvalidOperationException("Idempotency-Key is too long.");

        request.Recipient = InputGuardService.ValidatePhone(request.Recipient, "Recipient");
        if (request.IsMedia)
        {
            var mediaType = (request.MediaType ?? string.Empty).Trim().ToLowerInvariant();
            if (mediaType is not ("image" or "video" or "audio" or "document"))
                throw new InvalidOperationException("Media type must be image, video, audio, or document.");
            request.MediaType = mediaType;
            request.MediaId = InputGuardService.RequireTrimmed(request.MediaId, "Media id", 256);
            request.MediaCaption = (request.MediaCaption ?? string.Empty).Trim();
            if (request.MediaCaption.Length > 1024)
                throw new InvalidOperationException("Media caption is too long.");
        }
        else if (request.IsInteractive)
        {
            if (request.Channel != ChannelType.WhatsApp)
                throw new InvalidOperationException("Interactive messages are supported only for WhatsApp.");
            var interactiveType = (request.InteractiveType ?? string.Empty).Trim().ToLowerInvariant();
            if (interactiveType is not ("button" or "flow"))
                throw new InvalidOperationException("Interactive type must be button or flow.");
            request.Body = InputGuardService.RequireTrimmed(request.Body, "Message body", 1024);
            if (interactiveType == "button")
            {
                var buttons = (request.InteractiveButtons ?? [])
                    .Select(x => (x ?? string.Empty).Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .ToList();
                if (buttons.Count == 0)
                    throw new InvalidOperationException("At least one interactive button is required.");
                request.InteractiveButtons = buttons;
            }
            else
            {
                request.InteractiveFlowId = InputGuardService.RequireTrimmed(request.InteractiveFlowId, "Flow id", 120);
                request.InteractiveFlowCta = InputGuardService.RequireTrimmed(
                    string.IsNullOrWhiteSpace(request.InteractiveFlowCta) ? "Open" : request.InteractiveFlowCta,
                    "Flow CTA",
                    30);
                request.InteractiveFlowToken = (request.InteractiveFlowToken ?? string.Empty).Trim();
                request.InteractiveFlowAction = (request.InteractiveFlowAction ?? "navigate").Trim().ToLowerInvariant();
                if (request.InteractiveFlowAction is not ("navigate" or "data_exchange"))
                    throw new InvalidOperationException("Flow action must be navigate or data_exchange.");
                request.InteractiveFlowScreen = (request.InteractiveFlowScreen ?? string.Empty).Trim();
                request.InteractiveFlowDataJson = string.IsNullOrWhiteSpace(request.InteractiveFlowDataJson)
                    ? "{}"
                    : request.InteractiveFlowDataJson.Trim();
                try
                {
                    using var _ = JsonDocument.Parse(request.InteractiveFlowDataJson);
                }
                catch
                {
                    throw new InvalidOperationException("Interactive flow data must be valid JSON.");
                }

                if (request.InteractiveFlowMessageVersion < 1 || request.InteractiveFlowMessageVersion > 9)
                    throw new InvalidOperationException("Interactive flow message version must be between 1 and 9.");
            }
            request.InteractiveType = interactiveType;
        }
        else if (!request.UseTemplate)
        {
            request.Body = InputGuardService.RequireTrimmed(request.Body, "Message body", 4096);
        }
        if (request.Channel == ChannelType.Sms)
        {
            request.SmsSenderId = (request.SmsSenderId ?? string.Empty).Trim().ToUpperInvariant();
            request.SmsPeId = (request.SmsPeId ?? string.Empty).Trim();
            request.SmsTemplateId = (request.SmsTemplateId ?? string.Empty).Trim();
            if (request.SmsSenderId.Length > 20)
                throw new InvalidOperationException("SMS sender ID is too long.");
            if (request.SmsPeId.Length > 80)
                throw new InvalidOperationException("SMS PE_ID is too long.");
            if (request.SmsTemplateId.Length > 80)
                throw new InvalidOperationException("SMS Template_ID is too long.");
        }
        if (request.UseTemplate && request.Channel == ChannelType.WhatsApp)
        {
            request.TemplateName = InputGuardService.RequireTrimmed(request.TemplateName, "Template name", 128);
            var normalizedLang = string.IsNullOrWhiteSpace(request.TemplateLanguageCode)
                ? "en"
                : request.TemplateLanguageCode.Trim().ToLowerInvariant();
            request.TemplateLanguageCode = normalizedLang;

            var approvedTemplate = await db.Templates
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.TenantId == tenancy.TenantId &&
                    x.Channel == ChannelType.WhatsApp &&
                    x.Name == request.TemplateName &&
                    x.Language == normalizedLang, ct);

            if (approvedTemplate is null)
                throw new InvalidOperationException($"Approved WhatsApp template '{request.TemplateName}' ({normalizedLang}) not found.");
            var approved = string.Equals(approvedTemplate.Status, "approved", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(approvedTemplate.LifecycleStatus, "approved", StringComparison.OrdinalIgnoreCase);
            if (!approved)
                throw new InvalidOperationException($"Template '{request.TemplateName}' is not approved in WhatsApp yet.");

            var requiredParams = 0;
            foreach (Match m in Regex.Matches(approvedTemplate.Body ?? string.Empty, @"\{\{(\d+)\}\}"))
            {
                if (int.TryParse(m.Groups[1].Value, out var idx) && idx > requiredParams) requiredParams = idx;
            }

            var suppliedParams = (request.TemplateParameters ?? []).Select(x => (x ?? string.Empty).Trim()).ToList();
            var (tokenValues, suggestedValues) = await templateVariables.BuildAsync(request.Recipient, ct);

            // Auto-fill missing indices from system presets when available.
            if (requiredParams > suppliedParams.Count)
            {
                for (var idx = suppliedParams.Count + 1; idx <= requiredParams; idx++)
                {
                    suppliedParams.Add(suggestedValues.TryGetValue(idx, out var suggested) ? suggested : string.Empty);
                }
            }

            if (requiredParams > 0 && suppliedParams.Count < requiredParams)
                throw new InvalidOperationException($"Template requires {requiredParams} variables but only {suppliedParams.Count} provided.");

            for (var i = 0; i < suppliedParams.Count; i++)
            {
                var current = suppliedParams[i];
                if (TemplateVariableResolverService.TryResolveSystemToken(current, tokenValues, out var resolved))
                {
                    suppliedParams[i] = resolved;
                    continue;
                }

                if (current.StartsWith("{{", StringComparison.Ordinal) && current.EndsWith("}}", StringComparison.Ordinal))
                {
                    var tokenName = current[2..^2].Trim();
                    throw new InvalidOperationException($"Unknown system variable token: {tokenName}");
                }

                if (string.IsNullOrWhiteSpace(current))
                    throw new InvalidOperationException($"Template variable {i + 1} is required.");
            }

            request.TemplateParameters = suppliedParams;
        }

        var rpmOverride = await security.GetRatePerMinuteOverrideAsync(tenancy.TenantId, ct);
        if (rpmOverride.HasValue)
        {
            var minuteAgo = DateTime.UtcNow.AddMinutes(-1);
            var sentLastMinute = await db.Messages.CountAsync(x => x.TenantId == tenancy.TenantId && x.CreatedAtUtc >= minuteAgo, ct);
            if (sentLastMinute >= rpmOverride.Value)
                throw new InvalidOperationException($"Rate override reached ({rpmOverride.Value}/minute). Please retry shortly.");
        }

        var now = DateTime.UtcNow;
        var keyRow = await db.IdempotencyKeys.FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.Key == idempotencyKey, ct);
        if (keyRow is not null && keyRow.ExpiresAtUtc <= now)
        {
            db.IdempotencyKeys.Remove(keyRow);
            await db.SaveChangesAsync(ct);
            keyRow = null;
        }
        if (keyRow?.MessageId is Guid linkedMessageId)
        {
            var linked = await db.Messages.FirstOrDefaultAsync(x => x.Id == linkedMessageId && x.TenantId == tenancy.TenantId, ct);
            if (linked is not null) return linked;
        }

        var existing = await db.Messages
            .FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.IdempotencyKey == idempotencyKey, ct);
        if (existing is not null) return existing;

        var messageBody = request.UseTemplate
            ? $"{request.TemplateName}|{string.Join(",", request.TemplateParameters)}|{request.TemplateLanguageCode}"
            : request.IsInteractive
                ? request.InteractiveType == "flow"
                    ? JsonSerializer.Serialize(new
                    {
                        body = request.Body,
                        flowId = request.InteractiveFlowId,
                        flowCta = request.InteractiveFlowCta,
                        flowToken = request.InteractiveFlowToken,
                        flowAction = request.InteractiveFlowAction,
                        flowScreen = request.InteractiveFlowScreen,
                        flowMessageVersion = request.InteractiveFlowMessageVersion,
                        flowData = ParseFlowDataObject(request.InteractiveFlowDataJson)
                    })
                    : request.Body
            : request.IsMedia
                ? JsonSerializer.Serialize(new
                {
                    mediaId = request.MediaId,
                    caption = request.MediaCaption
                })
                : request.Body;

        var messageType = request.UseTemplate
            ? "template"
            : request.IsInteractive
                ? request.InteractiveType == "flow"
                    ? $"interactive:flow:{request.InteractiveFlowId}"
                    : $"interactive:button:{string.Join("~", request.InteractiveButtons.Select(x => x.Replace("~", " "))).Trim()}"
                : request.IsMedia
                    ? $"media:{request.MediaType}"
                    : "session";
        if (request.Channel == ChannelType.Sms &&
            (request.ForcePlatformSmsConfig ||
             !string.IsNullOrWhiteSpace(request.SmsSenderId) ||
             !string.IsNullOrWhiteSpace(request.SmsPeId) ||
             !string.IsNullOrWhiteSpace(request.SmsTemplateId)))
        {
            messageType = BuildSmsMessageType(
                messageType,
                request.ForcePlatformSmsConfig,
                request.SmsSenderId,
                request.SmsPeId,
                request.SmsTemplateId);
        }

        var smsSegmentInfo = request.Channel == ChannelType.Sms
            ? EstimateSmsSegments(messageBody)
            : new SmsSegmentInfo(1, false);

        if (request.Channel == ChannelType.WhatsApp)
        {
            var c = await billingGuard.TryConsumeAsync(tenancy.TenantId, "whatsappMessages", 1, ct);
            if (!c.Allowed) throw new InvalidOperationException(c.Message);
        }
        else if (request.Channel == ChannelType.Sms)
        {
            var optedOut = await db.SmsOptOuts.AsNoTracking()
                .AnyAsync(x => x.TenantId == tenancy.TenantId && x.Phone == request.Recipient && x.IsActive, ct);
            if (optedOut)
                throw new InvalidOperationException("Recipient has opted out from SMS.");

            var c = await billingGuard.TryConsumeAsync(tenancy.TenantId, "smsCredits", smsSegmentInfo.Segments, ct);
            if (!c.Allowed) throw new InvalidOperationException(c.Message);
        }

        var message = new Message
        {
            Id = Guid.NewGuid(),
            TenantId = tenancy.TenantId,
            CampaignId = request.CampaignId,
            Channel = request.Channel,
            Recipient = request.Recipient,
            Body = messageBody,
            MessageType = messageType,
            IdempotencyKey = idempotencyKey,
            RetryCount = 0,
            LastError = string.Empty,
            QueueProvider = queue.ActiveProvider,
            Status = "Queued"
        };

        db.Messages.Add(message);
        if (keyRow is null)
        {
            keyRow = new IdempotencyKeyRecord
            {
                Id = Guid.NewGuid(),
                TenantId = tenancy.TenantId,
                Key = idempotencyKey,
                MessageId = message.Id,
                Status = "reserved",
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddHours(24)
            };
            db.IdempotencyKeys.Add(keyRow);
        }
        else
        {
            keyRow.MessageId = message.Id;
            keyRow.Status = "reserved";
            keyRow.ExpiresAtUtc = now.AddHours(24);
        }
        db.MessageEvents.Add(new MessageEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenancy.TenantId,
            MessageId = message.Id,
            ProviderMessageId = message.ProviderMessageId,
            Direction = "outbound",
            EventType = "queued",
            State = "Queued",
            StatePriority = 10,
            EventTimestampUtc = DateTime.UtcNow,
            RecipientId = message.Recipient,
            CustomerPhone = message.Recipient,
            MessageType = message.MessageType,
            RawPayloadJson = "{}",
            CreatedAtUtc = DateTime.UtcNow
        });

        if (request.Channel == ChannelType.Sms)
        {
            const decimal unitPrice = 1.00m; // Per-message charge model
            db.SmsBillingLedgers.Add(new SmsBillingLedger
            {
                Id = Guid.NewGuid(),
                TenantId = tenancy.TenantId,
                MessageId = message.Id,
                Recipient = message.Recipient,
                ProviderMessageId = string.Empty,
                Currency = "INR",
                UnitPrice = unitPrice,
                Segments = smsSegmentInfo.Segments,
                TotalAmount = decimal.Round(unitPrice * smsSegmentInfo.Segments, 2),
                BillingState = "charged",
                DeliveryState = "submitted",
                Notes = smsSegmentInfo.IsUnicode
                    ? "Charged at enqueue (Unicode SMS segmenting: 70/67)."
                    : "Charged at enqueue (English SMS segmenting: 160/153).",
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        // Auto-create/update contact from outbound sends (WhatsApp/SMS).
        var recipientHash = contactPii.IsEnabled ? contactPii.ComputePhoneHash(request.Recipient) : string.Empty;
        var existingContact = contactPii.IsEnabled
            ? db.Contacts.FirstOrDefault(x => x.TenantId == tenancy.TenantId && x.PhoneHash == recipientHash)
            : db.Contacts.FirstOrDefault(x => x.TenantId == tenancy.TenantId && x.Phone == request.Recipient);
        if (existingContact is null)
        {
            var defaultSegment = db.ContactSegments.FirstOrDefault(x => x.TenantId == tenancy.TenantId && x.Name.ToLower() == "new");
            if (defaultSegment is null)
            {
                defaultSegment = new ContactSegment
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenancy.TenantId,
                    Name = "New",
                    RuleJson = "{}",
                    CreatedAtUtc = DateTime.UtcNow
                };
                db.ContactSegments.Add(defaultSegment);
            }

            var newContact = new Contact
            {
                Id = Guid.NewGuid(),
                TenantId = tenancy.TenantId,
                Name = request.Recipient,
                Phone = request.Recipient,
                SegmentId = defaultSegment.Id,
                TagsCsv = "New",
                OptInStatus = "unknown",
                CreatedAtUtc = DateTime.UtcNow
            };
            contactPii.Protect(newContact);
            db.Contacts.Add(newContact);
            var currentContacts = await db.Contacts.CountAsync(x => x.TenantId == tenancy.TenantId, ct);
            await billingGuard.SetAbsoluteUsageAsync(tenancy.TenantId, "contacts", currentContacts + 1, ct);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(existingContact.Name) && string.IsNullOrWhiteSpace(existingContact.NameEncrypted))
                existingContact.Name = request.Recipient;
            contactPii.Protect(existingContact);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            var existingAfterConflict = await db.Messages
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.IdempotencyKey == idempotencyKey, ct);
            if (existingAfterConflict is not null) return existingAfterConflict;
            throw;
        }
        await queue.EnqueueAsync(new OutboundMessageQueueItem
        {
            MessageId = message.Id,
            TenantId = tenancy.TenantId,
            TenantSlug = tenancy.TenantSlug,
            IdempotencyKey = idempotencyKey
        }, ct);
        return message;
    }

    private readonly record struct SmsSegmentInfo(int Segments, bool IsUnicode);

    private static SmsSegmentInfo EstimateSmsSegments(string text)
    {
        var body = text ?? string.Empty;
        if (body.Length == 0) return new SmsSegmentInfo(1, false);

        var isUnicode = ContainsNonGsm7(body);
        var singleLimit = isUnicode ? 70 : 160;
        var concatLimit = isUnicode ? 67 : 153;

        if (body.Length <= singleLimit) return new SmsSegmentInfo(1, isUnicode);
        var segments = Math.Max(1, (int)Math.Ceiling(body.Length / (double)concatLimit));
        return new SmsSegmentInfo(segments, isUnicode);
    }

    private static bool ContainsNonGsm7(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;

        foreach (var ch in value)
        {
            if (Gsm7BasicChars.Contains(ch) || Gsm7ExtendedChars.Contains(ch))
                continue;
            return true;
        }
        return false;
    }

    private static readonly HashSet<char> Gsm7BasicChars =
    [
        '@', '£', '$', '¥', 'è', 'é', 'ù', 'ì', 'ò', 'Ç', '\n', 'Ø', 'ø', '\r', 'Å', 'å',
        'Δ', '_', 'Φ', 'Γ', 'Λ', 'Ω', 'Π', 'Ψ', 'Σ', 'Θ', 'Ξ', 'Æ', 'æ', 'ß', 'É', ' ',
        '!', '"', '#', '¤', '%', '&', '\'', '(', ')', '*', '+', ',', '-', '.', '/', '0',
        '1', '2', '3', '4', '5', '6', '7', '8', '9', ':', ';', '<', '=', '>', '?', '¡',
        'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P',
        'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z', 'Ä', 'Ö', 'Ñ', 'Ü', '§', '¿',
        'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n', 'o', 'p',
        'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z', 'ä', 'ö', 'ñ', 'ü', 'à'
    ];

    private static readonly HashSet<char> Gsm7ExtendedChars = ['^', '{', '}', '\\', '[', '~', ']', '|', '€'];

    private static string BuildSmsMessageType(string baseType, bool forcePlatformConfig, string senderId, string peId, string templateId)
    {
        return string.Join("|", [
            baseType,
            "smscfg",
            forcePlatformConfig ? "1" : "0",
            Uri.EscapeDataString(senderId ?? string.Empty),
            Uri.EscapeDataString(peId ?? string.Empty),
            Uri.EscapeDataString(templateId ?? string.Empty)
        ]);
    }

    private static object ParseFlowDataObject(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            return doc.RootElement.Clone();
        }
        catch
        {
            return new { };
        }
    }
}
