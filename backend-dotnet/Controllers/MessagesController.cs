using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Text.Json;
using Textzy.Api.Data;
using Textzy.Api.DTOs;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/messages")]
public class MessagesController(
    MessagingService messaging,
    TenantDbContext db,
    TenancyContext tenancy,
    RbacService rbac,
    IHubContext<InboxHub> hub,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    SecretCryptoService crypto) : ControllerBase
{
    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] SendMessageRequest request, CancellationToken ct)
    {
        if (!rbac.HasPermission(InboxWrite)) return Forbid();
        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return BadRequest(new { error = "Idempotency-Key header is required." });
        request.IdempotencyKey = idempotencyKey;
        try
        {
            request.Recipient = InputGuardService.ValidatePhone(request.Recipient, "Recipient");
            if (!request.UseTemplate && !request.IsMedia)
                request.Body = InputGuardService.RequireTrimmed(request.Body, "Message body", 4000);
            if (request.UseTemplate && string.IsNullOrWhiteSpace(request.TemplateName))
                return BadRequest(new { error = "Template name is required when UseTemplate is true." });
            if (request.UseTemplate)
                request.TemplateName = InputGuardService.RequireTrimmed(request.TemplateName, "Template name", 128);
            if (request.IsMedia)
            {
                request.MediaType = InputGuardService.RequireTrimmed(request.MediaType, "Media type", 20).ToLowerInvariant();
                request.MediaId = InputGuardService.RequireTrimmed(request.MediaId, "Media id", 256);
                request.MediaCaption = (request.MediaCaption ?? string.Empty).Trim();
            }
            request.TemplateLanguageCode = string.IsNullOrWhiteSpace(request.TemplateLanguageCode)
                ? "en"
                : InputGuardService.RequireTrimmed(request.TemplateLanguageCode, "Template language", 12).ToLowerInvariant();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        try
        {
            var message = await messaging.EnqueueAsync(request, ct);
            await hub.Clients.Group($"tenant:{tenancy.TenantSlug}").SendAsync("message.queued", new
            {
                message.Id,
                message.Recipient,
                message.Body,
                message.Channel,
                message.Status,
                message.CreatedAtUtc
            }, ct);
            return Ok(new
            {
                message.Id,
                message.IdempotencyKey,
                message.ProviderMessageId,
                message.Status,
                message.CreatedAtUtc
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("upload-whatsapp-media")]
    [RequestSizeLimit(25_000_000)]
    public async Task<IActionResult> UploadWhatsAppMedia(
        [FromForm] string recipient,
        [FromForm] IFormFile file,
        [FromForm] string? mediaType,
        [FromForm] string? caption,
        CancellationToken ct)
    {
        if (!rbac.HasPermission(InboxWrite)) return Forbid();
        if (file is null || file.Length <= 0) return BadRequest(new { error = "File is required." });
        try
        {
            recipient = InputGuardService.ValidatePhone(recipient, "Recipient");
            var resolvedType = ResolveMediaType(mediaType, file.ContentType, file.FileName);
            if (resolvedType is null) return BadRequest(new { error = "Unsupported media type. Use image, video, audio, or document." });

            var wabaCfg = await db.Set<TenantWabaConfig>()
                .Where(x => x.TenantId == tenancy.TenantId && x.IsActive)
                .OrderByDescending(x => x.ConnectedAtUtc)
                .FirstOrDefaultAsync(ct);
            if (wabaCfg is null) return BadRequest(new { error = "WABA config not connected." });
            if (string.IsNullOrWhiteSpace(wabaCfg.PhoneNumberId)) return BadRequest(new { error = "Phone number ID missing." });
            var accessToken = UnprotectToken(wabaCfg.AccessToken);
            if (string.IsNullOrWhiteSpace(accessToken)) return BadRequest(new { error = "WABA access token missing." });

            var options = configuration.GetSection("WhatsApp").Get<WhatsAppOptions>() ?? new WhatsAppOptions();
            var mediaId = await UploadMediaToWhatsAppAsync(options, wabaCfg.PhoneNumberId, accessToken, file, ct);

            var message = await messaging.EnqueueAsync(new SendMessageRequest
            {
                IdempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault()?.Trim() ?? Guid.NewGuid().ToString("N"),
                Recipient = recipient,
                Channel = ChannelType.WhatsApp,
                IsMedia = true,
                MediaType = resolvedType,
                MediaId = mediaId,
                MediaCaption = (caption ?? string.Empty).Trim()
            }, ct);

            await hub.Clients.Group($"tenant:{tenancy.TenantSlug}").SendAsync("message.queued", new
            {
                message.Id,
                message.Recipient,
                message.Body,
                message.Channel,
                message.Status,
                message.CreatedAtUtc
            }, ct);

            return Ok(new { mediaId, messageId = message.Id, status = message.Status, mediaType = resolvedType });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("upload-whatsapp-asset")]
    [RequestSizeLimit(25_000_000)]
    public async Task<IActionResult> UploadWhatsAppAsset(
        [FromForm] IFormFile file,
        [FromForm] string? mediaType,
        CancellationToken ct)
    {
        if (!rbac.HasPermission(TemplatesWrite)) return Forbid();
        if (file is null || file.Length <= 0) return BadRequest(new { error = "File is required." });
        try
        {
            var resolvedType = ResolveMediaType(mediaType, file.ContentType, file.FileName);
            if (resolvedType is null) return BadRequest(new { error = "Unsupported media type. Use image, video, audio, or document." });

            var wabaCfg = await db.Set<TenantWabaConfig>()
                .Where(x => x.TenantId == tenancy.TenantId && x.IsActive)
                .OrderByDescending(x => x.ConnectedAtUtc)
                .FirstOrDefaultAsync(ct);
            if (wabaCfg is null) return BadRequest(new { error = "WABA config not connected." });
            if (string.IsNullOrWhiteSpace(wabaCfg.PhoneNumberId)) return BadRequest(new { error = "Phone number ID missing." });
            var accessToken = UnprotectToken(wabaCfg.AccessToken);
            if (string.IsNullOrWhiteSpace(accessToken)) return BadRequest(new { error = "WABA access token missing." });

            var options = configuration.GetSection("WhatsApp").Get<WhatsAppOptions>() ?? new WhatsAppOptions();
            var mediaId = await UploadMediaToWhatsAppAsync(options, wabaCfg.PhoneNumberId, accessToken, file, ct);
            return Ok(new { mediaId, mediaType = resolvedType, fileName = file.FileName });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet]
    public IActionResult List()
    {
        if (!rbac.HasPermission(InboxRead)) return Forbid();

        var items = db.Messages
            .Where(m => m.TenantId == tenancy.TenantId)
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(100)
            .ToList();

        return Ok(items);
    }

    [HttpGet("media/{mediaId}")]
    public async Task<IActionResult> GetInboundMedia(string mediaId, CancellationToken ct)
    {
        if (!rbac.HasPermission(InboxRead)) return Forbid();
        var id = (mediaId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { error = "mediaId is required." });

        var wabaCfg = await db.Set<TenantWabaConfig>()
            .Where(x => x.TenantId == tenancy.TenantId && x.IsActive)
            .OrderByDescending(x => x.ConnectedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (wabaCfg is null) return BadRequest(new { error = "WABA config not connected." });

        var accessToken = UnprotectToken(wabaCfg.AccessToken);
        if (string.IsNullOrWhiteSpace(accessToken)) return BadRequest(new { error = "WABA access token missing." });

        var options = configuration.GetSection("WhatsApp").Get<WhatsAppOptions>() ?? new WhatsAppOptions();
        var client = httpClientFactory.CreateClient();

        var metaUrl = $"{options.GraphApiBase}/{options.ApiVersion}/{id}";
        using var metaReq = new HttpRequestMessage(HttpMethod.Get, metaUrl);
        metaReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var metaResp = await client.SendAsync(metaReq, ct);
        var metaBody = await metaResp.Content.ReadAsStringAsync(ct);
        if (!metaResp.IsSuccessStatusCode)
            return BadRequest(new { error = $"Media metadata fetch failed ({(int)metaResp.StatusCode})", detail = metaBody });

        string downloadUrl;
        string mimeType;
        string fileName;
        using (var metaDoc = JsonDocument.Parse(metaBody))
        {
            downloadUrl = metaDoc.RootElement.TryGetProperty("url", out var u) ? (u.GetString() ?? string.Empty) : string.Empty;
            mimeType = metaDoc.RootElement.TryGetProperty("mime_type", out var m) ? (m.GetString() ?? "application/octet-stream") : "application/octet-stream";
            fileName = metaDoc.RootElement.TryGetProperty("filename", out var f) ? (f.GetString() ?? string.Empty) : string.Empty;
        }
        if (string.IsNullOrWhiteSpace(downloadUrl))
            return BadRequest(new { error = "Media URL missing from Graph metadata." });

        using var dlReq = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        dlReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var dlResp = await client.SendAsync(dlReq, ct);
        if (!dlResp.IsSuccessStatusCode)
        {
            var dlBody = await dlResp.Content.ReadAsStringAsync(ct);
            return BadRequest(new { error = $"Media download failed ({(int)dlResp.StatusCode})", detail = dlBody });
        }

        var bytes = await dlResp.Content.ReadAsByteArrayAsync(ct);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = $"waba-media-{id}";
        return File(bytes, mimeType, fileName);
    }

    private static string? ResolveMediaType(string? providedType, string? contentType, string? fileName)
    {
        var t = (providedType ?? string.Empty).Trim().ToLowerInvariant();
        if (t is "image" or "video" or "audio" or "document") return t;

        var mime = (contentType ?? string.Empty).ToLowerInvariant();
        if (mime.StartsWith("image/")) return "image";
        if (mime.StartsWith("video/")) return "video";
        if (mime.StartsWith("audio/")) return "audio";
        if (!string.IsNullOrWhiteSpace(mime)) return "document";

        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (new[] { ".png", ".jpg", ".jpeg", ".webp", ".gif" }.Contains(ext)) return "image";
        if (new[] { ".mp4", ".3gp", ".mov", ".mkv" }.Contains(ext)) return "video";
        if (new[] { ".mp3", ".ogg", ".wav", ".m4a", ".aac" }.Contains(ext)) return "audio";
        return "document";
    }

    private string UnprotectToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return string.Empty;
        if (!token.StartsWith("enc:", StringComparison.Ordinal)) return token;
        try { return crypto.Decrypt(token[4..]); } catch { return string.Empty; }
    }

    private async Task<string> UploadMediaToWhatsAppAsync(
        WhatsAppOptions options,
        string phoneNumberId,
        string accessToken,
        IFormFile file,
        CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient();
        var url = $"{options.GraphApiBase}/{options.ApiVersion}/{phoneNumberId}/media";
        await using var fs = file.OpenReadStream();
        using var content = new MultipartFormDataContent
        {
            { new StringContent("whatsapp"), "messaging_product" }
        };
        var fileContent = new StreamContent(fs);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
        content.Add(fileContent, "file", file.FileName);

        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Media upload failed ({(int)resp.StatusCode}): {body}");
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("id").GetString() ?? string.Empty;
    }
}
