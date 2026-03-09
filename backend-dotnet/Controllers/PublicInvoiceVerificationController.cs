using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/public/invoices")]
public class PublicInvoiceVerificationController(
    ControlDbContext db,
    SecretCryptoService crypto,
    IConfiguration config) : ControllerBase
{
    [HttpGet("verify")]
    public async Task<IActionResult> Verify([FromQuery] Guid invoiceId, [FromQuery] string hash = "", CancellationToken ct = default)
    {
        if (invoiceId == Guid.Empty)
            return Content(InvoiceDocumentRenderer.BuildVerificationPageHtml(false, "Invoice identifier is missing.", null, await GetPlatformBrandingAsync(ct), new InvoiceBuyerProfile()), "text/html; charset=utf-8");

        var invoice = await db.BillingInvoices.AsNoTracking().FirstOrDefaultAsync(x => x.Id == invoiceId, ct);
        var branding = await GetPlatformBrandingAsync(ct);
        if (invoice is null)
            return Content(InvoiceDocumentRenderer.BuildVerificationPageHtml(false, "This invoice does not exist in the system.", null, branding, new InvoiceBuyerProfile()), "text/html; charset=utf-8");

        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == invoice.TenantId, ct);
        var profile = await db.TenantCompanyProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == invoice.TenantId, ct);
        var buyer = new InvoiceBuyerProfile
        {
            CompanyName = string.IsNullOrWhiteSpace(profile?.CompanyName) ? (tenant?.Name ?? "Customer") : profile.CompanyName,
            LegalName = string.IsNullOrWhiteSpace(profile?.LegalName) ? (string.IsNullOrWhiteSpace(profile?.CompanyName) ? (tenant?.Name ?? "Customer") : profile.CompanyName) : profile.LegalName,
            BillingEmail = profile?.BillingEmail ?? string.Empty,
            Address = profile?.Address ?? string.Empty,
            Gstin = profile?.Gstin ?? string.Empty,
            Pan = profile?.Pan ?? string.Empty
        };

        var expected = InvoiceIntegrityHasher.Compute(invoice);
        var legacyExpected = InvoiceIntegrityHasher.ComputeLegacy(invoice);
        var storedHashValid = string.Equals(invoice.IntegrityHash, expected, StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(invoice.IntegrityHash, legacyExpected, StringComparison.OrdinalIgnoreCase);
        var requestMatchesStored = !string.IsNullOrWhiteSpace(hash) &&
                                   string.Equals(hash, invoice.IntegrityHash, StringComparison.OrdinalIgnoreCase);
        var requestHashValid = requestMatchesStored ||
                               (!string.IsNullOrWhiteSpace(hash) &&
                                (string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(hash, legacyExpected, StringComparison.OrdinalIgnoreCase)));
        var legacyManualCompatibility = !storedHashValid &&
                                        requestMatchesStored &&
                                        invoice.ReferenceNo.StartsWith("manual-plan:", StringComparison.OrdinalIgnoreCase);

        var valid = requestHashValid && (storedHashValid || legacyManualCompatibility);
        var message = valid
            ? "This invoice is authentic and verified in real time."
            : "This invoice is invalid or the verification token does not match.";

        return Content(InvoiceDocumentRenderer.BuildVerificationPageHtml(valid, message, invoice, branding, buyer), "text/html; charset=utf-8");
    }

    private async Task<InvoiceSellerProfile> GetPlatformBrandingAsync(CancellationToken ct)
    {
        var rows = await db.PlatformSettings
            .AsNoTracking()
            .Where(x => x.Scope == "platform-branding")
            .ToListAsync(ct);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
            values[row.Key] = crypto.Decrypt(row.ValueEncrypted);

        var platformName = (values.TryGetValue("platformName", out var pn) ? pn : "Textzy").Trim();
        var legalName = (values.TryGetValue("legalName", out var ln) ? ln : platformName).Trim();
        return new InvoiceSellerProfile
        {
            PlatformName = string.IsNullOrWhiteSpace(platformName) ? "Textzy" : platformName,
            LogoUrl = (values.TryGetValue("logoUrl", out var logoUrl) ? logoUrl : string.Empty).Trim(),
            LegalName = legalName,
            Address = (values.TryGetValue("address", out var address) ? address : string.Empty).Trim(),
            Gstin = (values.TryGetValue("gstin", out var gst) ? gst : string.Empty).Trim(),
            Pan = (values.TryGetValue("pan", out var pan) ? pan : string.Empty).Trim(),
            Cin = (values.TryGetValue("cin", out var cin) ? cin : string.Empty).Trim(),
            BillingEmail = (values.TryGetValue("billingEmail", out var email) ? email : string.Empty).Trim(),
            BillingPhone = (values.TryGetValue("billingPhone", out var phone) ? phone : string.Empty).Trim(),
            Website = NormalizeBaseUrl(values.TryGetValue("website", out var website) ? website : string.Empty) ?? NormalizeBaseUrl(config["APP_BASE_URL"]) ?? string.Empty,
            InvoiceFooter = (values.TryGetValue("invoiceFooter", out var footer) ? footer : string.Empty).Trim()
        };
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
