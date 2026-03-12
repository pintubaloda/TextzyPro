using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController(IHostEnvironment env) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        // Must remain cheap: no DB, no tenant resolution, no external calls.
        var asm = Assembly.GetExecutingAssembly();
        var version = asm.GetName().Version?.ToString() ?? string.Empty;

        return Ok(new
        {
            ok = true,
            service = "textzy-api",
            env = env.EnvironmentName,
            version,
            timeUtc = DateTime.UtcNow
        });
    }
}

