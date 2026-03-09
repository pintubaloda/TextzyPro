using Microsoft.AspNetCore.Mvc;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/sms/senders")]
[Route("api/sms/sender")]
public class SmsSendersController(TenantDbContext db, TenancyContext tenancy, RbacService rbac) : ControllerBase
{
    private static readonly HashSet<string> AllowedRouteTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "service_implicit",
        "service_explicit",
        "transactional",
        "promotional"
    };

    private static bool IsValidEntityId(string value)
        => System.Text.RegularExpressions.Regex.IsMatch(value ?? string.Empty, "^[0-9]{19}$");

    private static bool IsValidSenderId(string value)
        => System.Text.RegularExpressions.Regex.IsMatch(value ?? string.Empty, "^[A-Z0-9]{3,6}$");

    [HttpGet]
    public IActionResult List()
    {
        if (!rbac.HasPermission(TemplatesRead)) return Forbid();
        return Ok(db.SmsSenders.Where(x => x.TenantId == tenancy.TenantId && x.IsActive).OrderBy(x => x.SenderId).ToList());
    }

    [HttpGet("stats")]
    public IActionResult Stats()
    {
        if (!rbac.HasPermission(TemplatesRead)) return Forbid();
        var rows = db.SmsSenders.Where(x => x.TenantId == tenancy.TenantId && x.IsActive).ToList();
        var compliant = rows.Count(x => IsValidEntityId(x.EntityId) && IsValidSenderId(x.SenderId) && x.IsVerified);
        return Ok(new
        {
            total = rows.Count,
            verified = rows.Count(x => x.IsVerified),
            compliant,
            byRoute = rows
                .GroupBy(x => string.IsNullOrWhiteSpace(x.RouteType) ? "unknown" : x.RouteType.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.Count())
        });
    }

    public class UpsertSenderRequest
    {
        public string SenderId { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public string RouteType { get; set; } = "service_explicit";
        public string Purpose { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsVerified { get; set; } = false;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UpsertSenderRequest request, CancellationToken ct)
    {
        if (!rbac.HasPermission(TemplatesWrite)) return Forbid();
        var sender = (request.SenderId ?? string.Empty).Trim().ToUpperInvariant();
        var entityId = (request.EntityId ?? string.Empty).Trim();
        var routeType = (request.RouteType ?? string.Empty).Trim().ToLowerInvariant();
        if (!IsValidSenderId(sender)) return BadRequest("Sender ID must be 3-6 uppercase letters/numbers for India DLT.");
        if (!IsValidEntityId(entityId)) return BadRequest("DLT Entity ID must be exactly 19 digits.");
        if (!AllowedRouteTypes.Contains(routeType)) return BadRequest("Route type must be service_implicit, service_explicit, transactional, or promotional.");

        var exists = db.SmsSenders.FirstOrDefault(x => x.TenantId == tenancy.TenantId && x.SenderId == sender && x.IsActive);
        if (exists is not null) return Ok(exists);

        var row = new SmsSender
        {
            Id = Guid.NewGuid(),
            TenantId = tenancy.TenantId,
            SenderId = sender,
            EntityId = entityId,
            RouteType = routeType,
            Purpose = (request.Purpose ?? string.Empty).Trim(),
            Description = (request.Description ?? string.Empty).Trim(),
            IsVerified = request.IsVerified,
            VerifiedAtUtc = request.IsVerified ? DateTime.UtcNow : null,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.SmsSenders.Add(row);
        await db.SaveChangesAsync(ct);
        return Ok(row);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpsertSenderRequest request, CancellationToken ct)
    {
        if (!rbac.HasPermission(TemplatesWrite)) return Forbid();
        var row = db.SmsSenders.FirstOrDefault(x => x.Id == id && x.TenantId == tenancy.TenantId && x.IsActive);
        if (row is null) return NotFound();

        var sender = (request.SenderId ?? string.Empty).Trim().ToUpperInvariant();
        var entityId = (request.EntityId ?? string.Empty).Trim();
        var routeType = (request.RouteType ?? string.Empty).Trim().ToLowerInvariant();
        if (!IsValidSenderId(sender)) return BadRequest("Sender ID must be 3-6 uppercase letters/numbers for India DLT.");
        if (!IsValidEntityId(entityId)) return BadRequest("DLT Entity ID must be exactly 19 digits.");
        if (!AllowedRouteTypes.Contains(routeType)) return BadRequest("Route type must be service_implicit, service_explicit, transactional, or promotional.");

        var duplicate = db.SmsSenders.FirstOrDefault(x => x.TenantId == tenancy.TenantId && x.SenderId == sender && x.IsActive && x.Id != id);
        if (duplicate is not null) return BadRequest("Sender ID already exists.");

        row.SenderId = sender;
        row.EntityId = entityId;
        row.RouteType = routeType;
        row.Purpose = (request.Purpose ?? string.Empty).Trim();
        row.Description = (request.Description ?? string.Empty).Trim();
        row.IsVerified = request.IsVerified;
        row.VerifiedAtUtc = request.IsVerified ? (row.VerifiedAtUtc ?? DateTime.UtcNow) : null;
        await db.SaveChangesAsync(ct);
        return Ok(row);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!rbac.HasPermission(TemplatesWrite)) return Forbid();
        var row = db.SmsSenders.FirstOrDefault(x => x.Id == id && x.TenantId == tenancy.TenantId && x.IsActive);
        if (row is null) return NotFound();
        row.IsActive = false;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
