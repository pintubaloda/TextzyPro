using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/platform/settings")]
public class PlatformSettingsController(
    ControlDbContext db,
    SecretCryptoService crypto,
    AuditLogService audit,
    AuthContext auth,
    RbacService rbac,
    IConfiguration config,
    IHttpClientFactory httpClientFactory) : ControllerBase
{
    private static readonly TimeSpan StepUpFreshWindow = TimeSpan.FromMinutes(10);

    [HttpGet("{scope}")]
    public async Task<IActionResult> GetScope(string scope, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();
        try
        {
            scope = InputGuardService.RequireTrimmed(scope, "Scope", 80).ToLowerInvariant();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        var entries = await db.PlatformSettings
            .Where(x => x.Scope == scope)
            .ToListAsync(ct);

        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
            data[e.Key] = crypto.Decrypt(e.ValueEncrypted);

        return Ok(new { scope, values = data });
    }

    [HttpPut("{scope}")]
    public async Task<IActionResult> UpsertScope(string scope, [FromBody] Dictionary<string, string> values, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsWrite)) return Forbid();
        try
        {
            scope = InputGuardService.RequireTrimmed(scope, "Scope", 80).ToLowerInvariant();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        if (values.Count == 0) return BadRequest("At least one key is required.");
        if (values.Count > 200) return BadRequest("Too many settings in one request.");
        if (!HasFreshStepUp())
        {
            var action = scope switch
            {
                "payment-gateway" => "payment_settings_change",
                "api-integration" => "api_credentials_write",
                _ => "platform_settings_write"
            };
            return StatusCode(StatusCodes.Status428PreconditionRequired, new
            {
                stepUpRequired = true,
                action,
                title = "Verify before saving",
                message = "Enter your authenticator code to save platform settings."
            });
        }

        foreach (var kv in values)
        {
            string key;
            try
            {
                key = InputGuardService.RequireTrimmed(kv.Key, "Key", 120);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            var value = kv.Value ?? string.Empty;
            if (value.Length > 12000) return BadRequest($"Value too long for key '{key}'.");

            var row = await db.PlatformSettings.FirstOrDefaultAsync(x => x.Scope == scope && x.Key == key, ct);
            if (row is null)
            {
                row = new PlatformSetting
                {
                    Id = Guid.NewGuid(),
                    Scope = scope,
                    Key = key
                };
                db.PlatformSettings.Add(row);
            }

            row.ValueEncrypted = crypto.Encrypt(value);
            row.UpdatedByUserId = auth.UserId;
            row.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("platform.settings.updated", $"scope={scope}; keys={string.Join(",", values.Keys)}", ct);
        return Ok(new { scope, updated = values.Keys.Count });
    }

    private bool HasFreshStepUp()
        => auth.StepUpVerifiedAtUtc.HasValue && auth.StepUpVerifiedAtUtc.Value >= DateTime.UtcNow.Subtract(StepUpFreshWindow);

    [HttpPost("smtp/test")]
    public async Task<IActionResult> TestSmtp([FromBody] SmtpTestRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsWrite)) return Forbid();

        var values = await db.PlatformSettings
            .Where(x => x.Scope == "smtp")
            .ToDictionaryAsync(x => x.Key, x => crypto.Decrypt(x.ValueEncrypted), StringComparer.OrdinalIgnoreCase, ct);

        var provider = Pick(values, "provider", config["Email:Provider"], config["EMAIL_PROVIDER"], config["Smtp:Provider"], config["SMTP_PROVIDER"], "smtp").ToLowerInvariant();
        var host = Pick(values, "host", config["Smtp:Host"], config["SMTP_HOST"]);
        var portRaw = Pick(values, "port", config["Smtp:Port"], config["SMTP_PORT"], "587");
        var timeoutRaw = Pick(values, "timeoutMs", config["Smtp:TimeoutMs"], config["SMTP_TIMEOUT_MS"], "15000");
        var username = Pick(values, "username", config["Smtp:Username"], config["SMTP_USERNAME"]);
        var password = Pick(values, "password", config["Smtp:Password"], config["SMTP_PASSWORD"]);
        var fromEmail = Pick(values, "fromEmail", config["Smtp:FromEmail"], config["SMTP_FROM_EMAIL"]);
        var fromName = Pick(values, "fromName", config["Smtp:FromName"], config["SMTP_FROM_NAME"], "Textzy");
        var enableSslRaw = Pick(values, "enableSsl", config["Smtp:EnableSsl"], config["SMTP_ENABLE_SSL"], "true");
        var resendApiKey = Pick(values, "resendApiKey", config["Resend:ApiKey"], config["RESEND_API_KEY"]);
        var resendFromEmail = Pick(values, "resendFromEmail", fromEmail);
        var resendFromName = Pick(values, "resendFromName", fromName, "Textzy");

        var toEmail = (request.Email ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(toEmail))
            return BadRequest("Test recipient email is required.");
        try
        {
            toEmail = InputGuardService.RequireTrimmed(toEmail, "Email", 320);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        var timeoutMs = ParseTimeout(timeoutRaw);

        try
        {
            if (provider == "resend")
            {
                if (string.IsNullOrWhiteSpace(resendApiKey) || string.IsNullOrWhiteSpace(resendFromEmail))
                    return BadRequest("Resend apiKey/fromEmail not configured.");

                var payload = new
                {
                    from = string.IsNullOrWhiteSpace(resendFromName) ? resendFromEmail : $"{resendFromName} <{resendFromEmail}>",
                    to = new[] { toEmail },
                    subject = "Textzy Resend Test",
                    html = $"<p>This is a test email from Textzy platform settings.</p><p>Sent at: {DateTime.UtcNow:O} UTC</p><p>Provider: Resend API</p>",
                    text = $"This is a test email from Textzy platform settings.\nSent at: {DateTime.UtcNow:O} UTC\nProvider: Resend API"
                };

                using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", resendApiKey);
                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(timeoutMs);

                var http = httpClientFactory.CreateClient();
                var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
                var body = await res.Content.ReadAsStringAsync(timeoutCts.Token);
                if (!res.IsSuccessStatusCode)
                    throw new InvalidOperationException($"Resend API failed ({(int)res.StatusCode}): {body}");

                await audit.WriteAsync("platform.email.test.success", $"provider=resend; to={toEmail}", ct);
                return Ok(new { ok = true, provider = "resend", message = "Resend test email sent." });
            }

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromEmail))
                return BadRequest("SMTP host/fromEmail not configured.");

            var msg = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = "Textzy SMTP Test",
                Body = $"""
                    This is a test email from Textzy platform settings.

                    Sent at: {DateTime.UtcNow:O} UTC
                    Host: {host}
                    """,
                IsBodyHtml = false
            };
            msg.To.Add(new MailAddress(toEmail));

            using var client = new SmtpClient(host, int.TryParse(portRaw, out var p) ? p : 587)
            {
                EnableSsl = bool.TryParse(enableSslRaw, out var ssl) ? ssl : true,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };
            client.Timeout = timeoutMs;
            if (!string.IsNullOrWhiteSpace(username))
                client.Credentials = new NetworkCredential(username, password ?? string.Empty);

            using var smtpTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            smtpTimeoutCts.CancelAfter(timeoutMs);
            using var reg = smtpTimeoutCts.Token.Register(() => client.SendAsyncCancel());
            var sendTask = client.SendMailAsync(msg);
            var completed = await Task.WhenAny(sendTask, Task.Delay(timeoutMs, ct));
            if (completed != sendTask)
                throw new TimeoutException($"SMTP test timed out after {timeoutMs}ms.");
            await sendTask;

            await audit.WriteAsync("platform.email.test.success", $"provider=smtp; to={toEmail}", ct);
            return Ok(new { ok = true, provider = "smtp", message = "SMTP test email sent." });
        }
        catch (Exception ex)
        {
            await audit.WriteAsync("platform.email.test.failed", $"provider={provider}; to={toEmail}; err={ex.Message}", ct);
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }
    }

    [HttpPost("smtp/diagnose")]
    public async Task<IActionResult> DiagnoseSmtp([FromBody] SmtpDiagnoseRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsWrite)) return Forbid();

        var values = await db.PlatformSettings
            .Where(x => x.Scope == "smtp")
            .ToDictionaryAsync(x => x.Key, x => crypto.Decrypt(x.ValueEncrypted), StringComparer.OrdinalIgnoreCase, ct);

        var provider = Pick(values, "provider", config["Email:Provider"], config["EMAIL_PROVIDER"], config["Smtp:Provider"], config["SMTP_PROVIDER"], "smtp").ToLowerInvariant();
        var host = Pick(values, "host", config["Smtp:Host"], config["SMTP_HOST"]);
        var portRaw = Pick(values, "port", config["Smtp:Port"], config["SMTP_PORT"], "587");
        var timeoutRaw = Pick(values, "timeoutMs", config["Smtp:TimeoutMs"], config["SMTP_TIMEOUT_MS"], "15000");
        var enableSslRaw = Pick(values, "enableSsl", config["Smtp:EnableSsl"], config["SMTP_ENABLE_SSL"], "true");
        var resendApiKey = Pick(values, "resendApiKey", config["Resend:ApiKey"], config["RESEND_API_KEY"]);

        if (!string.IsNullOrWhiteSpace(request.Provider)) provider = request.Provider.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(request.Host)) host = request.Host.Trim();
        if (!string.IsNullOrWhiteSpace(request.Port)) portRaw = request.Port.Trim();
        if (!string.IsNullOrWhiteSpace(request.TimeoutMs)) timeoutRaw = request.TimeoutMs.Trim();
        if (!string.IsNullOrWhiteSpace(request.EnableSsl)) enableSslRaw = request.EnableSsl.Trim();

        var port = int.TryParse(portRaw, out var p) ? p : 587;
        var timeoutMs = ParseTimeout(timeoutRaw);
        var useSsl = bool.TryParse(enableSslRaw, out var ssl) ? ssl : true;
        var result = new Dictionary<string, object?>
        {
            ["provider"] = provider,
            ["host"] = host,
            ["port"] = port,
            ["timeoutMs"] = timeoutMs,
            ["enableSsl"] = useSsl,
            ["dnsResolved"] = false,
            ["tcpConnected"] = false,
            ["smtpBanner"] = null,
            ["startTlsAccepted"] = null,
            ["tlsHandshake"] = false,
            ["stage"] = "init",
            ["error"] = null
        };

        try
        {
            if (provider == "resend")
            {
                if (string.IsNullOrWhiteSpace(resendApiKey))
                    return BadRequest("Resend apiKey not configured.");

                host = "api.resend.com";
                result["host"] = host;
                result["port"] = 443;
                result["enableSsl"] = true;

                var dnsTaskResend = Dns.GetHostAddressesAsync(host);
                var dnsCompletedResend = await Task.WhenAny(dnsTaskResend, Task.Delay(timeoutMs, ct));
                if (dnsCompletedResend != dnsTaskResend) throw new TimeoutException("DNS lookup timed out.");
                var resendAddrs = dnsTaskResend.Result;
                if (resendAddrs is null || resendAddrs.Length == 0) throw new InvalidOperationException("No IP address resolved for Resend host.");
                result["dnsResolved"] = true;
                result["resolvedAddresses"] = resendAddrs.Select(a => a.ToString()).Take(4).ToArray();
                result["stage"] = "dns_ok";

                using var tcpResend = new TcpClient();
                var connTaskResend = tcpResend.ConnectAsync(host, 443);
                var connCompletedResend = await Task.WhenAny(connTaskResend, Task.Delay(timeoutMs, ct));
                if (connCompletedResend != connTaskResend) throw new TimeoutException("TCP connect timed out.");
                await connTaskResend;
                result["tcpConnected"] = true;
                result["stage"] = "tcp_ok";

                using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.resend.com/domains");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", resendApiKey);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(timeoutMs);

                var http = httpClientFactory.CreateClient();
                var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
                var body = await res.Content.ReadAsStringAsync(timeoutCts.Token);
                result["httpsStatus"] = (int)res.StatusCode;
                result["tlsHandshake"] = true;
                result["stage"] = "https_ok";

                if (!res.IsSuccessStatusCode)
                    throw new InvalidOperationException($"Resend API auth/connectivity failed: {(int)res.StatusCode} {body}");

                result["stage"] = "auth_ok";
                await audit.WriteAsync("platform.smtp.diagnose.success", $"provider=resend; host={host};port=443", ct);
                return Ok(new { ok = true, result });
            }

            if (string.IsNullOrWhiteSpace(host))
                return BadRequest("SMTP host not configured.");

            var dnsTask = Dns.GetHostAddressesAsync(host);
            var dnsCompleted = await Task.WhenAny(dnsTask, Task.Delay(timeoutMs, ct));
            if (dnsCompleted != dnsTask) throw new TimeoutException("DNS lookup timed out.");
            var addrs = dnsTask.Result;
            if (addrs is null || addrs.Length == 0) throw new InvalidOperationException("No IP address resolved for SMTP host.");
            result["dnsResolved"] = true;
            result["resolvedAddresses"] = addrs.Select(a => a.ToString()).Take(4).ToArray();
            result["stage"] = "dns_ok";

            using var tcp = new TcpClient();
            var connTask = tcp.ConnectAsync(host, port);
            var connCompleted = await Task.WhenAny(connTask, Task.Delay(timeoutMs, ct));
            if (connCompleted != connTask) throw new TimeoutException("TCP connect timed out.");
            await connTask;
            result["tcpConnected"] = true;
            result["stage"] = "tcp_ok";

            using var stream = tcp.GetStream();
            stream.ReadTimeout = timeoutMs;
            stream.WriteTimeout = timeoutMs;
            var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            var writer = new StreamWriter(stream, Encoding.ASCII, 1024, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };

            var banner = await ReadLineWithTimeoutAsync(reader, timeoutMs, ct);
            result["smtpBanner"] = banner;
            result["stage"] = "smtp_banner_ok";

            if (port == 465)
            {
                using var sslStream = new SslStream(stream, false, (_, _, _, _) => true);
                var sslTask = sslStream.AuthenticateAsClientAsync(host);
                var sslCompleted = await Task.WhenAny(sslTask, Task.Delay(timeoutMs, ct));
                if (sslCompleted != sslTask) throw new TimeoutException("TLS handshake timed out on port 465.");
                await sslTask;
                result["tlsHandshake"] = true;
                result["stage"] = "tls_ok";
            }
            else
            {
                await writer.WriteLineAsync("EHLO textzy.local");
                var ehloLines = await ReadSmtpResponseAsync(reader, timeoutMs, ct);
                result["ehlo"] = ehloLines;
                var supportsStartTls = ehloLines.Any(x => x.Contains("STARTTLS", StringComparison.OrdinalIgnoreCase));
                result["startTlsSupported"] = supportsStartTls;
                if (useSsl && supportsStartTls)
                {
                    await writer.WriteLineAsync("STARTTLS");
                    var startTlsResp = await ReadLineWithTimeoutAsync(reader, timeoutMs, ct);
                    var accepted = startTlsResp.StartsWith("220", StringComparison.OrdinalIgnoreCase);
                    result["startTlsAccepted"] = accepted;
                    if (!accepted) throw new InvalidOperationException($"STARTTLS rejected: {startTlsResp}");
                    using var sslStream = new SslStream(stream, false, (_, _, _, _) => true);
                    var sslTask = sslStream.AuthenticateAsClientAsync(host);
                    var sslCompleted = await Task.WhenAny(sslTask, Task.Delay(timeoutMs, ct));
                    if (sslCompleted != sslTask) throw new TimeoutException("TLS handshake timed out after STARTTLS.");
                    await sslTask;
                    result["tlsHandshake"] = true;
                    result["stage"] = "tls_ok";
                }
                else
                {
                    result["stage"] = "ehlo_ok";
                }
            }

            await audit.WriteAsync("platform.smtp.diagnose.success", $"host={host};port={port}", ct);
            return Ok(new { ok = true, result });
        }
        catch (Exception ex)
        {
            result["error"] = ex.Message;
            result["stage"] = "failed";
            await audit.WriteAsync("platform.smtp.diagnose.failed", $"host={host};port={port}; err={ex.Message}", ct);
            return StatusCode(StatusCodes.Status502BadGateway, new { ok = false, result });
        }
    }

    [HttpPost("sms/test")]
    public async Task<IActionResult> TestSms([FromBody] SmsTestRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsWrite)) return Forbid();

        var values = await db.PlatformSettings
            .Where(x => x.Scope == "sms-gateway")
            .ToDictionaryAsync(x => x.Key, x => crypto.Decrypt(x.ValueEncrypted), StringComparer.OrdinalIgnoreCase, ct);

        var provider = "tata";
        var timeoutMs = ParseTimeout(Pick(values, "timeoutMs", config["Sms:TimeoutMs"], "15000"));
        var recipient = InputGuardService.ValidatePhone(request.Phone, "Phone");
        var message = string.IsNullOrWhiteSpace(request.Message)
            ? "Textzy SMS gateway test message."
            : InputGuardService.RequireTrimmed(request.Message, "Message", 1024);

        try
        {
            var tataBaseUrl = Pick(values, "tataBaseUrl", config["Sms:Tata:BaseUrl"], "https://smsgw.tatatel.co.in:9095/campaignService/campaigns/qs");
            var tataUsername = Pick(values, "tataUsername", config["Sms:Tata:Username"]);
            var tataPassword = Pick(values, "tataPassword", config["Sms:Tata:Password"]);
            var sender = Pick(values, "defaultSenderAddress", config["Sms:Tata:SenderAddress"]);
            var peId = Pick(values, "defaultPeId", config["Sms:Tata:PeId"]);
            var templateId = Pick(values, "defaultTemplateId", config["Sms:Tata:TemplateId"], request.TemplateId);

            if (string.IsNullOrWhiteSpace(tataUsername) || string.IsNullOrWhiteSpace(tataPassword) ||
                string.IsNullOrWhiteSpace(sender) || string.IsNullOrWhiteSpace(peId) || string.IsNullOrWhiteSpace(templateId))
            {
                return BadRequest("TATA settings missing. Require username, password, senderAddress, PEID, TemplateID.");
            }

            var query = new[]
            {
                $"recipient={Uri.EscapeDataString(recipient)}",
                "dr=false",
                $"msg={Uri.EscapeDataString(message)}",
                $"user={Uri.EscapeDataString(tataUsername)}",
                $"pswd={Uri.EscapeDataString(tataPassword)}",
                $"sender={Uri.EscapeDataString(sender)}",
                $"PE_ID={Uri.EscapeDataString(peId)}",
                $"Template_ID={Uri.EscapeDataString(templateId)}",
            };
            var url = $"{tataBaseUrl.TrimEnd('?')}{(tataBaseUrl.Contains('?') ? "&" : "?")}{string.Join("&", query)}";

            using var smsTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            smsTimeoutCts.CancelAfter(timeoutMs);
            var http = httpClientFactory.CreateClient();
            using var resTata = await http.GetAsync(url, smsTimeoutCts.Token);
            var rawBody = await resTata.Content.ReadAsStringAsync(smsTimeoutCts.Token);
            if (!resTata.IsSuccessStatusCode)
                throw new InvalidOperationException($"TATA test failed ({(int)resTata.StatusCode}): {rawBody}");

            await audit.WriteAsync("platform.sms.test.success", $"provider=tata; to={recipient}", ct);
            return Ok(new { ok = true, provider = "tata", message = "TATA test SMS submitted.", raw = rawBody });
        }
        catch (Exception ex)
        {
            await audit.WriteAsync("platform.sms.test.failed", $"provider={provider}; to={recipient}; err={ex.Message}", ct);
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }
    }

    [HttpPost("payment-gateway/test")]
    public async Task<IActionResult> TestPaymentGateway([FromBody] PaymentGatewayTestRequest? request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsWrite)) return Forbid();

        var values = await db.PlatformSettings
            .Where(x => x.Scope == "payment-gateway")
            .ToDictionaryAsync(x => x.Key, x => crypto.Decrypt(x.ValueEncrypted), StringComparer.OrdinalIgnoreCase, ct);

        var provider = (Pick(values, "provider", request?.Provider, "razorpay") ?? string.Empty).Trim().ToLowerInvariant();
        if (provider != "razorpay")
            return BadRequest("Only Razorpay test is supported in this build.");

        var mode = ((Pick(values, "mode", "test") ?? "test").Trim().ToLowerInvariant() == "live") ? "live" : "test";
        var keyId = Pick(values, "keyId");
        var keySecret = Pick(values, "keySecret");
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(keySecret))
            return BadRequest("Razorpay keyId/keySecret not configured.");
        if ((mode == "live" && !keyId.StartsWith("rzp_live_", StringComparison.OrdinalIgnoreCase)) ||
            (mode != "live" && !keyId.StartsWith("rzp_test_", StringComparison.OrdinalIgnoreCase)))
            return BadRequest($"Razorpay keyId does not match configured mode '{mode}'.");

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var authBytes = Encoding.UTF8.GetBytes($"{keyId}:{keySecret}");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
            using var resp = await client.GetAsync("https://api.razorpay.com/v1/payments?count=1", ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                var message = ExtractRazorpayErrorMessage(raw, $"Razorpay test failed ({(int)resp.StatusCode}).");
                await audit.WriteAsync("platform.payment.test.failed", $"provider=razorpay; err={message}", ct);
                return StatusCode(StatusCodes.Status502BadGateway, new { ok = false, provider = "razorpay", mode, message });
            }

            await audit.WriteAsync("platform.payment.test.success", $"provider=razorpay; mode={mode}", ct);
            return Ok(new
            {
                ok = true,
                provider = "razorpay",
                mode,
                message = "Razorpay credentials are valid.",
                keyIdMasked = MaskKey(keyId)
            });
        }
        catch (Exception ex)
        {
            await audit.WriteAsync("platform.payment.test.failed", $"provider=razorpay; err={ex.Message}", ct);
            return StatusCode(StatusCodes.Status502BadGateway, new { ok = false, provider = "razorpay", mode, message = ex.Message });
        }
    }

    private static string Pick(Dictionary<string, string> values, string key, params string?[] fallbacks)
    {
        if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value.Trim();
        foreach (var f in fallbacks)
        {
            if (!string.IsNullOrWhiteSpace(f)) return f.Trim();
        }
        return string.Empty;
    }

    private static int ParseTimeout(string raw)
    {
        if (!int.TryParse(raw, out var ms)) ms = 15000;
        if (ms < 3000) ms = 3000;
        if (ms > 60000) ms = 60000;
        return ms;
    }

    private static async Task<string> ReadLineWithTimeoutAsync(StreamReader reader, int timeoutMs, CancellationToken ct)
    {
        var readTask = reader.ReadLineAsync();
        var completed = await Task.WhenAny(readTask, Task.Delay(timeoutMs, ct));
        if (completed != readTask)
            throw new TimeoutException("SMTP read line timed out.");
        return await readTask ?? string.Empty;
    }

    private static async Task<List<string>> ReadSmtpResponseAsync(StreamReader reader, int timeoutMs, CancellationToken ct)
    {
        var lines = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            var line = await ReadLineWithTimeoutAsync(reader, timeoutMs, ct);
            if (string.IsNullOrWhiteSpace(line)) break;
            lines.Add(line);
            if (line.Length >= 4 && line[3] == ' ') break;
        }
        return lines;
    }

    private static string ExtractRazorpayErrorMessage(string raw, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.TryGetProperty("description", out var desc) && !string.IsNullOrWhiteSpace(desc.GetString()))
                    return desc.GetString()!;
                if (err.TryGetProperty("reason", out var reason) && !string.IsNullOrWhiteSpace(reason.GetString()))
                    return reason.GetString()!;
                if (err.TryGetProperty("code", out var code) && !string.IsNullOrWhiteSpace(code.GetString()))
                    return $"Razorpay error: {code.GetString()}";
            }
        }
        catch
        {
            // ignored
        }

        return string.IsNullOrWhiteSpace(raw) ? fallback : fallback + " " + raw;
    }

    private static string MaskKey(string value)
    {
        var key = (value ?? string.Empty).Trim();
        if (key.Length <= 8) return key;
        return $"{key[..6]}...{key[^4..]}";
    }

    public sealed class SmtpTestRequest
    {
        public string Email { get; set; } = string.Empty;
    }

    public sealed class PaymentGatewayTestRequest
    {
        public string Provider { get; set; } = "razorpay";
    }

    public sealed class SmtpDiagnoseRequest
    {
        public string Provider { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public string Port { get; set; } = string.Empty;
        public string TimeoutMs { get; set; } = string.Empty;
        public string EnableSsl { get; set; } = string.Empty;
    }

    public sealed class SmsTestRequest
    {
        public string Phone { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string TemplateId { get; set; } = string.Empty;
    }

}
