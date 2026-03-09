using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;
using Textzy.Api.Data;
using Textzy.Api.DTOs;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/templates")]
public class TemplatesController(
    TenantDbContext db,
    TenancyContext tenancy,
    AuthContext auth,
    RbacService rbac,
    WhatsAppCloudService whatsapp,
    TemplateVariableResolverService templateVariables,
    TemplateSyncOrchestrator templateSync,
    SensitiveDataRedactor redactor,
    ILogger<TemplatesController> logger) : ControllerBase
{
    private Guid CurrentTenantId => tenancy.IsSet ? tenancy.TenantId : auth.TenantId;

    private Guid[] TenantScopeIds()
    {
        var ids = new List<Guid>(2);
        if (tenancy.IsSet && tenancy.TenantId != Guid.Empty) ids.Add(tenancy.TenantId);
        if (auth.TenantId != Guid.Empty && !ids.Contains(auth.TenantId)) ids.Add(auth.TenantId);
        if (CurrentTenantId != Guid.Empty && !ids.Contains(CurrentTenantId)) ids.Add(CurrentTenantId);
        return ids.ToArray();
    }

    private IQueryable<Template> QueryTemplates()
    {
        var scope = TenantScopeIds();
        return db.Templates
            .AsNoTracking()
            .Where(x => scope.Contains(x.TenantId))
            .OrderByDescending(x => x.CreatedAtUtc);
    }

    private async Task<List<Template>> LoadTemplateRowsWithAutoSyncAsync(CancellationToken ct)
    {
        var rows = await QueryTemplates().ToListAsync(ct);

        if (rows.Count > 0) return rows;

        var hasActiveWaba = await db.TenantWabaConfigs
            .AnyAsync(x => x.TenantId == CurrentTenantId && x.IsActive && x.WabaId != "", ct);
        if (!hasActiveWaba) return rows;

        try
        {
            await whatsapp.SyncMessageTemplatesAsync(deepSync: true, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Auto-recovery sync failed for tenant={TenantId}: {Error}",
                CurrentTenantId, redactor.RedactText(ex.Message));
            return rows;
        }

        return await QueryTemplates().ToListAsync(ct);
    }
    private static readonly HashSet<string> AllowedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "MARKETING", "UTILITY", "AUTHENTICATION"
    };

    private static readonly HashSet<string> AllowedHeaderTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "none", "text", "image", "video", "document"
    };

    private static readonly HashSet<string> AllowedButtonTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "quick_reply", "quickreply", "url", "cta_url", "phone", "phone_number", "call"
    };

    private static readonly Regex WhatsAppTemplateNameRegex = new("^[a-z0-9_]+$", RegexOptions.Compiled);
    private static readonly Regex UrlRegex = new(@"https?://[^\s]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> BlockedShortenerHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "bit.ly", "tinyurl.com", "t.co", "shorturl.at", "rb.gy", "goo.gl", "ow.ly", "is.gd", "cutt.ly", "buff.ly"
    };

    private static readonly string[] AggressiveMarketingTerms =
    [
        "buy now", "limited time", "flash sale", "act now", "hurry", "exclusive offer", "discount", "promo code"
    ];

    private static string? ValidateRequest(UpsertTemplateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return "Template name is required.";
        if (string.IsNullOrWhiteSpace(request.Body)) return "Template body is required.";
        if (request.Body.Length > 1024 && request.Channel == ChannelType.WhatsApp) return "WhatsApp template body cannot exceed 1024 characters.";
        if (!AllowedCategories.Contains(request.Category ?? "")) return "Invalid category. Use MARKETING, UTILITY, AUTHENTICATION.";
        if (!AllowedHeaderTypes.Contains(request.HeaderType ?? "none")) return "Invalid header type.";

        var vars = System.Text.RegularExpressions.Regex.Matches(request.Body, @"\{\{(\d+)\}\}")
            .Select(m => int.Parse(m.Groups[1].Value))
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        for (var i = 0; i < vars.Length; i++)
        {
            if (vars[i] != i + 1) return "Template variables must be sequential: {{1}}, {{2}}, {{3}} ...";
        }

        if (request.Channel == ChannelType.WhatsApp)
        {
            var normalizedName = request.Name.Trim();
            if (normalizedName.Length > 512) return "WhatsApp template name is too long.";
            if (!WhatsAppTemplateNameRegex.IsMatch(normalizedName))
                return "WhatsApp template name must contain only lowercase letters, numbers, and underscore.";
            if (normalizedName.Contains("__", StringComparison.Ordinal))
                return "WhatsApp template name should not contain repeated underscores.";
            if (normalizedName.StartsWith("_", StringComparison.Ordinal) || normalizedName.EndsWith("_", StringComparison.Ordinal))
                return "WhatsApp template name cannot start or end with underscore.";

            var headerType = (request.HeaderType ?? "none").Trim().ToLowerInvariant();
            if (headerType == "text")
            {
                if (string.IsNullOrWhiteSpace(request.HeaderText)) return "Header text is required when header type is text.";
                if (request.HeaderText.Trim().Length > 60) return "WhatsApp header text cannot exceed 60 characters.";
            }
            else if (headerType is "image" or "video" or "document")
            {
                if (!string.IsNullOrWhiteSpace(request.HeaderText))
                    return "Header text must be empty for media header types.";
                if (string.IsNullOrWhiteSpace(request.HeaderMediaId))
                    return "Header media id is required for media header types.";
            }
            else if (headerType == "none" && !string.IsNullOrWhiteSpace(request.HeaderText))
            {
                return "Header text is allowed only when header type is text.";
            }

            if (!string.IsNullOrWhiteSpace(request.FooterText) && request.FooterText.Trim().Length > 60)
                return "WhatsApp footer text cannot exceed 60 characters.";

            var buttonError = ValidateButtonsJson(request.ButtonsJson);
            if (buttonError is not null) return buttonError;
            var policyError = ValidatePolicyContent(request);
            if (policyError is not null) return policyError;
        }

        if (request.Channel == ChannelType.Sms)
        {
            if (string.IsNullOrWhiteSpace(request.DltEntityId)) return "SMS template requires DLT Entity ID.";
            if (string.IsNullOrWhiteSpace(request.DltTemplateId)) return "SMS template requires DLT Template ID.";
            if (string.IsNullOrWhiteSpace(request.SmsSenderId)) return "SMS template requires Sender ID.";
            if (request.SmsSenderId.Trim().Length is < 3 or > 6) return "SMS Sender ID must be 3-6 characters.";
        }

        return null;
    }

    private static string? ValidateButtonsJson(string? buttonsJson)
    {
        if (string.IsNullOrWhiteSpace(buttonsJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(buttonsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return "Buttons JSON must be an array.";

            var total = 0;
            var quickReplies = 0;
            var ctaButtons = 0;
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                total++;
                if (total > 10) return "WhatsApp supports up to 10 buttons.";

                var type = row.TryGetProperty("type", out var typeNode) ? (typeNode.GetString() ?? string.Empty).Trim().ToLowerInvariant() : string.Empty;
                var text = row.TryGetProperty("text", out var textNode) ? (textNode.GetString() ?? string.Empty).Trim() : string.Empty;
                if (string.IsNullOrWhiteSpace(type) || !AllowedButtonTypes.Contains(type))
                    return "Invalid button type. Use quick_reply, url, or phone_number.";
                if (string.IsNullOrWhiteSpace(text) || text.Length > 25)
                    return "Button text is required and must be <= 25 characters.";

                if (type is "quick_reply" or "quickreply")
                {
                    quickReplies++;
                    continue;
                }

                ctaButtons++;
                if (ctaButtons > 2) return "WhatsApp supports up to 2 CTA buttons (URL/phone).";

                if (type is "url" or "cta_url")
                {
                    var url = row.TryGetProperty("url", out var urlNode) ? (urlNode.GetString() ?? string.Empty).Trim() : string.Empty;
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
                        return "CTA URL button requires a valid http/https URL.";
                    if (IsBlockedShortener(parsed))
                        return "URL shortener domains are not allowed in WhatsApp templates.";
                }
                else
                {
                    var phone = row.TryGetProperty("phone_number", out var phoneNode) ? (phoneNode.GetString() ?? string.Empty).Trim() : string.Empty;
                    if (!Regex.IsMatch(phone, @"^\+?[0-9]{7,15}$"))
                        return "Phone button requires valid E.164 style number.";
                }
            }

            if (quickReplies > 10) return "WhatsApp supports up to 10 quick-reply buttons.";
            return null;
        }
        catch
        {
            return "Buttons JSON is invalid.";
        }
    }

    private static string? ValidatePolicyContent(UpsertTemplateRequest request)
    {
        var category = (request.Category ?? "UTILITY").Trim().ToUpperInvariant();
        var content = $"{request.HeaderText} {request.Body} {request.FooterText}".ToLowerInvariant();

        var urls = UrlRegex.Matches(content)
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var candidate in urls)
        {
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var parsed) && IsBlockedShortener(parsed))
                return "URL shortener domains are not allowed in WhatsApp templates.";
        }

        if (category is "UTILITY" or "AUTHENTICATION")
        {
            if (AggressiveMarketingTerms.Any(term => content.Contains(term, StringComparison.OrdinalIgnoreCase)))
                return $"{category} templates cannot contain promotional marketing language.";
        }

        if (category == "AUTHENTICATION")
        {
            if (!request.Body.Contains("{{1}}", StringComparison.Ordinal))
                return "Authentication templates must include OTP/code placeholder {{1}}.";
            if (request.Body.Contains("{{2}}", StringComparison.Ordinal))
                return "Authentication templates should only contain required auth variables; remove extra placeholders unless mandatory.";
        }

        return null;
    }

    private static bool IsBlockedShortener(Uri uri)
    {
        var host = uri.Host?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(host)) return false;
        return BlockedShortenerHosts.Contains(host) ||
               BlockedShortenerHosts.Any(x => host.EndsWith($".{x}", StringComparison.OrdinalIgnoreCase));
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!rbac.HasPermission(TemplatesRead)) return Forbid();
        try
        {
            await templateSync.EnsureInitialOrDailySyncAsync(false, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Non-blocking template auto-sync failed on /api/templates tenant={TenantId}: {Error}",
                CurrentTenantId, redactor.RedactText(ex.Message));
        }
        var rows = await LoadTemplateRowsWithAutoSyncAsync(ct);
        return Ok(rows);
    }

    [HttpGet("project-list")]
    public async Task<IActionResult> ProjectList(CancellationToken ct)
    {
        if (!rbac.HasPermission(TemplatesRead)) return Forbid();
        try
        {
            await templateSync.EnsureInitialOrDailySyncAsync(false, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Non-blocking template auto-sync failed on /api/templates/project-list tenant={TenantId}: {Error}",
                CurrentTenantId, redactor.RedactText(ex.Message));
        }
        var items = await LoadTemplateRowsWithAutoSyncAsync(ct);

        return Ok(new
        {
            tenantId = CurrentTenantId,
            tenantSlug = tenancy.TenantSlug,
            total = items.Count,
            items
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UpsertTemplateRequest request, CancellationToken ct)
    {
        if (!rbac.HasPermission(TemplatesWrite)) return Forbid();
        var error = ValidateRequest(request);
        if (error is not null) return BadRequest(error);
        var item = new Template
        {
            Id = Guid.NewGuid(),
            TenantId = CurrentTenantId,
            Name = request.Name.Trim(),
            Channel = request.Channel,
            Category = request.Category.ToUpperInvariant(),
            Language = request.Language,
            Body = request.Body,
            LifecycleStatus = request.Channel == ChannelType.WhatsApp ? "draft" : "approved",
            DltEntityId = request.DltEntityId ?? string.Empty,
            DltTemplateId = request.DltTemplateId ?? string.Empty,
            SmsSenderId = request.SmsSenderId ?? string.Empty,
            HeaderType = request.HeaderType ?? "none",
            HeaderText = request.HeaderText ?? string.Empty,
            HeaderMediaId = request.HeaderMediaId ?? string.Empty,
            HeaderMediaName = request.HeaderMediaName ?? string.Empty,
            FooterText = request.FooterText ?? string.Empty,
            ButtonsJson = request.ButtonsJson ?? string.Empty,
            Status = request.Channel == ChannelType.WhatsApp ? "Draft" : "Approved",
            RejectionReason = string.Empty
        };

        var duplicateExists = await db.Templates.AnyAsync(x =>
            x.TenantId == CurrentTenantId &&
            x.Channel == item.Channel &&
            x.Name == item.Name &&
            x.Language == item.Language, ct);
        if (duplicateExists)
            return BadRequest("Template with same name and language already exists for this project.");

        db.Templates.Add(item);
        await db.SaveChangesAsync(ct);
        return Ok(item);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpsertTemplateRequest request, CancellationToken ct)
    {
        if (!rbac.HasPermission(TemplatesWrite)) return Forbid();
        var error = ValidateRequest(request);
        if (error is not null) return BadRequest(error);
        var scope = TenantScopeIds();
        var item = db.Templates.FirstOrDefault(x => x.Id == id && scope.Contains(x.TenantId));
        if (item is null) return NotFound();
        var requestedName = request.Name.Trim();
        if (item.Channel == ChannelType.WhatsApp &&
            !string.Equals(item.Name, requestedName, StringComparison.Ordinal))
        {
            return BadRequest("WhatsApp template name is immutable after creation. Create a new version with a new name.");
        }

        var duplicateExists = await db.Templates.AnyAsync(x =>
            x.TenantId == CurrentTenantId &&
            x.Id != item.Id &&
            x.Channel == request.Channel &&
            x.Name == requestedName &&
            x.Language == request.Language, ct);
        if (duplicateExists)
            return BadRequest("Another template with same name and language already exists.");

        item.Name = requestedName;
        item.Channel = request.Channel;
        item.Category = request.Category.ToUpperInvariant();
        item.Language = request.Language;
        item.Body = request.Body;
        item.DltEntityId = request.DltEntityId ?? string.Empty;
        item.DltTemplateId = request.DltTemplateId ?? string.Empty;
        item.SmsSenderId = request.SmsSenderId ?? string.Empty;
        item.HeaderType = request.HeaderType ?? "none";
        item.HeaderText = request.HeaderText ?? string.Empty;
        item.HeaderMediaId = request.HeaderMediaId ?? string.Empty;
        item.HeaderMediaName = request.HeaderMediaName ?? string.Empty;
        item.FooterText = request.FooterText ?? string.Empty;
        item.ButtonsJson = request.ButtonsJson ?? string.Empty;
        item.RejectionReason = string.Empty;
        if (item.Channel == ChannelType.WhatsApp && !string.Equals(item.LifecycleStatus, "approved", StringComparison.OrdinalIgnoreCase))
        {
            item.LifecycleStatus = "draft";
            item.Status = "Draft";
        }
        await db.SaveChangesAsync(ct);
        return Ok(item);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!rbac.HasPermission(TemplatesWrite)) return Forbid();
        var scope = TenantScopeIds();
        var item = db.Templates.FirstOrDefault(x => x.Id == id && scope.Contains(x.TenantId));
        if (item is null) return NotFound();

        if (item.Channel == ChannelType.WhatsApp)
        {
            try
            {
                var result = await whatsapp.DeleteTemplateFromMetaAndDbAsync(id, ct);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        db.Templates.Remove(item);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/presets")]
    public async Task<IActionResult> Presets(Guid id, [FromQuery] string recipient, CancellationToken ct)
    {
        if (!rbac.HasPermission(TemplatesRead)) return Forbid();
        var scope = TenantScopeIds();
        var template = await db.Templates.FirstOrDefaultAsync(x => x.Id == id && scope.Contains(x.TenantId), ct);
        if (template is null) return NotFound();
        if (template.Channel != ChannelType.WhatsApp) return BadRequest("Presets are supported only for WhatsApp templates.");

        var to = (recipient ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(to)) return BadRequest("Recipient is required.");

        var indexes = Regex.Matches(template.Body ?? string.Empty, @"\{\{(\d+)\}\}")
            .Select(m => int.TryParse(m.Groups[1].Value, out var n) ? n : 0)
            .Where(n => n > 0)
            .Distinct()
            .OrderBy(n => n)
            .ToArray();

        var (tokenValues, suggestedValues) = await templateVariables.BuildAsync(to, ct);
        var suggestedByIndex = new Dictionary<string, string>();
        foreach (var idx in indexes)
        {
            if (suggestedValues.TryGetValue(idx, out var value) && !string.IsNullOrWhiteSpace(value))
                suggestedByIndex[idx.ToString()] = value;
        }

        var tokenList = tokenValues
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .OrderBy(kv => kv.Key)
            .Select(kv => new
            {
                key = kv.Key,
                label = kv.Key.Replace("_", " "),
                value = kv.Value
            })
            .ToList();

        return Ok(new
        {
            templateId = template.Id,
            templateName = template.Name,
            recipient = to,
            tokens = tokenList,
            suggestedByIndex
        });
    }

}
