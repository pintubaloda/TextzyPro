using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;

namespace Textzy.Api.Providers;

public class EquenceSmsMessageProvider(
    IConfiguration config,
    ControlDbContext controlDb,
    SecretCryptoService crypto,
    IHttpClientFactory httpClientFactory,
    ILogger<EquenceSmsMessageProvider> logger) : IMessageProvider
{
    private sealed class GatewayConfig
    {
        public int TimeoutMs { get; init; } = 15000;
        public string BaseUrl { get; init; } = "https://api.equence.in/pushsms";
        public string Username { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public string DefaultSender { get; init; } = string.Empty;
        public string DefaultPeId { get; init; } = string.Empty;
        public string DefaultTemplateId { get; init; } = string.Empty;
    }

    public async Task<string> SendAsync(ChannelType channel, string recipient, string body, SmsSendContext? context = null, CancellationToken ct = default)
    {
        if (channel != ChannelType.Sms)
            throw new InvalidOperationException("Equence provider only supports SMS sends.");

        var gateway = await ResolveGatewayConfigAsync(ct);
        var messageText = body ?? string.Empty;
        var sender = gateway.DefaultSender;
        var peId = gateway.DefaultPeId;
        var templateId = gateway.DefaultTemplateId;

        var parsedTemplate = ParseTemplateBody(body ?? string.Empty);
        var smsConfig = ParseSmsConfigFromMessageType(context?.MessageType);
        if (!string.IsNullOrWhiteSpace(smsConfig.Sender)) sender = smsConfig.Sender;
        if (!string.IsNullOrWhiteSpace(smsConfig.PeId)) peId = smsConfig.PeId;
        if (!string.IsNullOrWhiteSpace(smsConfig.TemplateId)) templateId = smsConfig.TemplateId;
        var allowTenantOverride = !smsConfig.ForcePlatformConfig && !smsConfig.HasOverrideValues;

        if (allowTenantOverride && context is not null && context.TenantId != Guid.Empty)
        {
            var tenant = await controlDb.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == context.TenantId, ct);
            if (tenant is not null)
            {
                using var tenantDb = SeedData.CreateTenantDbContext(tenant.DataConnectionString);
                var tenantSender = await tenantDb.SmsSenders
                    .AsNoTracking()
                    .Where(x => x.TenantId == context.TenantId && x.IsActive)
                    .OrderByDescending(x => x.IsVerified)
                    .ThenBy(x => x.SenderId)
                    .FirstOrDefaultAsync(ct);

                if (tenantSender is not null)
                {
                    sender = string.IsNullOrWhiteSpace(tenantSender.SenderId) ? sender : tenantSender.SenderId;
                    peId = string.IsNullOrWhiteSpace(tenantSender.EntityId) ? peId : tenantSender.EntityId;
                }

                if (string.Equals(context.MessageType, "template", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(parsedTemplate.TemplateName))
                        goto TenantDone;

                    var tpl = await tenantDb.Templates
                        .AsNoTracking()
                        .Where(x => x.TenantId == context.TenantId && x.Channel == ChannelType.Sms && x.Name == parsedTemplate.TemplateName)
                        .Where(x =>
                            string.Equals(x.LifecycleStatus, "approved", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(x.Status, "Approved", StringComparison.OrdinalIgnoreCase))
                        .Where(x =>
                            (!x.EffectiveFromUtc.HasValue || x.EffectiveFromUtc <= DateTime.UtcNow) &&
                            (!x.EffectiveToUtc.HasValue || x.EffectiveToUtc >= DateTime.UtcNow))
                        .OrderByDescending(x => x.CreatedAtUtc)
                        .FirstOrDefaultAsync(ct);

                    if (tpl is not null)
                    {
                        sender = string.IsNullOrWhiteSpace(tpl.SmsSenderId) ? sender : tpl.SmsSenderId;
                        peId = string.IsNullOrWhiteSpace(tpl.DltEntityId) ? peId : tpl.DltEntityId;
                        templateId = string.IsNullOrWhiteSpace(tpl.DltTemplateId) ? templateId : tpl.DltTemplateId;
                        messageText = RenderTemplate(tpl.Body, parsedTemplate.Parameters);
                    }
                }
            }
        }

TenantDone:
        if (string.IsNullOrWhiteSpace(gateway.BaseUrl) || string.IsNullOrWhiteSpace(gateway.Username) || string.IsNullOrWhiteSpace(gateway.Password))
            throw new InvalidOperationException("Equence gateway credentials are missing. Configure platform scope sms-gateway.");
        if (string.IsNullOrWhiteSpace(sender) || string.IsNullOrWhiteSpace(peId) || string.IsNullOrWhiteSpace(templateId))
            throw new InvalidOperationException("Equence SMS blocked: sender / PE_ID / Template_ID is missing.");

        return await SendViaEquenceAsync(recipient, messageText, sender, peId, templateId, gateway, context, ct);
    }

    private async Task<string> SendViaEquenceAsync(string recipient, string messageText, string sender, string peId, string templateId, GatewayConfig gateway, SmsSendContext? context, CancellationToken ct)
    {
        var query = new[]
        {
            $"username={Uri.EscapeDataString(gateway.Username)}",
            $"peId={Uri.EscapeDataString(peId)}",
            $"tmplId={Uri.EscapeDataString(templateId)}",
            $"password={Uri.EscapeDataString(gateway.Password)}",
            $"to={Uri.EscapeDataString(recipient)}",
            $"from={Uri.EscapeDataString(sender)}",
            $"text={Uri.EscapeDataString(messageText)}",
        };

        var url = $"{gateway.BaseUrl.TrimEnd('?')}{(gateway.BaseUrl.Contains('?') ? "&" : "?")}{string.Join("&", query)}";
        var startedAt = DateTime.UtcNow;
        var statusCode = 0;
        var providerId = string.Empty;
        var responseBody = string.Empty;
        var error = string.Empty;
        var isSuccess = false;
        var http = httpClientFactory.CreateClient();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(gateway.TimeoutMs));
            using var res = await http.GetAsync(url, cts.Token);
            statusCode = (int)res.StatusCode;
            responseBody = await res.Content.ReadAsStringAsync(cts.Token);
            if (!res.IsSuccessStatusCode)
            {
                error = $"Equence SMS failed ({statusCode})";
                throw new InvalidOperationException($"{error}: {responseBody}");
            }

            providerId = TryExtractProviderId(responseBody);
            isSuccess = string.Equals(ExtractResponseStatus(responseBody), "success", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(providerId);
            if (!isSuccess)
            {
                error = "Equence SMS not accepted.";
                throw new InvalidOperationException($"{error} {responseBody}");
            }

            logger.LogInformation("Equence SMS accepted recipient={Recipient} providerId={ProviderId}", recipient, providerId);
            return providerId;
        }
        catch (Exception ex)
        {
            if (string.IsNullOrWhiteSpace(error)) error = ex.Message;
            logger.LogWarning(ex, "Equence SMS failed recipient={Recipient}", recipient);
            throw;
        }
        finally
        {
            await SaveGatewayLogAsync(new SmsGatewayRequestLog
            {
                Id = Guid.NewGuid(),
                CreatedAtUtc = DateTime.UtcNow,
                Provider = "equence",
                TenantId = context?.TenantId == Guid.Empty ? null : context?.TenantId,
                Recipient = Truncate(recipient, 64),
                Sender = Truncate(sender, 32),
                PeId = Truncate(peId, 64),
                TemplateId = Truncate(templateId, 64),
                HttpMethod = "GET",
                RequestUrlMasked = Truncate(MaskSensitiveQueryString(url), 4000),
                RequestPayloadMasked = Truncate($"recipient={recipient};sender={sender};peId={peId};templateId={templateId};messageLength={messageText?.Length ?? 0}", 2000),
                HttpStatusCode = statusCode,
                ResponseBody = Truncate(responseBody, 4000),
                IsSuccess = isSuccess,
                Error = Truncate(error, 2000),
                DurationMs = Math.Max(1, (int)(DateTime.UtcNow - startedAt).TotalMilliseconds),
                ProviderMessageId = Truncate(providerId, 256)
            }, ct);
        }
    }

    private async Task SaveGatewayLogAsync(SmsGatewayRequestLog log, CancellationToken ct)
    {
        try
        {
            controlDb.SmsGatewayRequestLogs.Add(log);
            await controlDb.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist SMS gateway request log.");
        }
    }

    private async Task<GatewayConfig> ResolveGatewayConfigAsync(CancellationToken ct)
    {
        var rows = await controlDb.PlatformSettings.AsNoTracking()
            .Where(x => x.Scope == "sms-gateway")
            .ToListAsync(ct);
        var settings = rows.ToDictionary(x => x.Key, x => SafeDecrypt(x.ValueEncrypted), StringComparer.OrdinalIgnoreCase);
        var timeoutRaw = FirstNonEmpty(settings.TryGetValue("timeoutMs", out var timeout) ? timeout : null, config["Sms:Equence:TimeoutMs"], config["Sms:TimeoutMs"]);
        var timeoutMs = int.TryParse(timeoutRaw, out var parsedTimeout) && parsedTimeout > 0 ? parsedTimeout : 15000;

        return new GatewayConfig
        {
            TimeoutMs = timeoutMs,
            BaseUrl = FirstNonEmpty(settings.TryGetValue("equenceBaseUrl", out var baseUrl) ? baseUrl : null, config["Sms:Equence:BaseUrl"], "https://api.equence.in/pushsms") ?? string.Empty,
            Username = FirstNonEmpty(settings.TryGetValue("equenceUsername", out var username) ? username : null, config["Sms:Equence:Username"]) ?? string.Empty,
            Password = FirstNonEmpty(settings.TryGetValue("equencePassword", out var password) ? password : null, config["Sms:Equence:Password"]) ?? string.Empty,
            DefaultSender = FirstNonEmpty(settings.TryGetValue("defaultSenderAddress", out var sender) ? sender : null, config["Sms:Equence:SenderAddress"]) ?? string.Empty,
            DefaultPeId = FirstNonEmpty(settings.TryGetValue("defaultPeId", out var pe) ? pe : null, config["Sms:Equence:PeId"]) ?? string.Empty,
            DefaultTemplateId = FirstNonEmpty(settings.TryGetValue("defaultTemplateId", out var template) ? template : null, config["Sms:Equence:TemplateId"]) ?? string.Empty,
        };
    }

    private string SafeDecrypt(string input)
    {
        try { return crypto.Decrypt(input); } catch { return string.Empty; }
    }

    private static string MaskSensitiveQueryString(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        var masked = Regex.Replace(url, @"([?&]password=)[^&]*", "$1***", RegexOptions.IgnoreCase);
        masked = Regex.Replace(masked, @"([?&]username=)[^&]*", "$1***", RegexOptions.IgnoreCase);
        return masked;
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return value.Length <= max ? value : value[..max];
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private sealed class SmsMessageConfig
    {
        public bool ForcePlatformConfig { get; init; }
        public string Sender { get; init; } = string.Empty;
        public string PeId { get; init; } = string.Empty;
        public string TemplateId { get; init; } = string.Empty;
        public bool HasOverrideValues => !string.IsNullOrWhiteSpace(Sender) || !string.IsNullOrWhiteSpace(PeId) || !string.IsNullOrWhiteSpace(TemplateId);
    }

    private static SmsMessageConfig ParseSmsConfigFromMessageType(string? messageType)
    {
        var raw = (messageType ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw)) return new SmsMessageConfig();
        var parts = raw.Split('|');
        var markerIndex = Array.FindIndex(parts, p => string.Equals(p, "smscfg", StringComparison.OrdinalIgnoreCase));
        if (markerIndex < 0) return new SmsMessageConfig();
        var force = markerIndex + 1 < parts.Length && parts[markerIndex + 1] == "1";
        var sender = markerIndex + 2 < parts.Length ? Uri.UnescapeDataString(parts[markerIndex + 2]) : string.Empty;
        var peId = markerIndex + 3 < parts.Length ? Uri.UnescapeDataString(parts[markerIndex + 3]) : string.Empty;
        var templateId = markerIndex + 4 < parts.Length ? Uri.UnescapeDataString(parts[markerIndex + 4]) : string.Empty;
        return new SmsMessageConfig
        {
            ForcePlatformConfig = force,
            Sender = (sender ?? string.Empty).Trim(),
            PeId = (peId ?? string.Empty).Trim(),
            TemplateId = (templateId ?? string.Empty).Trim()
        };
    }

    private static (string TemplateName, List<string> Parameters) ParseTemplateBody(string body)
    {
        var parts = (body ?? string.Empty).Split('|', 3, StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return (string.Empty, []);
        var templateName = (parts[0] ?? string.Empty).Trim();
        var parameters = new List<string>();
        if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
            parameters = parts[1].Split(',', StringSplitOptions.None).Select(x => (x ?? string.Empty).Trim()).ToList();
        return (templateName, parameters);
    }

    private static string RenderTemplate(string templateBody, IReadOnlyList<string> parameters)
    {
        return Regex.Replace(templateBody ?? string.Empty, @"\{\{(\d+)\}\}", m =>
        {
            if (!int.TryParse(m.Groups[1].Value, out var idx) || idx <= 0) return m.Value;
            var pos = idx - 1;
            if (pos >= parameters.Count) return m.Value;
            return parameters[pos] ?? string.Empty;
        });
    }

    private static string ExtractResponseStatus(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("response", out var response) &&
                response.ValueKind == JsonValueKind.Array &&
                response.GetArrayLength() > 0)
            {
                var first = response[0];
                if (first.TryGetProperty("status", out var status))
                    return status.ToString().Trim();
            }
        }
        catch
        {
        }
        return string.Empty;
    }

    private static string TryExtractProviderId(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("response", out var response) &&
                response.ValueKind == JsonValueKind.Array &&
                response.GetArrayLength() > 0)
            {
                var first = response[0];
                if (first.TryGetProperty("mrid", out var mrid) && !string.IsNullOrWhiteSpace(mrid.ToString()))
                    return $"equence_{mrid}";
                if (first.TryGetProperty("id", out var id) && !string.IsNullOrWhiteSpace(id.ToString()))
                    return $"equence_{id}";
            }
        }
        catch
        {
        }

        return $"equence_{Guid.NewGuid():N}";
    }
}
