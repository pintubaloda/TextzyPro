using Microsoft.AspNetCore.Mvc;
using Textzy.Api.Services;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/public/queue-health")]
public class PublicQueueHealthController(
    OutboundMessageQueueService outboundQueue,
    WabaWebhookQueueService webhookQueue) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        return Ok(new
        {
            outbound = new
            {
                provider = outboundQueue.ActiveProvider,
                depth = await outboundQueue.GetDepthAsync(ct)
            },
            webhook = new
            {
                provider = webhookQueue.ActiveProvider,
                depth = await webhookQueue.GetDepthAsync(ct)
            }
        });
    }
}
