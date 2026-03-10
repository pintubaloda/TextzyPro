using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Services;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/public/platform-branding")]
public class PublicPlatformBrandingController(
    ControlDbContext db,
    SecretCryptoService crypto,
    IConfiguration config) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var rows = await db.PlatformSettings
            .AsNoTracking()
            .Where(x => x.Scope == "platform-branding")
            .ToListAsync(ct);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
            values[row.Key] = crypto.Decrypt(row.ValueEncrypted);

        static string PickValue(Dictionary<string, string> source, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (source.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        var platformName = PickValue(values, "platformName");
        if (string.IsNullOrWhiteSpace(platformName)) platformName = "Textzy";

        var legalName = PickValue(values, "legalName");
        if (string.IsNullOrWhiteSpace(legalName)) legalName = platformName;

        return Ok(new
        {
            platformName,
            legalName,
            logoUrl = PickValue(values, "logoUrl"),
            website = NormalizeBaseUrl(PickValue(values, "website"))
                      ?? NormalizeBaseUrl(config["APP_BASE_URL"])
                      ?? string.Empty,
            billingEmail = PickValue(values, "billingEmail", "supportEmail", "email"),
            billingPhone = PickValue(values, "billingPhone", "supportPhone", "phone"),
            supportEmail = PickValue(values, "supportEmail", "billingEmail", "email"),
            supportPhone = PickValue(values, "supportPhone", "billingPhone", "phone"),
            supportWhatsappNo = PickValue(values, "supportWhatsappNo", "supportWhatsapp", "supportWhatsApp", "whatsapp"),
            billingAddress = PickValue(values, "billingAddress", "address", "contactAddress"),
            invoiceFooter = PickValue(values, "invoiceFooter")
        });
    }

    private static string? NormalizeBaseUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var value = raw.Trim();
        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            value = $"https://{value}";
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return null;
        if (string.IsNullOrWhiteSpace(uri.Host)) return null;

        var builder = new UriBuilder(uri)
        {
            Path = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri.ToString().TrimEnd('/');
    }
}
