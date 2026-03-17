using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/reports")]
public class ReportsController(TenantDbContext tenantDb, AuthContext auth, TenancyContext tenancy, RbacService rbac) : ControllerBase
{
    [HttpGet("whatsapp")]
    public async Task<IActionResult> WhatsAppMessages([FromQuery] int take = 200, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();
        if (!rbac.HasPermission(BillingRead) && !rbac.HasPermission(ApiRead)) return Forbid();

        take = Math.Clamp(take, 1, 2000);
        var rows = await tenantDb.Messages.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId && x.Channel == ChannelType.WhatsApp)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(take)
            .Select(x => new
            {
                x.Id,
                x.CampaignId,
                x.Recipient,
                x.Body,
                x.MessageType,
                x.Status,
                x.ProviderMessageId,
                x.IdempotencyKey,
                x.DeliveredAtUtc,
                x.ReadAtUtc,
                x.LastError,
                x.CreatedAtUtc
            })
            .ToListAsync(ct);

        return Ok(rows);
    }
}
