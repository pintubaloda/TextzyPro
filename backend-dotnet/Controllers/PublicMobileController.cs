using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Services;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/public/mobile")]
public class PublicMobileController(
    ControlDbContext db,
    SecretCryptoService crypto) : ControllerBase
{
    [HttpGet("download")]
    public async Task<IActionResult> GetDownloadInfo(CancellationToken ct)
    {
        var entries = await db.PlatformSettings
            .Where(x => x.Scope == "mobile-app")
            .ToListAsync(ct);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            values[e.Key] = crypto.Decrypt(e.ValueEncrypted);
        }

        var apkUrl = GetSetting(values, "androidApkUrl", string.Empty);
        var versionName = GetSetting(values, "androidVersionName", string.Empty);
        var versionCode = GetSetting(values, "androidVersionCode", string.Empty);
        var releaseNotesUrl = GetSetting(values, "androidReleaseNotesUrl", string.Empty);
        var appName = GetSetting(values, "appName", "Textzy");

        return Ok(new
        {
            enabled = !string.IsNullOrWhiteSpace(apkUrl),
            appName,
            apkUrl,
            versionName,
            versionCode,
            releaseNotesUrl
        });
    }

    private static string GetSetting(Dictionary<string, string> values, string key, string fallback)
    {
        return values.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw)
            ? raw.Trim()
            : fallback;
    }
}
