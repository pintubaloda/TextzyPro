using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;

namespace Textzy.Api.Services;

public sealed class InvoiceAttachmentService(
    ControlDbContext db,
    SecretCryptoService crypto,
    IConfiguration config)
{
    public async Task<EmailAttachment> BuildPdfAttachmentAsync(BillingInvoice invoice, HttpRequest? request, CancellationToken ct = default)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == invoice.TenantId, ct);
        var profile = await db.TenantCompanyProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == invoice.TenantId, ct);
        var branding = await GetPlatformBrandingAsync(ct);
        var integrityHash = await EnsureInvoiceIntegrityHashAsync(invoice, ct);
        var verificationUrl = BuildPublicInvoiceVerificationUrl(invoice.Id, integrityHash, request);

        var companyName = string.IsNullOrWhiteSpace(profile?.CompanyName)
            ? (tenant?.Name ?? "Textzy Workspace")
            : profile!.CompanyName;
        var legalName = string.IsNullOrWhiteSpace(profile?.LegalName)
            ? companyName
            : profile!.LegalName;

        var bytes = InvoicePdfRenderer.BuildInvoicePdf(
            invoice,
            new InvoiceSellerProfile
            {
                PlatformName = branding.PlatformName,
                LegalName = branding.LegalName,
                Address = branding.Address,
                Gstin = branding.Gstin,
                Pan = branding.Pan,
                Cin = branding.Cin,
                BillingEmail = branding.BillingEmail,
                BillingPhone = branding.BillingPhone,
                Website = branding.Website,
                InvoiceFooter = branding.InvoiceFooter
            },
            new InvoiceBuyerProfile
            {
                CompanyName = companyName,
                LegalName = legalName,
                BillingEmail = profile?.BillingEmail ?? string.Empty,
                Address = profile?.Address ?? string.Empty,
                Gstin = profile?.Gstin ?? string.Empty,
                Pan = profile?.Pan ?? string.Empty
            },
            profile?.TaxRatePercent ?? 18m,
            profile?.IsTaxExempt ?? false,
            profile?.IsReverseCharge ?? false,
            verificationUrl);

        var fileName = string.IsNullOrWhiteSpace(invoice.InvoiceNo) ? invoice.Id.ToString("N") : SanitizeFileName(invoice.InvoiceNo);
        return new EmailAttachment($"{fileName}.pdf", "application/pdf", bytes);
    }

    private async Task<string> EnsureInvoiceIntegrityHashAsync(BillingInvoice invoice, CancellationToken ct)
    {
        var expected = InvoiceIntegrityHasher.Compute(invoice);
        var legacyExpected = InvoiceIntegrityHasher.ComputeLegacy(invoice);
        if (string.Equals(invoice.IntegrityHash, expected, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invoice.IntegrityHash, legacyExpected, StringComparison.OrdinalIgnoreCase))
        {
            return invoice.IntegrityHash;
        }

        var entry = db.Entry(invoice);
        if (entry.State == EntityState.Detached)
            db.BillingInvoices.Attach(invoice);

        invoice.IntegrityAlgo = "SHA256";
        invoice.IntegrityHash = expected;
        await db.SaveChangesAsync(ct);
        return expected;
    }

    private async Task<PlatformBrandingSnapshot> GetPlatformBrandingAsync(CancellationToken ct)
    {
        var rows = await db.PlatformSettings
            .AsNoTracking()
            .Where(x => x.Scope == "platform-branding")
            .ToListAsync(ct);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
            values[row.Key] = crypto.Decrypt(row.ValueEncrypted);

        var platformName = (values.TryGetValue("platformName", out var rawPlatformName) ? rawPlatformName : "Textzy").Trim();
        var legalName = (values.TryGetValue("legalName", out var rawLegalName) ? rawLegalName : platformName).Trim();

        return new PlatformBrandingSnapshot
        {
            PlatformName = string.IsNullOrWhiteSpace(platformName) ? "Textzy" : platformName,
            LegalName = string.IsNullOrWhiteSpace(legalName) ? (string.IsNullOrWhiteSpace(platformName) ? "Textzy" : platformName) : legalName,
            Gstin = (values.TryGetValue("gstin", out var gstin) ? gstin : string.Empty).Trim(),
            Pan = (values.TryGetValue("pan", out var pan) ? pan : string.Empty).Trim(),
            Cin = (values.TryGetValue("cin", out var cin) ? cin : string.Empty).Trim(),
            BillingEmail = (values.TryGetValue("billingEmail", out var billingEmail) ? billingEmail : string.Empty).Trim(),
            BillingPhone = (values.TryGetValue("billingPhone", out var billingPhone) ? billingPhone : string.Empty).Trim(),
            Website = (values.TryGetValue("website", out var website) ? website : string.Empty).Trim(),
            Address = (values.TryGetValue("address", out var address) ? address : string.Empty).Trim(),
            InvoiceFooter = (values.TryGetValue("invoiceFooter", out var footer) ? footer : string.Empty).Trim()
        };
    }

    private string BuildPublicInvoiceVerificationUrl(Guid invoiceId, string integrityHash, HttpRequest? request)
    {
        var baseUrl = NormalizeBaseUrl(config["PUBLIC_API_BASE_URL"])
            ?? NormalizeBaseUrl(config["API_BASE_URL"])
            ?? (request is not null ? $"{request.Scheme}://{request.Host}" : null)
            ?? "http://localhost";

        return $"{baseUrl}/api/public/invoices/verify?invoiceId={Uri.EscapeDataString(invoiceId.ToString())}&hash={Uri.EscapeDataString(integrityHash ?? string.Empty)}";
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

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(invalid.Contains(ch) ? '-' : ch);
        }

        return builder.ToString().Trim();
    }

    private sealed class PlatformBrandingSnapshot
    {
        public string PlatformName { get; init; } = "Textzy";
        public string LegalName { get; init; } = "Textzy";
        public string Gstin { get; init; } = string.Empty;
        public string Pan { get; init; } = string.Empty;
        public string Cin { get; init; } = string.Empty;
        public string BillingEmail { get; init; } = string.Empty;
        public string BillingPhone { get; init; } = string.Empty;
        public string Website { get; init; } = string.Empty;
        public string Address { get; init; } = string.Empty;
        public string InvoiceFooter { get; init; } = string.Empty;
    }
}
