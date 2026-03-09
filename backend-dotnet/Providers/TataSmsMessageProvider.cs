using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;

namespace Textzy.Api.Providers;

public class TataSmsMessageProvider(
    IConfiguration config,
    ControlDbContext controlDb,
    SecretCryptoService crypto,
    IHttpClientFactory httpClientFactory,
    ILogger<TataSmsMessageProvider> logger) : IMessageProvider
{
    private sealed class GatewayConfig
    {
        public string Provider { get; init; } = "tata";
        public int TimeoutMs { get; init; } = 15000;
        public string TataBaseUrl { get; init; } = string.Empty;
        public string TataUsername { get; init; } = string.Empty;
        public string TataPassword { get; init; } = string.Empty;
        public string DefaultSender { get; init; } = string.Empty;
        public string DefaultPeId { get; init; } = string.Empty;
        public string DefaultTemplateId { get; init; } = string.Empty;
    }

    public async Task<string> SendAsync(ChannelType channel, string recipient, string body, SmsSendContext? context = null, CancellationToken ct = default)
    {
        if (channel != ChannelType.Sms)
            throw new InvalidOperationException("TATA provider only supports SMS sends.");

        var gateway = await ResolveGatewayConfigAsync(ct);
        var provider = gateway.Provider;
        if (provider != "tata")
            throw new InvalidOperationException("SMS gateway provider is not configured as tata.");

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

                // Tenant sender master has priority over platform fallback.
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

        if (string.IsNullOrWhiteSpace(gateway.TataBaseUrl) || string.IsNullOrWhiteSpace(gateway.TataUsername) || string.IsNullOrWhiteSpace(gateway.TataPassword))
            throw new InvalidOperationException("TATA gateway credentials are missing. Configure platform scope sms-gateway.");
        if (string.IsNullOrWhiteSpace(sender) || string.IsNullOrWhiteSpace(peId) || string.IsNullOrWhiteSpace(templateId))
            throw new InvalidOperationException("TATA SMS blocked: senderAddress / PE_ID / Template_ID is missing.");
        return await SendViaTataAsync(recipient, messageText, sender, peId, templateId, gateway, context, ct);
    }

    private async Task<string> SendViaTataAsync(
        string recipient,
        string messageText,
        string sender,
        string peId,
        string templateId,
        GatewayConfig gateway,
        SmsSendContext? context,
        CancellationToken ct)
    {
        var query = new[]
        {
            $"recipient={Uri.EscapeDataString(recipient)}",
            "dr=false",
            $"msg={Uri.EscapeDataString(messageText)}",
            $"user={Uri.EscapeDataString(gateway.TataUsername)}",
            $"pswd={Uri.EscapeDataString(gateway.TataPassword)}",
            $"sender={Uri.EscapeDataString(sender)}",
            $"PE_ID={Uri.EscapeDataString(peId)}",
            $"Template_ID={Uri.EscapeDataString(templateId)}",
        };

        var url = $"{gateway.TataBaseUrl.TrimEnd('?')}{(gateway.TataBaseUrl.Contains('?') ? "&" : "?")}{string.Join("&", query)}";
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
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var res = await http.SendAsync(req, cts.Token);
            statusCode = (int)res.StatusCode;
            responseBody = await res.Content.ReadAsStringAsync(cts.Token);
            if (!res.IsSuccessStatusCode)
            {
                error = $"TATA SMS failed ({(int)res.StatusCode})";
                throw new InvalidOperationException($"{error}: {responseBody}");
            }

            providerId = TryExtractProviderId(responseBody, "tata");
            isSuccess = true;
            logger.LogInformation("TATA SMS accepted recipient={Recipient} providerId={ProviderId}", recipient, providerId);
            return providerId;
        }
        catch (Exception ex)
        {
            if (string.IsNullOrWhiteSpace(error)) error = ex.Message;
            logger.LogWarning(ex, "TATA SMS failed recipient={Recipient}", recipient);
            throw;
        }
        finally
        {
            await SaveGatewayLogAsync(new SmsGatewayRequestLog
            {
                Id = Guid.NewGuid(),
                CreatedAtUtc = DateTime.UtcNow,
                Provider = "tata",
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

    private static string MaskSensitiveQueryString(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        var masked = Regex.Replace(url, @"([?&]pswd=)[^&]*", "$1***", RegexOptions.IgnoreCase);
        masked = Regex.Replace(masked, @"([?&]user=)[^&]*", "$1***", RegexOptions.IgnoreCase);
        return masked;
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (value.Length <= max) return value;
        return value[..max];
    }

    private sealed class SmsMessageConfig
    {
        public bool ForcePlatformConfig { get; init; }
        public string Sender { get; init; } = string.Empty;
        public string PeId { get; init; } = string.Empty;
        public string TemplateId { get; init; } = string.Empty;
        public bool HasOverrideValues =>
            !string.IsNullOrWhiteSpace(Sender) ||
            !string.IsNullOrWhiteSpace(PeId) ||
            !string.IsNullOrWhiteSpace(TemplateId);
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

    private async Task<GatewayConfig> ResolveGatewayConfigAsync(CancellationToken ct)
    {
        var rows = await controlDb.PlatformSettings.AsNoTracking()
            .Where(x => x.Scope == "sms-gateway")
            .ToListAsync(ct);
        var settings = rows.ToDictionary(x => x.Key, x => SafeDecrypt(x.ValueEncrypted), StringComparer.OrdinalIgnoreCase);

        var provider = FirstNonEmpty(settings.TryGetValue("provider", out var providerRaw) ? providerRaw : null, config["Sms:Provider"], "tata")!
            .Trim().ToLowerInvariant();
        var tataBaseUrl = FirstNonEmpty(settings.TryGetValue("tataBaseUrl", out var s1) ? s1 : null, config["Sms:Tata:BaseUrl"], "https://smsgw.tatatel.co.in:9095/campaignService/campaigns/qs");
        var tataUsername = FirstNonEmpty(settings.TryGetValue("tataUsername", out var s2) ? s2 : null, config["Sms:Tata:Username"]);
        var tataPassword = FirstNonEmpty(settings.TryGetValue("tataPassword", out var s3) ? s3 : null, config["Sms:Tata:Password"]);
        var sender = FirstNonEmpty(settings.TryGetValue("defaultSenderAddress", out var s4) ? s4 : null, config["Sms:Tata:SenderAddress"]);
        var peId = FirstNonEmpty(settings.TryGetValue("defaultPeId", out var s5) ? s5 : null, config["Sms:Tata:PeId"]);
        var templateId = FirstNonEmpty(settings.TryGetValue("defaultTemplateId", out var s6) ? s6 : null, config["Sms:Tata:TemplateId"]);
        var timeoutRaw = FirstNonEmpty(settings.TryGetValue("timeoutMs", out var s7) ? s7 : null, config["Sms:Tata:TimeoutMs"], config["Sms:TimeoutMs"]);
        var timeoutMs = int.TryParse(timeoutRaw, out var parsedTimeout) && parsedTimeout > 0 ? parsedTimeout : 15000;

        return new GatewayConfig
        {
            Provider = provider,
            TimeoutMs = timeoutMs,
            TataBaseUrl = tataBaseUrl ?? string.Empty,
            TataUsername = tataUsername ?? string.Empty,
            TataPassword = tataPassword ?? string.Empty,
            DefaultSender = sender ?? string.Empty,
            DefaultPeId = peId ?? string.Empty,
            DefaultTemplateId = templateId ?? string.Empty
        };
    }

    private string SafeDecrypt(string input)
    {
        try { return crypto.Decrypt(input); } catch { return string.Empty; }
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

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

    private static string TryExtractProviderId(string response, string provider)
    {
        if (string.IsNullOrWhiteSpace(response))
            return $"{provider}_{Guid.NewGuid():N}";
        var m = Regex.Match(response, @"(?i)(jobid|job_id|campaignid|campaign_id|msgid|messageid|custmid|custm_id|id)\s*[:=]\s*([A-Za-z0-9\-_]+)");
        if (m.Success) return $"{provider}_{m.Groups[2].Value}";

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(response);
            var root = doc.RootElement;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var key in new[] { "jobId", "jobid", "job_id", "campaignId", "campaign_id", "cusTmId", "custmId", "request_id", "requestId", "message", "id", "msgid" })
                {
                    if (root.TryGetProperty(key, out var val))
                    {
                        var text = val.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                            return $"{provider}_{text}";
                    }
                }
            }
        }
        catch
        {
            // ignore JSON parse errors
        }
        return $"{provider}_{Guid.NewGuid():N}";
    }
}
