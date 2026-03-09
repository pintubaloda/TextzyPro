using System.Text.Json;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Textzy.Api.Data;
using Textzy.Api.DTOs;
using Textzy.Api.Models;

namespace Textzy.Api.Services;

public class WorkflowExecutionEngine(
    ControlDbContext controlDb,
    TenantDbContext tenantDb,
    MessagingService messaging,
    IHttpClientFactory httpClientFactory,
    SecretCryptoService crypto,
    SensitiveDataRedactor redactor,
    IOptions<WorkflowRuntimeOptions> runtimeOptions)
{
    private const int MaxSubflowDepth = 3;
    private sealed class DelayResumePayload
    {
        public Guid RunId { get; init; }
        public Guid VersionId { get; init; }
        public string InboundRecipient { get; init; } = string.Empty;
        public string InboundMessageText { get; init; } = string.Empty;
        public string InboundMessageId { get; init; } = string.Empty;
        public string PhoneNumberId { get; init; } = string.Empty;
        public string DefinitionJson { get; init; } = "{}";
        public string StartNodeId { get; init; } = string.Empty;
        public Dictionary<string, object?> Payload { get; init; } = new(StringComparer.OrdinalIgnoreCase);
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
        public Dictionary<string, string> OptionRoutes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, object?> Config { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class ExecuteRequest
    {
        public required Guid TenantId { get; init; }
        public required Guid FlowId { get; init; }
        public required string PhoneNumberId { get; init; }
        public required string InboundMessageId { get; init; }
        public required string InboundRecipient { get; init; }
        public required string InboundMessageText { get; init; }
        public string InboundMatchKey { get; init; } = string.Empty;
        public required string DefinitionJson { get; init; }
        public required AutomationRun Run { get; init; }
        public required Dictionary<string, object?> Payload { get; init; }
        public bool IsInteractiveResume { get; init; }
        public string ResumeSourceNodeId { get; init; } = string.Empty;
        public string StartNodeId { get; init; } = string.Empty;
    }

    public async Task ExecuteAsync(ExecuteRequest req, CancellationToken ct)
    {
        var (nodes, startNodeId) = ParseFlowDefinition(req.DefinitionJson);
        var trace = new List<object>();
        var log = new List<string>();
        var allowSideEffects = !string.Equals(req.Run.Mode, "simulate", StringComparison.OrdinalIgnoreCase);
        var cursor = !string.IsNullOrWhiteSpace(req.StartNodeId)
            ? req.StartNodeId
            : ResolveResumeNodeId(nodes, startNodeId, req.IsInteractiveResume, req.ResumeSourceNodeId, req.InboundMessageText, req.InboundMatchKey);
        var guard = 0;
        try
        {
            while (!string.IsNullOrWhiteSpace(cursor) && guard < 250)
            {
                guard++;
                if (!nodes.TryGetValue(cursor, out var node))
                {
                    log.Add($"missing-node:{cursor}");
                    break;
                }

                var nodeType = (node.Type ?? string.Empty).Trim().ToLowerInvariant().Replace("-", "_");
                await TryWriteExecutionStateAsync(req, cursor, "running", string.Empty, ct);
                await TryWriteExecutionLogAsync(req, node, "started", 0, "{}", "{}", string.Empty, ct);

                controlDb.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    TenantId = req.TenantId,
                    ActorUserId = Guid.Empty,
                    Action = "waba.workflow.node_exec",
                    Details = $"phoneNumberId={req.PhoneNumberId}; inboundMessageId={req.InboundMessageId}; flowId={req.FlowId}; nodeId={node.Id}; nodeType={nodeType}",
                    CreatedAtUtc = DateTime.UtcNow
                });
                await controlDb.SaveChangesAsync(ct);

                var next = node.Next;
                var outputData = "{}";
                if (nodeType is "start")
                {
                    next = node.Next;
                    outputData = JsonSerializer.Serialize(new { nextNodeId = next });
                }
                else if (nodeType is "capture_input")
                {
                    var variable = ResolveValue(node.Config, req.Payload, "variable", "field", "key");
                    if (string.IsNullOrWhiteSpace(variable)) variable = "user_input";
                    variable = NormalizeVariableName(variable);

                    var rawInput = ResolveValue(node.Config, req.Payload, "value");
                    if (string.IsNullOrWhiteSpace(rawInput))
                        rawInput = ResolveValue(req.Payload, req.Payload, "message");
                    var validationType = ResolveValue(node.Config, req.Payload, "validation");
                    if (string.IsNullOrWhiteSpace(validationType)) validationType = "text";
                    var regexPattern = ResolveValue(node.Config, req.Payload, "regex");
                    var minLenRaw = ResolveValue(node.Config, req.Payload, "minLength");
                    var maxLenRaw = ResolveValue(node.Config, req.Payload, "maxLength");
                    var maxAttemptsRaw = ResolveValue(node.Config, req.Payload, "maxAttempts");
                    var minLen = int.TryParse(minLenRaw, out var m1) && m1 > 0 ? m1 : 0;
                    var maxLen = int.TryParse(maxLenRaw, out var m2) && m2 > 0 ? m2 : 0;
                    var maxAttempts = int.TryParse(maxAttemptsRaw, out var ma) && ma > 0 ? ma : 0;
                    var attemptsKey = $"{variable}_attempts";
                    var attempts = req.Payload.TryGetValue(attemptsKey, out var attemptsRaw) &&
                                   int.TryParse(SafeString(attemptsRaw), out var currentAttempts)
                        ? currentAttempts
                        : 0;

                    var (valid, normalized, reason) = ValidateCapturedInput(rawInput, validationType, regexPattern, minLen, maxLen);
                    req.Payload[variable] = normalized;
                    req.Payload[$"{variable}_valid"] = valid;
                    req.Payload[$"{variable}_reason"] = reason;
                    req.Payload["last_capture_variable"] = variable;
                    req.Payload["last_capture_valid"] = valid;
                    req.Payload["last_capture_reason"] = reason;
                    if (valid) attempts = 0;
                    else attempts += 1;
                    req.Payload[attemptsKey] = attempts;
                    req.Payload["last_capture_attempts"] = attempts;
                    req.Payload[$"{variable}_max_attempts"] = maxAttempts;

                    if (valid)
                    {
                        next = node.OnTrue ?? node.OnSuccess ?? node.Next;
                    }
                    else if (maxAttempts > 0 && attempts >= maxAttempts)
                    {
                        next = node.OnFailure ?? node.OnFalse ?? node.Next;
                    }
                    else
                    {
                        next = node.OnFalse ?? node.OnFailure ?? node.Next;
                    }
                    outputData = JsonSerializer.Serialize(new
                    {
                        variable,
                        validation = validationType,
                        valid,
                        reason,
                        attempts,
                        maxAttempts,
                        nextNodeId = next
                    });
                }
                else if (nodeType is "text" or "text_message" or "textmessage" or "send_text" or "message" or "ask_question" or "bot_reply" or "botreply" or "buttons" or "list" or "cta_url" or "media" or "location" or "form")
                {
                    var recipient = ResolveValue(node.Config, req.Payload, "recipient");
                    if (string.IsNullOrWhiteSpace(recipient)) recipient = req.InboundRecipient;

                    var body = string.Empty;
                    var interactiveButtons = new List<string>();
                    try
                    {
                        body = ResolveNodeReplyText(nodeType, node.Config, req.Payload);
                    }
                    catch
                    {
                        body = ResolveValue(node.Config, req.Payload, "simpleText", "body", "message", "question", "prompt");
                    }

                    if (nodeType is "buttons")
                    {
                        if (node.Config.TryGetValue("buttons", out var btns))
                            interactiveButtons = ReadStringOptions(btns, 3);
                        body = ResolveValue(node.Config, req.Payload, "body", "message", "question", "prompt");
                    }
                    else if (nodeType is "bot_reply" or "botreply")
                    {
                        var replyMode = ResolveValue(node.Config, req.Payload, "replyMode");
                        var advancedType = ResolveValue(node.Config, req.Payload, "advancedType");
                        if (string.Equals(replyMode, "advanced", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(advancedType, "quick_reply", StringComparison.OrdinalIgnoreCase) &&
                            node.Config.TryGetValue("buttons", out var btns))
                        {
                            interactiveButtons = ReadStringOptions(btns, 3);
                            body = ResolveValue(node.Config, req.Payload, "simpleText", "body", "message", "question", "prompt");
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(recipient) && !string.IsNullOrWhiteSpace(body) && allowSideEffects)
                    {
                        try
                        {
                            var messageReq = new SendMessageRequest
                            {
                                Recipient = recipient,
                                Body = Interpolate(body, req.Payload),
                                Channel = ChannelType.WhatsApp,
                                IdempotencyKey = $"auto-msg:{req.FlowId}:{req.InboundMessageId}:{node.Id}"
                            };
                            if (interactiveButtons.Count > 0)
                            {
                                messageReq.IsInteractive = true;
                                messageReq.InteractiveType = "button";
                                messageReq.InteractiveButtons = interactiveButtons.Select(x => Interpolate(x, req.Payload)).ToList();
                            }
                            var msg = await messaging.EnqueueAsync(messageReq, ct);
                            outputData = JsonSerializer.Serialize(new
                            {
                                recipient,
                                enqueuedMessageId = msg.Id,
                                providerMessageId = msg.ProviderMessageId,
                                interactive = interactiveButtons.Count > 0,
                                buttons = interactiveButtons
                            });
                            controlDb.AuditLogs.Add(new AuditLog
                            {
                                Id = Guid.NewGuid(),
                                TenantId = req.TenantId,
                                ActorUserId = Guid.Empty,
                                Action = "waba.workflow.enqueued",
                                Details = $"phoneNumberId={req.PhoneNumberId}; inboundMessageId={req.InboundMessageId}; flowId={req.FlowId}; nodeId={node.Id}; messageId={msg.Id}; recipient={recipient}",
                                CreatedAtUtc = DateTime.UtcNow
                            });
                            await controlDb.SaveChangesAsync(ct);
                        }
                        catch (Exception ex)
                        {
                            controlDb.AuditLogs.Add(new AuditLog
                            {
                                Id = Guid.NewGuid(),
                                TenantId = req.TenantId,
                                ActorUserId = Guid.Empty,
                                Action = "waba.workflow.enqueue_failed",
                                Details = $"phoneNumberId={req.PhoneNumberId}; inboundMessageId={req.InboundMessageId}; flowId={req.FlowId}; nodeId={node.Id}; error={redactor.RedactText(ex.Message)}",
                                CreatedAtUtc = DateTime.UtcNow
                            });
                            await controlDb.SaveChangesAsync(ct);
                            throw;
                        }
                    }
                    else
                    {
                        outputData = JsonSerializer.Serialize(new
                        {
                            recipient,
                            bodyLength = body?.Length ?? 0,
                            skipped = true,
                            reason = !allowSideEffects ? "simulate_mode" : "empty_recipient_or_body"
                        });
                        controlDb.AuditLogs.Add(new AuditLog
                        {
                            Id = Guid.NewGuid(),
                            TenantId = req.TenantId,
                            ActorUserId = Guid.Empty,
                            Action = "waba.workflow.send_skipped",
                            Details = $"phoneNumberId={req.PhoneNumberId}; inboundMessageId={req.InboundMessageId}; flowId={req.FlowId}; nodeId={node.Id}; nodeType={nodeType}; reason={(allowSideEffects ? "empty_recipient_or_body" : "simulate_mode")}",
                            CreatedAtUtc = DateTime.UtcNow
                        });
                        await controlDb.SaveChangesAsync(ct);
                    }
                    next = node.OnSuccess ?? node.Next;
                }
                else if (nodeType == "template")
                {
                    var recipient = ResolveValue(node.Config, req.Payload, "recipient");
                    var templateName = ResolveValue(node.Config, req.Payload, "templateName", "template_name");
                    var languageCode = ResolveValue(node.Config, req.Payload, "languageCode", "language");
                    var body = ResolveValue(node.Config, req.Payload, "body", "message");
                    var paramValues = new List<string>();
                    if (node.Config.TryGetValue("parameters", out var p))
                    {
                        if (p is IEnumerable<object?> arr)
                        {
                            foreach (var item in arr)
                                paramValues.Add(Interpolate(SafeString(item), req.Payload));
                        }
                        else if (p is JsonElement pElem && pElem.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in pElem.EnumerateArray())
                                paramValues.Add(Interpolate(SafeString(item), req.Payload));
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(recipient) && !string.IsNullOrWhiteSpace(templateName) && allowSideEffects)
                    {
                        var msg = await messaging.EnqueueAsync(new SendMessageRequest
                        {
                            Recipient = recipient,
                            Body = body,
                            Channel = ChannelType.WhatsApp,
                            UseTemplate = true,
                            TemplateName = templateName,
                            TemplateLanguageCode = string.IsNullOrWhiteSpace(languageCode) ? "en" : languageCode,
                            TemplateParameters = paramValues,
                            IdempotencyKey = $"auto-tpl:{req.FlowId}:{req.InboundMessageId}:{node.Id}"
                        }, ct);
                        outputData = JsonSerializer.Serialize(new
                        {
                            recipient,
                            templateName,
                            languageCode = string.IsNullOrWhiteSpace(languageCode) ? "en" : languageCode,
                            paramCount = paramValues.Count,
                            enqueuedMessageId = msg.Id,
                            providerMessageId = msg.ProviderMessageId
                        });
                        controlDb.AuditLogs.Add(new AuditLog
                        {
                            Id = Guid.NewGuid(),
                            TenantId = req.TenantId,
                            ActorUserId = Guid.Empty,
                            Action = "waba.workflow.enqueued",
                            Details = $"phoneNumberId={req.PhoneNumberId}; inboundMessageId={req.InboundMessageId}; flowId={req.FlowId}; nodeId={node.Id}; messageId={msg.Id}; recipient={recipient}; template={templateName}",
                            CreatedAtUtc = DateTime.UtcNow
                        });
                        await controlDb.SaveChangesAsync(ct);
                    }
                    else
                    {
                        outputData = JsonSerializer.Serialize(new
                        {
                            recipient,
                            templateName,
                            skipped = true,
                            reason = !allowSideEffects ? "simulate_mode" : "empty_recipient_or_template"
                        });
                    }
                    next = node.OnSuccess ?? node.Next;
                }
                else if (nodeType is "condition" or "split")
                {
                    var ok = EvaluateCondition(node.Config, req.Payload);
                    next = ok ? (node.OnTrue ?? node.OnSuccess ?? node.Next) : (node.OnFalse ?? node.OnFailure ?? node.Next);
                    outputData = JsonSerializer.Serialize(new
                    {
                        field = ResolveValue(node.Config, req.Payload, "field"),
                        @operator = ResolveValue(node.Config, req.Payload, "operator"),
                        value = ResolveValue(node.Config, req.Payload, "value"),
                        matched = ok,
                        nextNodeId = next
                    });
                }
                else if (nodeType is "assign_agent" or "assignagent" or "handoff")
                {
                    if (allowSideEffects)
                        await TryAssignConversationAsync(req, node, ct);
                    next = node.OnSuccess ?? node.Next;
                    outputData = JsonSerializer.Serialize(new
                    {
                        queue = ResolveValue(node.Config, req.Payload, "queue", "team", "department"),
                        assignedUserId = ResolveValue(node.Config, req.Payload, "userId", "agentId", "assignedUserId"),
                        assignedUserName = ResolveValue(node.Config, req.Payload, "userName", "agentName", "assignedUserName"),
                        nextNodeId = next
                    });
                }
                else if (nodeType is "request_intervention" or "requesthelp")
                {
                    if (allowSideEffects)
                        await TryAssignConversationAsync(req, node, ct);
                    var escalationMessage = ResolveValue(node.Config, req.Payload, "message", "body");
                    if (allowSideEffects && !string.IsNullOrWhiteSpace(escalationMessage) && !string.IsNullOrWhiteSpace(req.InboundRecipient))
                    {
                        await messaging.EnqueueAsync(new SendMessageRequest
                        {
                            Recipient = req.InboundRecipient,
                            Body = Interpolate(escalationMessage, req.Payload),
                            Channel = ChannelType.WhatsApp,
                            IdempotencyKey = $"auto-escalation:{req.FlowId}:{req.InboundMessageId}:{node.Id}"
                        }, ct);
                    }
                    next = node.OnSuccess ?? node.Next;
                    outputData = JsonSerializer.Serialize(new
                    {
                        queue = ResolveValue(node.Config, req.Payload, "queue", "team", "department"),
                        escalationMessageLength = escalationMessage?.Length ?? 0,
                        nextNodeId = next
                    });
                }
                else if (nodeType is "tag_user" or "taguser")
                {
                    var labelsAdded = allowSideEffects ? await TryTagConversationAsync(req, node, ct) : 0;
                    next = node.OnSuccess ?? node.Next;
                    outputData = JsonSerializer.Serialize(new
                    {
                        labelsAdded,
                        nextNodeId = next
                    });
                }
                else if (nodeType is "webhook" or "api_call")
                {
                    var webhookResult = allowSideEffects
                        ? await TryInvokeWebhookAsync(req, node, ct)
                        : (statusCode: 0, success: true, savedAs: "simulate_mode");
                    next = node.OnSuccess ?? node.Next;
                    outputData = JsonSerializer.Serialize(new
                    {
                        webhookResult.statusCode,
                        webhookResult.success,
                        webhookResult.savedAs,
                        nextNodeId = next
                    });
                }
                else if (nodeType == "subflow")
                {
                    var subflowIdRaw = ResolveValue(node.Config, req.Payload, "subflowId", "flowId");
                    if (!Guid.TryParse(subflowIdRaw, out var subflowId) || subflowId == Guid.Empty)
                        throw new InvalidOperationException("Subflow node requires valid subflowId.");

                    var depth = 0;
                    if (req.Payload.TryGetValue("__subflow_depth", out var existingDepth))
                        int.TryParse(SafeString(existingDepth), out depth);
                    if (depth >= MaxSubflowDepth)
                        throw new InvalidOperationException($"Subflow max depth {MaxSubflowDepth} reached.");

                    var subflow = await tenantDb.AutomationFlows
                        .FirstOrDefaultAsync(x => x.TenantId == req.TenantId && x.Id == subflowId, ct);
                    if (subflow is null)
                        throw new InvalidOperationException("Subflow not found.");

                    var subflowVersionId = subflow.PublishedVersionId ?? subflow.CurrentVersionId ?? Guid.Empty;
                    if (subflowVersionId == Guid.Empty)
                        throw new InvalidOperationException("Subflow version missing.");
                    var subflowVersion = await tenantDb.AutomationFlowVersions
                        .FirstOrDefaultAsync(x => x.TenantId == req.TenantId && x.FlowId == subflowId && x.Id == subflowVersionId, ct);
                    if (subflowVersion is null)
                        throw new InvalidOperationException("Subflow version not found.");

                    req.Payload["__subflow_depth"] = depth + 1;
                    var subflowStartNodeId = ResolveValue(node.Config, req.Payload, "startNodeId");
                    await ExecuteAsync(new ExecuteRequest
                    {
                        TenantId = req.TenantId,
                        FlowId = subflowId,
                        PhoneNumberId = req.PhoneNumberId,
                        InboundMessageId = req.InboundMessageId,
                        InboundRecipient = req.InboundRecipient,
                        InboundMessageText = req.InboundMessageText,
                        InboundMatchKey = req.InboundMatchKey,
                        DefinitionJson = subflowVersion.DefinitionJson,
                        Run = req.Run,
                        Payload = req.Payload,
                        IsInteractiveResume = false,
                        ResumeSourceNodeId = string.Empty,
                        StartNodeId = subflowStartNodeId
                    }, ct);
                    req.Payload["__subflow_depth"] = depth;

                    next = node.OnSuccess ?? node.Next;
                    outputData = JsonSerializer.Serialize(new
                    {
                        subflowId,
                        subflowVersionId,
                        nextNodeId = next
                    });
                }
                else if (nodeType == "jump")
                {
                    var jumpTarget = ResolveValue(node.Config, req.Payload, "targetNodeId", "to", "target");
                    if (!string.IsNullOrWhiteSpace(jumpTarget))
                        next = jumpTarget;
                    else
                        next = node.OnSuccess ?? node.Next;
                    outputData = JsonSerializer.Serialize(new { jumpTarget = next });
                }
                else if (nodeType is "delay" or "wait")
                {
                    var delaySeconds = ParseDelaySeconds(node.Config, req.Payload);
                    if (delaySeconds <= 0)
                    {
                        next = node.OnSuccess ?? node.Next;
                        outputData = JsonSerializer.Serialize(new { delaySeconds, inline = false, skipped = true, nextNodeId = next });
                    }
                    else if (!allowSideEffects)
                    {
                        next = node.OnSuccess ?? node.Next;
                        outputData = JsonSerializer.Serialize(new { delaySeconds, inline = false, skipped = true, reason = "simulate_mode", nextNodeId = next });
                    }
                    else if (delaySeconds <= runtimeOptions.Value.InlineDelayMaxSeconds)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
                        next = node.OnSuccess ?? node.Next;
                        outputData = JsonSerializer.Serialize(new { delaySeconds, inline = true, nextNodeId = next });
                    }
                    else
                    {
                        await TryScheduleResumeAsync(req, node, delaySeconds, node.OnSuccess ?? node.Next, ct);
                        outputData = JsonSerializer.Serialize(new
                        {
                            delaySeconds,
                            scheduled = true,
                            nextNodeId = node.OnSuccess ?? node.Next
                        });
                        req.Run.Status = "scheduled";
                        req.Run.Log = string.Join('\n', log.Append($"delay:{node.Id}:{delaySeconds}s"));
                        req.Run.TraceJson = JsonSerializer.Serialize(trace);
                        req.Run.CompletedAtUtc = DateTime.UtcNow;
                        await TryWriteExecutionStateAsync(req, node.Id, "scheduled", string.Empty, ct);
                        await TryWriteExecutionLogAsync(req, node, "completed", 0, "{}", outputData, string.Empty, ct);
                        break;
                    }
                }
                else if (nodeType == "end")
                {
                    trace.Add(new { nodeId = node.Id, nodeType, nextNodeId = "", status = "ok" });
                    outputData = JsonSerializer.Serialize(new { ended = true });
                    await TryWriteExecutionLogAsync(req, node, "completed", 0, "{}", outputData, string.Empty, ct);
                    break;
                }
                else
                {
                    next = node.OnSuccess ?? node.Next;
                    outputData = JsonSerializer.Serialize(new { nextNodeId = next });
                }

                trace.Add(new { nodeId = node.Id, nodeType, nextNodeId = next, status = "ok" });
                log.Add($"{nodeType}:{node.Id}->{next}");
                await TryWriteExecutionLogAsync(req, node, "completed", 0, "{}", outputData, string.Empty, ct);
                if (string.IsNullOrWhiteSpace(next)) break;
                cursor = next;
            }

            if (!string.Equals(req.Run.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
            {
                req.Run.Status = "completed";
                req.Run.Log = string.Join('\n', log);
                req.Run.TraceJson = JsonSerializer.Serialize(trace);
                req.Run.CompletedAtUtc = DateTime.UtcNow;
                await TryWriteExecutionStateAsync(req, cursor, "completed", string.Empty, ct);
            }
        }
        catch (Exception ex)
        {
            req.Run.Status = "failed";
            req.Run.FailureReason = ex.Message;
            req.Run.Log = string.Join('\n', log);
            req.Run.TraceJson = JsonSerializer.Serialize(trace);
            req.Run.CompletedAtUtc = DateTime.UtcNow;
            await TryWriteExecutionStateAsync(req, cursor, "failed", ex.Message, ct);
            controlDb.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = req.TenantId,
                ActorUserId = Guid.Empty,
                Action = "waba.workflow.execution_failed",
                Details = $"phoneNumberId={req.PhoneNumberId}; inboundMessageId={req.InboundMessageId}; flowId={req.FlowId}; error={redactor.RedactText(ex.Message)}",
                CreatedAtUtc = DateTime.UtcNow
            });
            await controlDb.SaveChangesAsync(ct);
        }
    }

    private async Task TryAssignConversationAsync(ExecuteRequest req, FlowNode node, CancellationToken ct)
    {
        var queue = ResolveValue(node.Config, req.Payload, "queue", "team", "department");
        var assignedUserId = ResolveValue(node.Config, req.Payload, "userId", "agentId", "assignedUserId");
        var assignedUserName = ResolveValue(node.Config, req.Payload, "userName", "agentName", "assignedUserName");

        var convo = await tenantDb.Conversations
            .FirstOrDefaultAsync(x => x.TenantId == req.TenantId && x.CustomerPhone == req.InboundRecipient, ct);
        if (convo is null) return;

        if (!string.IsNullOrWhiteSpace(assignedUserId))
            convo.AssignedUserId = assignedUserId;
        if (!string.IsNullOrWhiteSpace(assignedUserName))
            convo.AssignedUserName = assignedUserName;
        if (!string.IsNullOrWhiteSpace(queue) && string.IsNullOrWhiteSpace(convo.AssignedUserName))
            convo.AssignedUserName = queue;
        convo.Status = "Open";
        convo.LastMessageAtUtc = DateTime.UtcNow;

        var actor = string.IsNullOrWhiteSpace(queue) ? "workflow" : $"workflow ({queue})";
        tenantDb.Messages.Add(new Message
        {
            Id = Guid.NewGuid(),
            TenantId = req.TenantId,
            Channel = ChannelType.WhatsApp,
            Recipient = req.InboundRecipient,
            Body = $"Conversation assigned to {(string.IsNullOrWhiteSpace(convo.AssignedUserName) ? "Unassigned" : convo.AssignedUserName)} by {actor}.",
            MessageType = "system_event",
            Status = "Sent",
            QueueProvider = "workflow",
            ProviderMessageId = string.Empty,
            IdempotencyKey = $"sys:workflow-assign:{req.FlowId}:{req.InboundMessageId}:{node.Id}"
        });
    }

    private async Task<int> TryTagConversationAsync(ExecuteRequest req, FlowNode node, CancellationToken ct)
    {
        var convo = await tenantDb.Conversations
            .FirstOrDefaultAsync(x => x.TenantId == req.TenantId && x.CustomerPhone == req.InboundRecipient, ct);
        if (convo is null) return 0;

        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(convo.LabelsCsv))
        {
            foreach (var item in convo.LabelsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                labels.Add(item.Trim());
        }

        if (node.Config.TryGetValue("tags", out var rawTags) && rawTags is IEnumerable<object?> arr)
        {
            foreach (var item in arr)
            {
                var v = Interpolate(SafeString(item), req.Payload).Trim();
                if (!string.IsNullOrWhiteSpace(v)) labels.Add(v);
            }
        }
        else
        {
            var one = ResolveValue(node.Config, req.Payload, "tag", "label");
            if (!string.IsNullOrWhiteSpace(one)) labels.Add(one.Trim());
        }

        convo.LabelsCsv = string.Join(",", labels.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        convo.LastMessageAtUtc = DateTime.UtcNow;
        return labels.Count;
    }

    private async Task<(int statusCode, bool success, string savedAs)> TryInvokeWebhookAsync(ExecuteRequest req, FlowNode node, CancellationToken ct)
    {
        var method = ResolveValue(node.Config, req.Payload, "method");
        if (string.IsNullOrWhiteSpace(method)) method = "POST";
        var url = ResolveValue(node.Config, req.Payload, "url", "endpoint");
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("Webhook node requires url.");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Webhook url must be absolute https.");
        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Webhook url localhost is not allowed.");
        if (uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Webhook .local domains are not allowed.");
        if (IPAddress.TryParse(uri.Host, out var ip) && IsPrivateOrLoopback(ip))
            throw new InvalidOperationException("Webhook private/loopback IP is not allowed.");
        await EnforceWebhookHostAllowListAsync(req.TenantId, uri.Host, ct);

        var body = ResolveValue(node.Config, req.Payload, "body", "payload");
        var saveAs = ResolveValue(node.Config, req.Payload, "saveAs");
        if (string.IsNullOrWhiteSpace(saveAs)) saveAs = "webhook_response";

        using var reqMsg = new HttpRequestMessage(new HttpMethod(method.ToUpperInvariant()), uri);
        if (!string.IsNullOrWhiteSpace(body))
            reqMsg.Content = new StringContent(Interpolate(body, req.Payload), System.Text.Encoding.UTF8, "application/json");

        if (node.Config.TryGetValue("headers", out var headersRaw))
        {
            foreach (var pair in ReadStringMap(headersRaw))
            {
                if (string.IsNullOrWhiteSpace(pair.Key)) continue;
                if (!reqMsg.Headers.TryAddWithoutValidation(pair.Key, Interpolate(pair.Value, req.Payload)))
                {
                    reqMsg.Content ??= new StringContent(string.Empty);
                    reqMsg.Content.Headers.TryAddWithoutValidation(pair.Key, Interpolate(pair.Value, req.Payload));
                }
            }
        }

        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        using var res = await client.SendAsync(reqMsg, ct);
        var responseText = await res.Content.ReadAsStringAsync(ct);

        req.Payload[$"{saveAs}_status"] = (int)res.StatusCode;
        req.Payload[$"{saveAs}_body"] = responseText;
        req.Payload[$"{saveAs}_ok"] = res.IsSuccessStatusCode;

        return ((int)res.StatusCode, res.IsSuccessStatusCode, saveAs);
    }

    private async Task EnforceWebhookHostAllowListAsync(Guid tenantId, string host, CancellationToken ct)
    {
        var entry = await controlDb.PlatformSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Scope == "mobile-app" && x.Key == "webhookAllowedHosts", ct);
        if (entry is null || string.IsNullOrWhiteSpace(entry.ValueEncrypted))
            return;

        var raw = crypto.Decrypt(entry.ValueEncrypted);
        var allowed = ParseStringArray(raw);
        if (allowed.Count == 0) return;

        var normalizedHost = host.Trim().ToLowerInvariant();
        var isAllowed = allowed.Any(a =>
        {
            var x = a.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(x)) return false;
            return normalizedHost == x || normalizedHost.EndsWith("." + x, StringComparison.OrdinalIgnoreCase);
        });

        if (!isAllowed)
        {
            throw new InvalidOperationException($"Webhook host '{host}' is not in allow-list.");
        }
    }

    private async Task TryScheduleResumeAsync(ExecuteRequest req, FlowNode node, int delaySeconds, string nextNodeId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(nextNodeId)) return;
        var scheduledAt = DateTime.UtcNow.AddSeconds(delaySeconds);
        var payload = new DelayResumePayload
        {
            RunId = req.Run.Id,
            VersionId = req.Run.VersionId ?? Guid.Empty,
            InboundRecipient = req.InboundRecipient,
            InboundMessageText = req.InboundMessageText,
            InboundMessageId = req.InboundMessageId,
            PhoneNumberId = req.PhoneNumberId,
            DefinitionJson = req.DefinitionJson,
            StartNodeId = nextNodeId,
            Payload = req.Payload
        };
        tenantDb.WorkflowScheduledMessages.Add(new WorkflowScheduledMessage
        {
            Id = Guid.NewGuid(),
            TenantId = req.TenantId,
            FlowId = req.FlowId,
            ConversationId = Guid.Empty,
            NodeId = nextNodeId,
            ScheduledForUtc = scheduledAt,
            MessageContent = JsonSerializer.Serialize(payload),
            Status = "pending",
            RetryCount = 0,
            MaxRetries = Math.Max(1, runtimeOptions.Value.DelayMaxRetries),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        controlDb.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = req.TenantId,
            ActorUserId = Guid.Empty,
            Action = "waba.workflow.delay_scheduled",
            Details = $"phoneNumberId={req.PhoneNumberId}; inboundMessageId={req.InboundMessageId}; flowId={req.FlowId}; nodeId={node.Id}; nextNodeId={nextNodeId}; delaySeconds={delaySeconds}",
            CreatedAtUtc = DateTime.UtcNow
        });
        await controlDb.SaveChangesAsync(ct);
    }

    private static int ParseDelaySeconds(Dictionary<string, object?> config, Dictionary<string, object?> payload)
    {
        static int parse(object? raw)
        {
            if (raw is null) return 0;
            var text = SafeString(raw);
            if (string.IsNullOrWhiteSpace(text)) return 0;
            if (int.TryParse(text, out var direct) && direct >= 0) return direct;
            if (double.TryParse(text, out var asDouble) && asDouble >= 0) return (int)Math.Round(asDouble);
            return 0;
        }

        var seconds = parse(config.TryGetValue("delaySeconds", out var ds) ? ds : null);
        if (seconds > 0) return seconds;
        var minutes = parse(config.TryGetValue("delayMinutes", out var dm) ? dm : null);
        if (minutes > 0) return minutes * 60;
        var hours = parse(config.TryGetValue("delayHours", out var dh) ? dh : null);
        if (hours > 0) return hours * 3600;
        var generic = parse(config.TryGetValue("value", out var val) ? val : null);
        if (generic > 0)
        {
            var unit = ResolveValue(config, payload, "unit");
            return unit switch
            {
                "hour" or "hours" => generic * 3600,
                "minute" or "minutes" => generic * 60,
                _ => generic
            };
        }
        return 0;
    }

    private async Task TryWriteExecutionStateAsync(ExecuteRequest req, string currentNodeId, string status, string errorMessage, CancellationToken ct)
    {
        if (!runtimeOptions.Value.EnableExecutionState) return;
        try
        {
            await tenantDb.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "WorkflowExecutionStates"
                ("Id","TenantId","FlowId","ConversationId","CurrentNodeId","ExecutionData","Status","StartedAtUtc","LastUpdatedAtUtc","CompletedAtUtc","ExecutionTrace","ErrorMessage")
                VALUES
                ({req.Run.Id},{req.TenantId},{req.FlowId},{(Guid?)null},{currentNodeId ?? string.Empty},{JsonSerializer.Serialize(req.Payload)},{status},{req.Run.StartedAtUtc},{DateTime.UtcNow},{(status == "completed" || status == "failed" ? DateTime.UtcNow : (DateTime?)null)},"[]",{errorMessage ?? string.Empty})
                ON CONFLICT ("Id") DO UPDATE SET
                    "CurrentNodeId" = EXCLUDED."CurrentNodeId",
                    "Status" = EXCLUDED."Status",
                    "LastUpdatedAtUtc" = EXCLUDED."LastUpdatedAtUtc",
                    "CompletedAtUtc" = EXCLUDED."CompletedAtUtc",
                    "ErrorMessage" = EXCLUDED."ErrorMessage"
                """, ct);
        }
        catch
        {
            // keep execution safe even when state table is missing in old tenant schema
        }
    }

    private async Task TryWriteExecutionLogAsync(ExecuteRequest req, FlowNode node, string status, int durationMs, string inputData, string outputData, string errorMessage, CancellationToken ct)
    {
        if (!runtimeOptions.Value.EnableExecutionState) return;
        try
        {
            await tenantDb.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "WorkflowExecutionLogs"
                ("Id","TenantId","ExecutionStateId","NodeId","NodeType","NodeName","ExecutedAtUtc","Status","DurationMs","InputData","OutputData","ErrorMessage")
                VALUES
                ({Guid.NewGuid()},{req.TenantId},{req.Run.Id},{node.Id},{node.Type ?? string.Empty},{ResolveValue(node.Config, req.Payload, "title", "name")},{DateTime.UtcNow},{status},{durationMs},{inputData},{outputData},{errorMessage ?? string.Empty})
                """, ct);
        }
        catch
        {
            // keep execution safe even when log table is missing in old tenant schema
        }
    }

    private static string ResolveResumeNodeId(
        IReadOnlyDictionary<string, FlowNode> nodes,
        string startNodeId,
        bool isInteractiveReply,
        string resumeSourceNodeId,
        string inboundText,
        string inboundMatchKey)
    {
        if (!isInteractiveReply) return startNodeId;
        var normalizedMatchKey = NormalizeMatchKey(inboundMatchKey);
        var normalizedInbound = NormalizeMatchKey(inboundText);
        if (!string.IsNullOrWhiteSpace(resumeSourceNodeId) &&
            nodes.TryGetValue(resumeSourceNodeId, out var sourceNode))
        {
            if (!string.IsNullOrWhiteSpace(normalizedMatchKey) &&
                sourceNode.OptionRoutes.TryGetValue(normalizedMatchKey, out var directByKey) &&
                !string.IsNullOrWhiteSpace(directByKey))
            {
                return directByKey;
            }
            if (!string.IsNullOrWhiteSpace(normalizedInbound) &&
                sourceNode.OptionRoutes.TryGetValue(normalizedInbound, out var directNext) &&
                !string.IsNullOrWhiteSpace(directNext))
            {
                return directNext;
            }
            if (!string.IsNullOrWhiteSpace(sourceNode.Next)) return sourceNode.Next;
        }

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

    private static string NormalizeMatchKey(string? input)
    {
        var value = NormalizeInboundMessageText(input);
        var chars = value.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray();
        return new string(chars).Trim().ToLowerInvariant();
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
                    ? (labelNode.ToString() ?? string.Empty).Trim()
                    : string.Empty;
                var normalizedLabel = NormalizeMatchKey(label);
                if (normalizedLabel == "true")
                {
                    if (string.IsNullOrWhiteSpace(source.OnTrue)) source.OnTrue = to;
                    continue;
                }
                if (normalizedLabel == "false")
                {
                    if (string.IsNullOrWhiteSpace(source.OnFalse)) source.OnFalse = to;
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(normalizedLabel))
                {
                    source.OptionRoutes[normalizedLabel] = to;
                    if (string.IsNullOrWhiteSpace(source.Next)) source.Next = to;
                    continue;
                }
                if (string.IsNullOrWhiteSpace(source.Next)) source.Next = to;
            }
        }

        // Add alias routes for interactive IDs/payloads when edge labels are mapped by title.
        foreach (var node in nodes.Values)
        {
            if (node.OptionRoutes.Count == 0) continue;
            foreach (var option in ExtractInteractiveOptionAliases(node.Config))
            {
                if (string.IsNullOrWhiteSpace(option.label)) continue;
                var normalizedLabel = NormalizeMatchKey(option.label);
                if (string.IsNullOrWhiteSpace(normalizedLabel)) continue;
                if (!node.OptionRoutes.TryGetValue(normalizedLabel, out var target)) continue;
                if (!string.IsNullOrWhiteSpace(option.id))
                    node.OptionRoutes[NormalizeMatchKey(option.id)] = target;
                if (!string.IsNullOrWhiteSpace(option.payload))
                    node.OptionRoutes[NormalizeMatchKey(option.payload)] = target;
            }
        }

        if (string.IsNullOrWhiteSpace(startNodeId) && nodes.Count > 0) startNodeId = nodes.Keys.First();
        return (nodes, startNodeId);
    }

    private static IEnumerable<(string label, string id, string payload)> ExtractInteractiveOptionAliases(Dictionary<string, object?> config)
    {
        foreach (var key in new[] { "buttons", "listItems", "items" })
        {
            if (!config.TryGetValue(key, out var raw) || raw is null) continue;
            if (raw is IEnumerable<object?> arr)
            {
                foreach (var item in arr)
                {
                    if (item is IDictionary<string, object?> map)
                    {
                        var label = map.TryGetValue("title", out var t) ? SafeString(t) : string.Empty;
                        var id = map.TryGetValue("id", out var i) ? SafeString(i) : string.Empty;
                        var payload = map.TryGetValue("payload", out var p) ? SafeString(p) : string.Empty;
                        if (!string.IsNullOrWhiteSpace(label) || !string.IsNullOrWhiteSpace(id) || !string.IsNullOrWhiteSpace(payload))
                            yield return (label, id, payload);
                    }
                }
            }
        }
    }

    private static object? ToClrValue(JsonElement element)
    {
        try
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
                _ => element.GetRawText()
            };
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool EvaluateCondition(Dictionary<string, object?> config, Dictionary<string, object?> payload)
    {
        var field = config.TryGetValue("field", out var f) ? SafeString(f) : string.Empty;
        var @operator = config.TryGetValue("operator", out var op) ? SafeString(op).ToLowerInvariant() : "equals";
        var expected = config.TryGetValue("value", out var v) ? SafeString(v) : string.Empty;
        var actual = payload.TryGetValue(field, out var a) ? SafeString(a) : string.Empty;
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
            output = output.Replace($"{{{{{pair.Key}}}}}", SafeString(pair.Value), StringComparison.OrdinalIgnoreCase);
        }
        return output;
    }

    private static string ResolveValue(Dictionary<string, object?> config, Dictionary<string, object?> payload, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (config.TryGetValue(key, out var raw))
            {
                var val = SafeString(raw);
                if (!string.IsNullOrWhiteSpace(val)) return Interpolate(val, payload);
            }
            if (payload.TryGetValue(key, out var fromPayload))
            {
                var val = SafeString(fromPayload);
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }
        }
        return string.Empty;
    }

    private static string SafeString(object? raw)
    {
        if (raw is null) return string.Empty;
        if (raw is string s) return s;
        if (raw is JsonElement j)
        {
            try
            {
                return j.ValueKind switch
                {
                    JsonValueKind.String => j.GetString() ?? string.Empty,
                    JsonValueKind.Null => string.Empty,
                    JsonValueKind.Undefined => string.Empty,
                    _ => j.GetRawText()
                };
            }
            catch
            {
                return string.Empty;
            }
        }
        try
        {
            return raw.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
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
                    try
                    {
                        if (item is IDictionary<string, object?> map)
                        {
                            var title = map.TryGetValue("title", out var t) ? SafeString(t) : null;
                            var subtitle = map.TryGetValue("subtitle", out var s) ? SafeString(s) : null;
                            var merged = string.IsNullOrWhiteSpace(subtitle) ? title : $"{title} - {subtitle}";
                            if (!string.IsNullOrWhiteSpace(merged)) list.Add(merged.Trim());
                            continue;
                        }
                        var v = SafeString(item).Trim();
                        if (!string.IsNullOrWhiteSpace(v)) list.Add(v);
                    }
                    catch
                    {
                        // skip malformed option item
                    }
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
                                ? SafeString(title)
                                : SafeString(it);
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

        if (nodeType == "location")
        {
            var prompt = ResolveValue(config, payload, "prompt", "body", "message", "question");
            return string.IsNullOrWhiteSpace(prompt) ? "Please share your location." : prompt;
        }

        if (nodeType == "form")
        {
            return BuildFormPrompt(config, payload);
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

    private static string BuildFormPrompt(Dictionary<string, object?> config, Dictionary<string, object?> payload)
    {
        var title = ResolveValue(config, payload, "title", "prompt", "body", "message");
        var labels = ReadFormFieldLabels(config.TryGetValue("fields", out var rawFields) ? rawFields : null);
        if (labels.Count == 0)
            return string.IsNullOrWhiteSpace(title) ? "Please share the requested details." : title;

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(title)) lines.Add(title);
        lines.Add("Please reply with:");
        for (var index = 0; index < labels.Count; index++)
            lines.Add($"{index + 1}. {labels[index]}");
        return string.Join("\n", lines);
    }

    private static List<string> ReadFormFieldLabels(object? rawFields)
    {
        var labels = new List<string>();
        if (rawFields is JsonElement json && json.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in json.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var label = item.TryGetProperty("label", out var labelNode) ? labelNode.GetString() ?? string.Empty : string.Empty;
                var key = item.TryGetProperty("key", out var keyNode) ? keyNode.GetString() ?? string.Empty : string.Empty;
                var required = item.TryGetProperty("required", out var requiredNode) && requiredNode.ValueKind == JsonValueKind.True;
                var display = !string.IsNullOrWhiteSpace(label) ? label : key;
                if (string.IsNullOrWhiteSpace(display)) continue;
                labels.Add(required ? $"{display} (required)" : display);
            }
            return labels;
        }

        if (rawFields is IEnumerable<object?> items)
        {
            foreach (var item in items)
            {
                if (item is JsonElement element)
                {
                    labels.AddRange(ReadFormFieldLabels(element));
                    continue;
                }

                var text = SafeString(item).Trim();
                if (!string.IsNullOrWhiteSpace(text)) labels.Add(text);
            }
        }

        return labels;
    }

    private static Dictionary<string, string> ReadStringMap(object? raw)
    {
        var outMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (raw is null) return outMap;

        if (raw is IDictionary<string, object?> dict)
        {
            foreach (var kv in dict)
                outMap[kv.Key] = SafeString(kv.Value);
            return outMap;
        }

        if (raw is JsonElement j && j.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in j.EnumerateObject())
                outMap[prop.Name] = SafeString(prop.Value);
            return outMap;
        }

        return outMap;
    }

    private static List<string> ParseStringArray(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        try
        {
            if (raw.TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                var fromJson = JsonSerializer.Deserialize<string[]>(raw);
                if (fromJson is not null)
                    return fromJson.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
        }
        catch
        {
            // fallback to csv/newline
        }

        return raw
            .Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsPrivateOrLoopback(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b[0] == 10) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            if (b[0] == 169 && b[1] == 254) return true;
        }
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return true;
        }
        return false;
    }

    private static string NormalizeVariableName(string raw)
    {
        var cleaned = new string((raw ?? string.Empty).Trim().Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray());
        cleaned = cleaned.Trim('_');
        while (cleaned.Contains("__", StringComparison.Ordinal)) cleaned = cleaned.Replace("__", "_", StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(cleaned) ? "user_input" : cleaned.ToLowerInvariant();
    }

    private static (bool valid, string normalized, string reason) ValidateCapturedInput(
        string rawInput,
        string validationType,
        string regexPattern,
        int minLength,
        int maxLength)
    {
        var input = (rawInput ?? string.Empty).Trim();
        if (minLength > 0 && input.Length < minLength) return (false, input, "min_length");
        if (maxLength > 0 && input.Length > maxLength) return (false, input, "max_length");

        var v = (validationType ?? "text").Trim().ToLowerInvariant();
        if (v == "text" || string.IsNullOrWhiteSpace(v)) return (!string.IsNullOrWhiteSpace(input), input, string.IsNullOrWhiteSpace(input) ? "empty" : "ok");
        if (v == "email")
        {
            var ok = System.Text.RegularExpressions.Regex.IsMatch(input, @"^[^\s@]+@[^\s@]+\.[^\s@]+$");
            return (ok, input.ToLowerInvariant(), ok ? "ok" : "invalid_email");
        }
        if (v is "number" or "numeric" or "int")
        {
            var ok = decimal.TryParse(input, out _);
            return (ok, input, ok ? "ok" : "invalid_number");
        }
        if (v is "phone" or "mobile")
        {
            var digits = new string(input.Where(char.IsDigit).ToArray());
            var ok = digits.Length is >= 8 and <= 15;
            return (ok, digits, ok ? "ok" : "invalid_phone");
        }
        if (v == "regex")
        {
            if (string.IsNullOrWhiteSpace(regexPattern)) return (true, input, "ok");
            try
            {
                var ok = System.Text.RegularExpressions.Regex.IsMatch(input, regexPattern);
                return (ok, input, ok ? "ok" : "regex_no_match");
            }
            catch
            {
                return (false, input, "regex_invalid");
            }
        }

        return (!string.IsNullOrWhiteSpace(input), input, string.IsNullOrWhiteSpace(input) ? "empty" : "ok");
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
                try
                {
                    if (item is IDictionary<string, object?> map)
                    {
                        var title = map.TryGetValue("title", out var t) ? SafeString(t) : null;
                        var subtitle = map.TryGetValue("subtitle", out var s) ? SafeString(s) : null;
                        var merged = string.IsNullOrWhiteSpace(subtitle) ? title : $"{title} - {subtitle}";
                        if (!string.IsNullOrWhiteSpace(merged)) result.Add(merged.Trim());
                        continue;
                    }
                    var v = SafeString(item).Trim();
                    if (!string.IsNullOrWhiteSpace(v)) result.Add(v);
                }
                catch
                {
                    // skip malformed option item
                }
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
                            ? SafeString(title)
                            : SafeString(it);
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
}
