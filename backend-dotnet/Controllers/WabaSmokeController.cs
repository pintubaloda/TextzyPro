using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.DTOs;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/waba/smoke")]
public class WabaSmokeController(
    TenantDbContext db,
    TenancyContext tenancy,
    RbacService rbac,
    WhatsAppCloudService whatsapp) : ControllerBase
{
    [HttpGet("readiness")]
    public async Task<IActionResult> Readiness(CancellationToken ct)
    {
        if (!rbac.HasPermission(ApiRead)) return Forbid();

        var cfg = await db.TenantWabaConfigs
            .AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId)
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.ConnectedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (cfg is null)
        {
            return Ok(new
            {
                tenantId = tenancy.TenantId,
                tenantSlug = tenancy.TenantSlug,
                configured = false
            });
        }

        var templates = await db.Templates
            .AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId && x.Channel == Textzy.Api.Models.ChannelType.WhatsApp)
            .ToListAsync(ct);
        var approvedTemplates = templates.Count(x => string.Equals((x.Status ?? string.Empty).Trim(), "APPROVED", StringComparison.OrdinalIgnoreCase));
        var pendingTemplates = templates.Count(x =>
            string.Equals((x.Status ?? string.Empty).Trim(), "PENDING", StringComparison.OrdinalIgnoreCase) ||
            string.Equals((x.Status ?? string.Empty).Trim(), "IN_REVIEW", StringComparison.OrdinalIgnoreCase));

        var now = DateTime.UtcNow;
        var from24h = now.AddHours(-24);
        var statusEvents24h = await db.MessageEvents
            .AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId && x.CreatedAtUtc >= from24h)
            .ToListAsync(ct);

        return Ok(new
        {
            tenantId = tenancy.TenantId,
            tenantSlug = tenancy.TenantSlug,
            configured = true,
            waba = new
            {
                cfg.IsActive,
                cfg.OnboardingState,
                cfg.PermissionAuditPassed,
                cfg.WebhookSubscribedAtUtc,
                cfg.WebhookVerifiedAtUtc,
                cfg.WabaId,
                cfg.PhoneNumberId,
                cfg.DisplayPhoneNumber,
                cfg.LastError
            },
            templates = new
            {
                total = templates.Count,
                approved = approvedTemplates,
                pending = pendingTemplates,
                syncStatus = cfg.TemplatesSyncStatus,
                syncedAtUtc = cfg.TemplatesSyncedAtUtc
            },
            runtime24h = new
            {
                inbound = statusEvents24h.Count(x => string.Equals(x.Direction, "inbound", StringComparison.OrdinalIgnoreCase)),
                outbound = statusEvents24h.Count(x => string.Equals(x.Direction, "outbound", StringComparison.OrdinalIgnoreCase)),
                delivered = statusEvents24h.Count(x => string.Equals(x.State, "Delivered", StringComparison.OrdinalIgnoreCase)),
                read = statusEvents24h.Count(x => string.Equals(x.State, "Read", StringComparison.OrdinalIgnoreCase)),
                failed = statusEvents24h.Count(x => string.Equals(x.State, "Failed", StringComparison.OrdinalIgnoreCase))
            }
        });
    }

    [HttpPost("run")]
    public async Task<IActionResult> Run([FromBody] WabaSmokeRunRequest request, CancellationToken ct)
    {
        if (!rbac.HasPermission(ApiWrite)) return Forbid();

        var recipient = (request.Recipient ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(recipient))
            return BadRequest(new { error = "Recipient is required." });

        var result = new Dictionary<string, object?>
        {
            ["tenantId"] = tenancy.TenantId,
            ["tenantSlug"] = tenancy.TenantSlug,
            ["recipient"] = recipient,
            ["checkedAtUtc"] = DateTime.UtcNow
        };

        try
        {
            var sessionOpen = await whatsapp.IsSessionWindowOpenAsync(recipient, ct);
            result["sessionWindowOpen"] = sessionOpen;
        }
        catch (Exception ex)
        {
            result["sessionWindowOpen"] = false;
            result["sessionCheckError"] = ex.Message;
        }

        if (request.SendSessionMessage)
        {
            try
            {
                var body = string.IsNullOrWhiteSpace(request.SessionMessageText)
                    ? $"Textzy smoke test at {DateTime.UtcNow:O}"
                    : request.SessionMessageText!;
                var providerId = await whatsapp.SendSessionMessageAsync(recipient, body, ct);
                result["sessionSend"] = new { ok = true, providerMessageId = providerId };
            }
            catch (Exception ex)
            {
                result["sessionSend"] = new { ok = false, error = ex.Message };
            }
        }

        if (request.SendTemplateMessage)
        {
            if (string.IsNullOrWhiteSpace(request.TemplateName))
                return BadRequest(new { error = "TemplateName is required when SendTemplateMessage is true." });

            try
            {
                var templateProviderId = await whatsapp.SendTemplateMessageAsync(new WabaSendTemplateRequest
                {
                    Recipient = recipient,
                    TemplateName = request.TemplateName.Trim(),
                    LanguageCode = string.IsNullOrWhiteSpace(request.TemplateLanguageCode) ? "en" : request.TemplateLanguageCode.Trim().ToLowerInvariant(),
                    BodyParameters = request.TemplateParameters ?? []
                }, ct);
                result["templateSend"] = new { ok = true, providerMessageId = templateProviderId };
            }
            catch (Exception ex)
            {
                result["templateSend"] = new { ok = false, error = ex.Message };
            }
        }

        return Ok(result);
    }
}

public sealed class WabaSmokeRunRequest
{
    public string Recipient { get; set; } = string.Empty;
    public bool SendSessionMessage { get; set; }
    public string SessionMessageText { get; set; } = string.Empty;
    public bool SendTemplateMessage { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public string TemplateLanguageCode { get; set; } = "en";
    public List<string> TemplateParameters { get; set; } = [];
}
