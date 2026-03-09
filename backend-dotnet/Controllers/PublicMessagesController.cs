using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.DTOs;
using Textzy.Api.Models;
using Textzy.Api.Services;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/public/messages")]
public class PublicMessagesController(
    ControlDbContext controlDb,
    SecretCryptoService crypto,
    TenancyContext tenancy,
    MessagingService messaging) : ControllerBase
{
    [HttpGet("send")]
    public async Task<IActionResult> SendByQuery(CancellationToken ct)
    {
        var q = Request.Query;
        var req = new PublicSendRequest
        {
            Recipient = q["recipient"].FirstOrDefault() ?? string.Empty,
            Message = q["msg"].FirstOrDefault() ?? q["message"].FirstOrDefault() ?? string.Empty,
            User = q["user"].FirstOrDefault() ?? string.Empty,
            Password = q["pswd"].FirstOrDefault() ?? q["password"].FirstOrDefault() ?? string.Empty,
            ApiKey = q["apikey"].FirstOrDefault() ?? q["apiKey"].FirstOrDefault() ?? string.Empty,
            TenantSlug = q["tenantSlug"].FirstOrDefault() ?? string.Empty,
            Channel = q["channel"].FirstOrDefault() ?? "sms",
            Sender = q["sender"].FirstOrDefault() ?? q["senderid"].FirstOrDefault() ?? string.Empty,
            PeId = q["PE_ID"].FirstOrDefault() ?? q["peid"].FirstOrDefault() ?? q["entityid"].FirstOrDefault() ?? string.Empty,
            TemplateId = q["Template_ID"].FirstOrDefault() ?? q["templateid"].FirstOrDefault() ?? string.Empty,
            IdempotencyKey = q["idempotencyKey"].FirstOrDefault() ?? string.Empty
        };
        return await SendCore(req, ct);
    }

    [HttpPost("send")]
    public async Task<IActionResult> SendByPost([FromBody] PublicSendRequest request, CancellationToken ct)
    {
        return await SendCore(request, ct);
    }

    private async Task<IActionResult> SendCore(PublicSendRequest request, CancellationToken ct)
    {
        if (!tenancy.IsSet)
            return BadRequest("tenantSlug query parameter is required.");

        if (!IsHttpsRequest(HttpContext))
            return StatusCode(StatusCodes.Status403Forbidden, "HTTPS is required.");

        var profile = await controlDb.TenantCompanyProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId, ct);
        if (profile is null || !profile.PublicApiEnabled)
            return StatusCode(StatusCodes.Status403Forbidden, "Public API integration is disabled.");

        var expectedUser = (profile.ApiUsername ?? string.Empty).Trim();
        var expectedPassword = crypto.Decrypt(profile.ApiPasswordEncrypted).Trim();
        var expectedApiKey = crypto.Decrypt(profile.ApiKeyEncrypted).Trim();

        if (string.IsNullOrWhiteSpace(expectedUser) || string.IsNullOrWhiteSpace(expectedPassword) || string.IsNullOrWhiteSpace(expectedApiKey))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Public API credentials are not configured.");

        var providedUser = (request.User ?? string.Empty).Trim();
        var providedPassword = (request.Password ?? string.Empty).Trim();
        var providedApiKey = (request.ApiKey ?? string.Empty).Trim();

        if (!SecureEquals(providedUser, expectedUser) ||
            !SecureEquals(providedPassword, expectedPassword) ||
            !SecureEquals(providedApiKey, expectedApiKey))
        {
            return Unauthorized("Invalid API credentials.");
        }

        var ipWhitelist = profile.ApiIpWhitelist ?? string.Empty;
        if (!IsIpAllowed(HttpContext.Connection.RemoteIpAddress, ipWhitelist))
            return StatusCode(StatusCodes.Status403Forbidden, "Client IP not allowed.");

        var recipient = (request.Recipient ?? string.Empty).Trim();
        var message = (request.Message ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(recipient)) return BadRequest("recipient is required.");
        if (string.IsNullOrWhiteSpace(message)) return BadRequest("msg is required.");

        var channel = ParseChannel(request.Channel);
        var sender = string.Empty;
        var peId = string.Empty;
        var templateId = string.Empty;
        if (channel == ChannelType.Sms)
        {
            sender = (request.Sender ?? string.Empty).Trim();
            peId = (request.PeId ?? string.Empty).Trim();
            templateId = (request.TemplateId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(sender)) return BadRequest("sender is required for SMS DLT.");
            if (string.IsNullOrWhiteSpace(peId)) return BadRequest("PE_ID is required for SMS DLT.");
            if (string.IsNullOrWhiteSpace(templateId)) return BadRequest("Template_ID is required for SMS DLT.");
        }
        var idempotency = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? $"public-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}"[..48]
            : request.IdempotencyKey.Trim();

        try
        {
            var queued = await messaging.EnqueueAsync(new SendMessageRequest
            {
                IdempotencyKey = idempotency,
                Recipient = recipient,
                Body = message,
                Channel = channel,
                SmsSenderId = sender,
                SmsPeId = peId,
                SmsTemplateId = templateId,
                ForcePlatformSmsConfig = channel == ChannelType.Sms
            }, ct);

            return Ok(new
            {
                jobId = queued.Id.ToString(),
                message = "Accepted"
            });
        }
        catch (InvalidOperationException ex)
        {
            return BuildPublicError(ex.Message);
        }
    }

    private static IActionResult BuildPublicError(string raw)
    {
        var text = (raw ?? string.Empty).Trim();
        var lower = text.ToLowerInvariant();

        if (lower.Contains("authorization") || lower.Contains("unauthorized") || lower.Contains("(401)") || lower.Contains(" 401"))
            return new JsonResult(new { message = "Invalid authorization.", code = "401" }) { StatusCode = StatusCodes.Status401Unauthorized };

        if (lower.Contains("(403)") || lower.Contains("forbidden"))
            return new JsonResult(new { message = "Access denied.", code = "403" }) { StatusCode = StatusCodes.Status403Forbidden };

        if (lower.Contains("(429)") || lower.Contains("rate"))
            return new JsonResult(new { message = "Rate limit exceeded.", code = "429" }) { StatusCode = StatusCodes.Status429TooManyRequests };

        if (lower.Contains("(500)") || lower.Contains("(502)") || lower.Contains("(503)") || lower.Contains("(504)") || lower.Contains("timeout"))
            return new JsonResult(new { message = "Gateway temporarily unavailable.", code = "503" }) { StatusCode = StatusCodes.Status503ServiceUnavailable };

        if (lower.Contains("sender is required"))
            return new JsonResult(new { message = "Sender is required.", code = "422" }) { StatusCode = StatusCodes.Status422UnprocessableEntity };
        if (lower.Contains("pe_id is required") || lower.Contains("peid is required"))
            return new JsonResult(new { message = "PE_ID is required.", code = "422" }) { StatusCode = StatusCodes.Status422UnprocessableEntity };
        if (lower.Contains("template_id is required") || lower.Contains("templateid is required"))
            return new JsonResult(new { message = "Template_ID is required.", code = "422" }) { StatusCode = StatusCodes.Status422UnprocessableEntity };

        return new JsonResult(new { message = "Request rejected.", code = "400" }) { StatusCode = StatusCodes.Status400BadRequest };
    }

    private static ChannelType ParseChannel(string? raw)
    {
        var normalized = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "whatsapp" or "wa" or "waba" => ChannelType.WhatsApp,
            _ => ChannelType.Sms
        };
    }

    private static bool IsHttpsRequest(HttpContext context)
    {
        if (context.Request.IsHttps) return true;
        var xfProto = context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault();
        return string.Equals(xfProto, "https", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SecureEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left ?? string.Empty);
        var b = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static bool IsIpAllowed(IPAddress? remoteIp, string rawWhitelist)
    {
        if (string.IsNullOrWhiteSpace(rawWhitelist)) return true;
        if (remoteIp is null) return false;

        var ip = remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4() : remoteIp;
        var rules = rawWhitelist.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule)) continue;
            if (string.Equals(rule, "*", StringComparison.Ordinal)) return true;
            if (TryMatchCidr(ip, rule)) return true;

            if (IPAddress.TryParse(rule, out var allowed))
            {
                var normalizedAllowed = allowed.IsIPv4MappedToIPv6 ? allowed.MapToIPv4() : allowed;
                if (normalizedAllowed.Equals(ip)) return true;
            }
        }

        return false;
    }

    private static bool TryMatchCidr(IPAddress ip, string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2) return false;
        if (!IPAddress.TryParse(parts[0], out var baseIp)) return false;
        if (!int.TryParse(parts[1], out var prefixLength)) return false;

        var ipBytes = (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).GetAddressBytes();
        var baseBytes = (baseIp.IsIPv4MappedToIPv6 ? baseIp.MapToIPv4() : baseIp).GetAddressBytes();
        if (ipBytes.Length != baseBytes.Length) return false;

        var bits = ipBytes.Length * 8;
        if (prefixLength < 0 || prefixLength > bits) return false;

        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var i = 0; i < fullBytes; i++)
        {
            if (ipBytes[i] != baseBytes[i]) return false;
        }

        if (remainingBits == 0) return true;
        var mask = (byte)(0xFF << (8 - remainingBits));
        return (ipBytes[fullBytes] & mask) == (baseBytes[fullBytes] & mask);
    }

    public sealed class PublicSendRequest
    {
        public string Recipient { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string TenantSlug { get; set; } = string.Empty;
        public string Channel { get; set; } = "sms";
        public string Sender { get; set; } = string.Empty;
        public string PeId { get; set; } = string.Empty;
        public string TemplateId { get; set; } = string.Empty;
        public string IdempotencyKey { get; set; } = string.Empty;
    }
}
