using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/sms/templates")]
public class SmsTemplatesController(TenantDbContext db, TenancyContext tenancy, RbacService rbac) : ControllerBase
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "draft", "submitted", "approved", "rejected", "expired"
    };

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string status = "", CancellationToken ct = default)
    {
        if (!rbac.HasPermission(TemplatesRead)) return Forbid();
        var q = db.Templates.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId && x.Channel == ChannelType.Sms);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = status.Trim().ToLowerInvariant();
            q = q.Where(x => x.LifecycleStatus.ToLower() == s || x.Status.ToLower() == s);
        }
        var rows = await q.OrderByDescending(x => x.CreatedAtUtc).Take(500).ToListAsync(ct);
        return Ok(rows.Select(x => new
        {
            x.Id,
            x.Name,
            x.Category,
            x.Language,
            x.Body,
            x.SmsSenderId,
            x.DltEntityId,
            x.DltTemplateId,
            x.SmsOperator,
            x.Status,
            x.LifecycleStatus,
            x.RejectionReason,
            x.EffectiveFromUtc,
            x.EffectiveToUtc,
            x.CreatedAtUtc
        }));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        if (!rbac.HasPermission(TemplatesWrite)) return Forbid();
        var row = await db.Templates.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenancy.TenantId && x.Channel == ChannelType.Sms, ct);
        if (row is null) return NotFound();
        db.Templates.Remove(row);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    public sealed class UpsertSmsTemplateRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = "service";
        public string Language { get; set; } = "en";
        public string Body { get; set; } = string.Empty;
        public string SmsSenderId { get; set; } = string.Empty;
        public string DltEntityId { get; set; } = string.Empty;
        public string DltTemplateId { get; set; } = string.Empty;
        public string SmsOperator { get; set; } = "all";
        public DateTime? EffectiveFromUtc { get; set; }
        public DateTime? EffectiveToUtc { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UpsertSmsTemplateRequest request, CancellationToken ct)
    {
        if (!rbac.HasPermission(TemplatesWrite)) return Forbid();
        var body = InputGuardService.RequireTrimmed(request.Body, "Template body", 2000);
        var name = InputGuardService.RequireTrimmed(request.Name, "Template name", 120);
        var senderId = InputGuardService.RequireTrimmed(request.SmsSenderId, "Sender ID", 20);
        var entityId = InputGuardService.RequireTrimmed(request.DltEntityId, "DLT Entity ID", 50);
        var templateId = InputGuardService.RequireTrimmed(request.DltTemplateId, "DLT Template ID", 80);
        var category = string.IsNullOrWhiteSpace(request.Category) ? "service" : request.Category.Trim().ToLowerInvariant();
        var language = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language.Trim().ToLowerInvariant();
        var smsOperator = string.IsNullOrWhiteSpace(request.SmsOperator) ? "all" : request.SmsOperator.Trim().ToLowerInvariant();

        var row = new Template
        {
            Id = Guid.NewGuid(),
            TenantId = tenancy.TenantId,
            Channel = ChannelType.Sms,
            Name = name,
            Category = category,
            Language = language,
            Body = body,
            SmsSenderId = senderId,
            DltEntityId = entityId,
            DltTemplateId = templateId,
            SmsOperator = smsOperator,
            LifecycleStatus = "draft",
            Status = "Draft",
            EffectiveFromUtc = request.EffectiveFromUtc,
            EffectiveToUtc = request.EffectiveToUtc,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Templates.Add(row);
        await db.SaveChangesAsync(ct);
        return Ok(row);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpsertSmsTemplateRequest request, CancellationToken ct)
    {
        if (!rbac.HasPermission(TemplatesWrite)) return Forbid();
        var row = await db.Templates.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenancy.TenantId && x.Channel == ChannelType.Sms, ct);
        if (row is null) return NotFound();

        row.Name = InputGuardService.RequireTrimmed(request.Name, "Template name", 120);
        row.Category = string.IsNullOrWhiteSpace(request.Category) ? "service" : request.Category.Trim().ToLowerInvariant();
        row.Language = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language.Trim().ToLowerInvariant();
        row.Body = InputGuardService.RequireTrimmed(request.Body, "Template body", 2000);
        row.SmsSenderId = InputGuardService.RequireTrimmed(request.SmsSenderId, "Sender ID", 20);
        row.DltEntityId = InputGuardService.RequireTrimmed(request.DltEntityId, "DLT Entity ID", 50);
        row.DltTemplateId = InputGuardService.RequireTrimmed(request.DltTemplateId, "DLT Template ID", 80);
        row.SmsOperator = string.IsNullOrWhiteSpace(request.SmsOperator) ? "all" : request.SmsOperator.Trim().ToLowerInvariant();
        row.EffectiveFromUtc = request.EffectiveFromUtc;
        row.EffectiveToUtc = request.EffectiveToUtc;
        await db.SaveChangesAsync(ct);
        return Ok(row);
    }

    public sealed class SmsTemplateStatusRequest
    {
        public string Status { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    [HttpPost("{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] SmsTemplateStatusRequest request, CancellationToken ct)
    {
        if (!rbac.HasPermission(TemplatesWrite)) return Forbid();
        var row = await db.Templates.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenancy.TenantId && x.Channel == ChannelType.Sms, ct);
        if (row is null) return NotFound();
        var status = (request.Status ?? string.Empty).Trim().ToLowerInvariant();
        if (!AllowedStatuses.Contains(status))
            return BadRequest("Status must be draft/submitted/approved/rejected/expired.");

        row.LifecycleStatus = status;
        row.Status = status switch
        {
            "approved" => "Approved",
            "submitted" => "InReview",
            "rejected" => "Rejected",
            "expired" => "Expired",
            _ => "Draft"
        };
        row.RejectionReason = status == "rejected" ? (request.Reason ?? string.Empty).Trim() : string.Empty;
        await db.SaveChangesAsync(ct);
        return Ok(new { ok = true, row.Id, row.Status, row.LifecycleStatus, row.RejectionReason });
    }

    [HttpPost("import-approved-csv")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> ImportApprovedCsv([FromForm] IFormFile? file, CancellationToken ct = default)
    {
        if (!rbac.HasPermission(TemplatesWrite)) return Forbid();
        if (file is null || file.Length <= 0) return BadRequest("CSV file is required.");
        if (file.Length > 5 * 1024 * 1024) return BadRequest("CSV file is too large. Max 5MB.");

        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, true);
        var headerLine = await reader.ReadLineAsync(ct);
        if (string.IsNullOrWhiteSpace(headerLine))
            return BadRequest("CSV header is missing.");

        var headers = ParseCsvLine(headerLine).Select(x => x.Trim()).ToList();
        var map = BuildHeaderMap(headers);

        var rowNo = 1;
        var imported = 0;
        var updated = 0;
        var rejected = 0;
        var errors = new List<object>();

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct);
            rowNo++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cols = ParseCsvLine(line);
            string GetValue(string key) => map.TryGetValue(key, out var idx) && idx < cols.Count ? (cols[idx] ?? string.Empty).Trim() : string.Empty;

            var entityId = GetValue("entityid");
            var templateName = GetValue("templatename");
            var templateId = GetValue("templateid");
            var templateContent = GetValue("templatecontent");
            var header = GetValue("header");
            var templateType = NormalizeCategory(GetValue("templatetype"));
            var senderId = GetValue("senderid");
            var smsOperator = NormalizeOperator(GetValue("operator"));

            try
            {
                if (string.IsNullOrWhiteSpace(entityId)) throw new InvalidOperationException("EntityID is required.");
                if (string.IsNullOrWhiteSpace(templateName)) throw new InvalidOperationException("TemplateName is required.");
                if (string.IsNullOrWhiteSpace(templateId)) throw new InvalidOperationException("TemplateID is required.");
                if (string.IsNullOrWhiteSpace(templateContent)) throw new InvalidOperationException("TemplateContent is required.");

                entityId = InputGuardService.RequireTrimmed(entityId, "Entity ID", 50);
                templateName = InputGuardService.RequireTrimmed(templateName, "Template name", 120);
                templateId = InputGuardService.RequireTrimmed(templateId, "Template ID", 80);
                templateContent = InputGuardService.RequireTrimmed(templateContent, "Template content", 2000);
                header = (header ?? string.Empty).Trim();
                if (header.Length > 240) header = header[..240];
                senderId = string.IsNullOrWhiteSpace(senderId) ? string.Empty : InputGuardService.RequireTrimmed(senderId, "Sender ID", 20);

                var existing = await db.Templates.FirstOrDefaultAsync(x =>
                    x.TenantId == tenancy.TenantId &&
                    x.Channel == ChannelType.Sms &&
                    x.DltTemplateId == templateId, ct);

                if (existing is null)
                {
                    db.Templates.Add(new Template
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenancy.TenantId,
                        Channel = ChannelType.Sms,
                        Name = templateName,
                        Category = templateType,
                        Language = "en",
                        Body = templateContent,
                        SmsSenderId = senderId,
                        DltEntityId = entityId,
                        DltTemplateId = templateId,
                        HeaderType = string.IsNullOrWhiteSpace(header) ? "none" : "text",
                        HeaderText = header,
                        SmsOperator = smsOperator,
                        LifecycleStatus = "approved",
                        Status = "Approved",
                        RejectionReason = string.Empty,
                        CreatedAtUtc = DateTime.UtcNow
                    });
                    imported++;
                }
                else
                {
                    existing.Name = templateName;
                    existing.Category = templateType;
                    existing.Body = templateContent;
                    existing.SmsSenderId = senderId;
                    existing.DltEntityId = entityId;
                    existing.HeaderType = string.IsNullOrWhiteSpace(header) ? "none" : "text";
                    existing.HeaderText = header;
                    existing.SmsOperator = smsOperator;
                    existing.LifecycleStatus = "approved";
                    existing.Status = "Approved";
                    existing.RejectionReason = string.Empty;
                    updated++;
                }
            }
            catch (Exception ex)
            {
                rejected++;
                errors.Add(new { row = rowNo, error = ex.Message });
            }
        }

        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            ok = true,
            imported,
            updated,
            rejected,
            tenantId = tenancy.TenantId,
            totalProcessed = imported + updated + rejected,
            errors = errors.Take(200).ToArray()
        });
    }

    private static Dictionary<string, int> BuildHeaderMap(IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
        {
            var normalized = NormalizeHeader(headers[i]);
            if (!string.IsNullOrWhiteSpace(normalized) && !map.ContainsKey(normalized))
                map[normalized] = i;
        }

        if (!map.ContainsKey("entityid") || !map.ContainsKey("templatename") || !map.ContainsKey("templateid") || !map.ContainsKey("templatecontent"))
            throw new InvalidOperationException("CSV must contain EntityID, TemplateName, TemplateID, TemplateContent columns.");

        return map;
    }

    private static string NormalizeHeader(string value)
    {
        var v = (value ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty);
        return v switch
        {
            "tenantid" => "tenantid",
            "entityid" => "entityid",
            "templatename" => "templatename",
            "templateid" => "templateid",
            "templatecontent" => "templatecontent",
            "header" => "header",
            "templatetype" => "templatetype",
            "status" => "status",
            "senderid" => "senderid",
            "smssenderid" => "senderid",
            "operator" => "operator",
            _ => string.Empty
        };
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                    continue;
                }
                inQuotes = !inQuotes;
                continue;
            }
            if (ch == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(ch);
        }
        result.Add(current.ToString());
        return result;
    }

    private static string NormalizeCategory(string value)
    {
        var v = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (v is "otp" or "service" or "transactional" or "promotional") return v;
        if (v.Contains("trans")) return "transactional";
        if (v.Contains("promo")) return "promotional";
        if (v.Contains("otp")) return "otp";
        return "service";
    }

    private static string NormalizeOperator(string value)
    {
        var v = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (v is "jio" or "vi" or "airtel" or "all") return v;
        return "all";
    }
}
