using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Services;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/public/app-updates")]
public class PublicAppUpdatesController(
    ControlDbContext db,
    SecretCryptoService crypto) : ControllerBase
{
    [HttpGet("manifest")]
    public async Task<IActionResult> GetManifest(
        [FromQuery] string? platform = null,
        [FromQuery] string? appVersion = null,
        CancellationToken ct = default)
    {
        var entries = await db.PlatformSettings
            .Where(x => x.Scope == "mobile-app")
            .ToListAsync(ct);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
            values[e.Key] = crypto.Decrypt(e.ValueEncrypted);

        var appName = GetSetting(values, "appName", "Textzy");
        var supportUrl = GetSetting(values, "supportUrl", string.Empty);
        var termsUrl = GetSetting(values, "termsUrl", string.Empty);
        var privacyUrl = GetSetting(values, "privacyUrl", string.Empty);

        var android = BuildPlatformManifest(
            values,
            "android",
            latestVersion: GetSetting(values, "androidVersionName", string.Empty),
            minSupportedVersion: GetSetting(values, "androidMinSupportedVersion", GetSetting(values, "minSupportedAppVersion", string.Empty)),
            buildCode: GetSetting(values, "androidVersionCode", string.Empty),
            downloadUrl: GetSetting(values, "androidApkUrl", string.Empty),
            releaseNotesUrl: GetSetting(values, "androidReleaseNotesUrl", string.Empty),
            forceUpdate: ParseBool(GetSetting(values, "androidForceUpdate", "false")));

        var ios = BuildPlatformManifest(
            values,
            "ios",
            latestVersion: GetSetting(values, "iosVersionName", string.Empty),
            minSupportedVersion: GetSetting(values, "iosMinSupportedVersion", GetSetting(values, "minSupportedAppVersion", string.Empty)),
            buildCode: GetSetting(values, "iosBuildNumber", string.Empty),
            downloadUrl: GetSetting(values, "iosStoreUrl", GetSetting(values, "iosDownloadUrl", string.Empty)),
            releaseNotesUrl: GetSetting(values, "iosReleaseNotesUrl", string.Empty),
            forceUpdate: ParseBool(GetSetting(values, "iosForceUpdate", "false")));

        var windows = BuildPlatformManifest(
            values,
            "windows",
            latestVersion: GetSetting(values, "windowsVersionName", string.Empty),
            minSupportedVersion: GetSetting(values, "windowsMinSupportedVersion", GetSetting(values, "minSupportedAppVersion", string.Empty)),
            buildCode: GetSetting(values, "windowsVersionCode", string.Empty),
            downloadUrl: GetSetting(values, "windowsDownloadUrl", string.Empty),
            releaseNotesUrl: GetSetting(values, "windowsReleaseNotesUrl", string.Empty),
            forceUpdate: ParseBool(GetSetting(values, "windowsForceUpdate", "false")));

        var macos = BuildPlatformManifest(
            values,
            "macos",
            latestVersion: GetSetting(values, "macosVersionName", string.Empty),
            minSupportedVersion: GetSetting(values, "macosMinSupportedVersion", GetSetting(values, "minSupportedAppVersion", string.Empty)),
            buildCode: GetSetting(values, "macosVersionCode", string.Empty),
            downloadUrl: GetSetting(values, "macosDownloadUrl", string.Empty),
            releaseNotesUrl: GetSetting(values, "macosReleaseNotesUrl", string.Empty),
            forceUpdate: ParseBool(GetSetting(values, "macosForceUpdate", "false")));

        var web = BuildPlatformManifest(
            values,
            "web",
            latestVersion: GetSetting(values, "webVersionName", string.Empty),
            minSupportedVersion: GetSetting(values, "webMinSupportedVersion", string.Empty),
            buildCode: GetSetting(values, "webVersionCode", string.Empty),
            downloadUrl: GetSetting(values, "webUrl", string.Empty),
            releaseNotesUrl: GetSetting(values, "webReleaseNotesUrl", string.Empty),
            forceUpdate: ParseBool(GetSetting(values, "webForceUpdate", "false")));

        var platformKey = NormalizePlatform(platform);
        object? current = null;
        if (!string.IsNullOrWhiteSpace(platformKey))
        {
            var p = platformKey switch
            {
                "android" => android,
                "ios" => ios,
                "windows" => windows,
                "macos" => macos,
                "web" => web,
                _ => null
            };

            if (p is not null)
            {
                var normalizedClientVersion = NormalizeVersion(appVersion);
                var normalizedLatest = NormalizeVersion(p.LatestVersion);
                var normalizedMinSupported = NormalizeVersion(p.MinSupportedVersion);
                current = new
                {
                    platform = platformKey,
                    appVersion = appVersion?.Trim() ?? string.Empty,
                    updateAvailable = CompareVersions(normalizedClientVersion, normalizedLatest) < 0,
                    supported = string.IsNullOrWhiteSpace(normalizedMinSupported) || CompareVersions(normalizedClientVersion, normalizedMinSupported) >= 0,
                    forceUpdate = p.ForceUpdate ||
                                  (!string.IsNullOrWhiteSpace(normalizedMinSupported) &&
                                   CompareVersions(normalizedClientVersion, normalizedMinSupported) < 0)
                };
            }
        }

        return Ok(new
        {
            generatedAtUtc = DateTime.UtcNow,
            app = new
            {
                appName,
                supportUrl,
                termsUrl,
                privacyUrl
            },
            platforms = new
            {
                android,
                ios,
                windows,
                macos,
                web
            },
            current
        });
    }

    private sealed record PlatformManifest(
        string Platform,
        string LatestVersion,
        string MinSupportedVersion,
        string BuildCode,
        string DownloadUrl,
        string ReleaseNotesUrl,
        bool ForceUpdate,
        bool Enabled);

    private static PlatformManifest BuildPlatformManifest(
        Dictionary<string, string> values,
        string platform,
        string latestVersion,
        string minSupportedVersion,
        string buildCode,
        string downloadUrl,
        string releaseNotesUrl,
        bool forceUpdate)
    {
        var enabledKey = $"{platform}UpdateEnabled";
        var enabled = values.TryGetValue(enabledKey, out var rawEnabled)
            ? ParseBool(rawEnabled)
            : true;

        return new PlatformManifest(
            Platform: platform,
            LatestVersion: latestVersion.Trim(),
            MinSupportedVersion: minSupportedVersion.Trim(),
            BuildCode: buildCode.Trim(),
            DownloadUrl: downloadUrl.Trim(),
            ReleaseNotesUrl: releaseNotesUrl.Trim(),
            ForceUpdate: forceUpdate,
            Enabled: enabled);
    }

    private static string GetSetting(Dictionary<string, string> values, string key, string fallback)
    {
        return values.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw)
            ? raw.Trim()
            : fallback;
    }

    private static bool ParseBool(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Trim().Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               value.Trim().Equals("y", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePlatform(string? platform)
    {
        var value = (platform ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            "android" => "android",
            "ios" => "ios",
            "iphone" => "ios",
            "ipad" => "ios",
            "windows" => "windows",
            "win" => "windows",
            "mac" => "macos",
            "macos" => "macos",
            "osx" => "macos",
            "web" => "web",
            _ => string.Empty
        };
    }

    private static string NormalizeVersion(string? version)
    {
        var input = (version ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(input)) return "0";
        var chars = input.Where(c => char.IsAsciiDigit(c) || c == '.').ToArray();
        var normalized = new string(chars).Trim('.');
        return string.IsNullOrWhiteSpace(normalized) ? "0" : normalized;
    }

    private static int CompareVersions(string left, string right)
    {
        var l = left.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var r = right.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var len = Math.Max(l.Length, r.Length);
        for (var i = 0; i < len; i++)
        {
            var li = i < l.Length && int.TryParse(l[i], out var lnum) ? lnum : 0;
            var ri = i < r.Length && int.TryParse(r[i], out var rnum) ? rnum : 0;
            if (li < ri) return -1;
            if (li > ri) return 1;
        }
        return 0;
    }
}
