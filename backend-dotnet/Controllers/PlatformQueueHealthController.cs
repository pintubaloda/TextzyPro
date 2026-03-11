using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/platform/queue-health")]
public class PlatformQueueHealthController(
    ControlDbContext db,
    AuthContext auth,
    RbacService rbac,
    OutboundMessageQueueService outboundQueue,
    WabaWebhookQueueService webhookQueue) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        var now = DateTime.UtcNow;
        var deadLetter24h = await db.WebhookEvents.CountAsync(x => x.Status == "DeadLetter" && x.ReceivedAtUtc > now.AddHours(-24), ct);
        var retrying = await db.WebhookEvents.CountAsync(x => x.Status == "RetryScheduled", ct);
        var processing = await db.WebhookEvents.CountAsync(x => x.Status == "Processing", ct);
        var unmapped24h = await db.WebhookEvents.CountAsync(x => x.Status == "Unmapped" && x.ReceivedAtUtc > now.AddHours(-24), ct);
        var outboundRedis = outboundQueue.GetRedisHealth();
        var webhookRedis = webhookQueue.GetRedisHealth();
        var outboundRabbit = outboundQueue.GetRabbitHealth();
        var webhookRabbit = webhookQueue.GetRabbitHealth();

        static bool? Combine(bool? a, bool? b)
        {
            if (a == true || b == true) return true;
            if (a == false && b == false) return false;
            if (a == false || b == false) return false;
            return null;
        }

        var redisConnected = Combine(outboundRedis.Connected, webhookRedis.Connected);
        var rabbitConnected = Combine(outboundRabbit.Connected, webhookRabbit.Connected);

        return Ok(new
        {
            redis = new
            {
                provider = "redis",
                connected = redisConnected
            },
            rabbitmq = new
            {
                provider = "rabbitmq",
                connected = rabbitConnected
            },
            outbound = new
            {
                provider = outboundQueue.ActiveProvider,
                depth = await outboundQueue.GetDepthAsync(ct),
                connected = outboundQueue.ActiveProvider switch
                {
                    "redis" => outboundRedis.Connected,
                    "rabbitmq" => outboundRabbit.Connected,
                    _ => null
                }
            },
            webhook = new
            {
                provider = webhookQueue.ActiveProvider,
                depth = await webhookQueue.GetDepthAsync(ct),
                processing,
                retrying,
                deadLetter24h,
                unmapped24h,
                connected = webhookQueue.ActiveProvider switch
                {
                    "redis" => webhookRedis.Connected,
                    "rabbitmq" => webhookRabbit.Connected,
                    _ => null
                }
            }
        });
    }
}
