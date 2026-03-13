using Microsoft.AspNetCore.Mvc;
using Textzy.Api.Services;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/public/status")]
public class PublicStatusController(
    IConfiguration config,
    OutboundMessageQueueService outboundQueue,
    WabaWebhookQueueService webhookQueue) : ControllerBase
{
    private static readonly DateTime StartedAtUtc = DateTime.UtcNow;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var incidentLevel = (config["Status:IncidentLevel"] ?? string.Empty).Trim();
        var incidentMessage = (config["Status:IncidentMessage"] ?? string.Empty).Trim();

        object? incident = null;
        if (!string.IsNullOrWhiteSpace(incidentLevel) || !string.IsNullOrWhiteSpace(incidentMessage))
        {
            incident = new { level = incidentLevel, message = incidentMessage };
        }

        var asm = typeof(PublicStatusController).Assembly;
        var version = asm.GetName().Version?.ToString() ?? "unknown";

        return Ok(new
        {
            service = "textzy-api",
            version,
            timeUtc = DateTime.UtcNow,
            uptimeSec = (int)Math.Max(0, (DateTime.UtcNow - StartedAtUtc).TotalSeconds),
            queues = new
            {
                outbound = new { provider = outboundQueue.ActiveProvider, depth = await outboundQueue.GetDepthAsync(ct) },
                webhook = new { provider = webhookQueue.ActiveProvider, depth = await webhookQueue.GetDepthAsync(ct) }
            },
            incident
        });
    }
}

