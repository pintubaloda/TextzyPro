using Microsoft.AspNetCore.Mvc;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/chatbot-config")]
public class ChatbotController(TenantDbContext db, TenancyContext tenancy, RbacService rbac) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        if (!rbac.HasPermission(AutomationRead)) return Forbid();
        var cfg = db.ChatbotConfigs.FirstOrDefault(x => x.TenantId == tenancy.TenantId)
                  ?? new ChatbotConfig { Id = Guid.NewGuid(), TenantId = tenancy.TenantId };
        return Ok(cfg);
    }

    [HttpPut]
    public async Task<IActionResult> Upsert([FromBody] ChatbotConfig request, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationWrite)) return Forbid();
        string greeting;
        string fallback;
        try
        {
            greeting = InputGuardService.RequireTrimmed(request.Greeting, "Greeting", 2000);
            fallback = InputGuardService.RequireTrimmed(request.Fallback, "Fallback", 2000);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        var cfg = db.ChatbotConfigs.FirstOrDefault(x => x.TenantId == tenancy.TenantId);
        if (cfg is null)
        {
            cfg = new ChatbotConfig { Id = Guid.NewGuid(), TenantId = tenancy.TenantId };
            db.ChatbotConfigs.Add(cfg);
        }
        cfg.Greeting = greeting;
        cfg.Fallback = fallback;
        cfg.HandoffEnabled = request.HandoffEnabled;
        cfg.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(cfg);
    }
}
