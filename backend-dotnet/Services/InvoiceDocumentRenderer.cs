using System.Net;
using Textzy.Api.Models;

namespace Textzy.Api.Services;

public sealed class InvoiceSellerProfile
{
    public string PlatformName { get; init; } = "Textzy";
    public string LogoUrl { get; init; } = string.Empty;
    public string LegalName { get; init; } = "TEXTZY DIGITAL SOLUTIONS PRIVATE LIMITED";
    public string Address { get; init; } = "Plot No. 456, Tech Park Building, Bandra Kurla Complex\nMumbai, Maharashtra 400051, India";
    public string Gstin { get; init; } = "27AAFCU5055K1ZO";
    public string Pan { get; init; } = "AAFCU5055K";
    public string Cin { get; init; } = "U74900MH2020PTC345678";
    public string BillingEmail { get; init; } = string.Empty;
    public string BillingPhone { get; init; } = string.Empty;
    public string Website { get; init; } = string.Empty;
    public string InvoiceFooter { get; init; } = "This is a computer-generated invoice.";
}

public sealed class InvoiceBuyerProfile
{
    public string CompanyName { get; init; } = string.Empty;
    public string LegalName { get; init; } = string.Empty;
    public string BillingEmail { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public string Gstin { get; init; } = string.Empty;
    public string Pan { get; init; } = string.Empty;
}

public static class InvoiceDocumentRenderer
{
    public static string BuildInvoiceHtml(
        BillingInvoice invoice,
        InvoiceSellerProfile sellerInput,
        InvoiceBuyerProfile buyerInput,
        decimal taxRatePercent,
        bool isTaxExempt,
        bool isReverseCharge,
        string verificationUrl,
        string qrCodeUrl)
    {
        var seller = NormalizeSeller(sellerInput);
        var buyer = NormalizeBuyer(buyerInput);

        var safePlatformName = Encode(seller.PlatformName);
        var safeLogoUrl = Encode(seller.LogoUrl);
        var safeSellerLegalName = Encode(seller.LegalName);
        var safeSellerAddress = EncodeMultiline($"Registered Address: {seller.Address}");
        var safeSellerGstin = Encode(seller.Gstin);
        var safeSellerPan = Encode(seller.Pan);
        var safeSellerCin = Encode(seller.Cin);
        var safeSellerEmail = Encode(seller.BillingEmail);
        var safeSellerPhone = Encode(seller.BillingPhone);
        var safeSellerWebsite = Encode(seller.Website);
        var safeFooter = Encode(seller.InvoiceFooter);
        var safeBuyerCompany = Encode(string.IsNullOrWhiteSpace(buyer.CompanyName) ? "Customer" : buyer.CompanyName);
        var safeBuyerLegal = Encode(string.IsNullOrWhiteSpace(buyer.LegalName) ? buyer.CompanyName : buyer.LegalName);
        var safeBuyerEmail = Encode(buyer.BillingEmail);
        var safeBuyerAddress = EncodeMultiline(buyer.Address);
        var safeBuyerGstin = Encode(buyer.Gstin);
        var safeBuyerPan = Encode(buyer.Pan);
        var safeInvoiceNo = Encode(invoice.InvoiceNo);
        var safeReference = Encode(invoice.ReferenceNo);
        var safeDescription = Encode(string.IsNullOrWhiteSpace(invoice.Description) ? "Platform service purchase" : invoice.Description);
        var safeBillingCycle = Encode((invoice.BillingCycle ?? string.Empty).Replace("_", " "));
        var safeVerificationUrl = Encode(verificationUrl);
        var safeQrCodeUrl = Encode(qrCodeUrl);
        var invoiceLabel = string.Equals(invoice.InvoiceKind, "proforma_invoice", StringComparison.OrdinalIgnoreCase) ? "PROFORMA INVOICE" : "TAX INVOICE";
        var safeInvoiceLabel = Encode(invoiceLabel);
        var taxLabel = isReverseCharge
            ? "GST payable under reverse charge"
            : isTaxExempt
                ? "GST exempt supply"
                : $"GST @ {Math.Clamp(taxRatePercent, 0m, 100m):0.##}%";
        var safeTaxLabel = Encode(taxLabel);
        var showPaidStamp = string.Equals(invoice.Status, "paid", StringComparison.OrdinalIgnoreCase);
        var paidStamp = showPaidStamp ? """<div class="paid-stamp">PAID</div>""" : string.Empty;
        var logoBlock = string.IsNullOrWhiteSpace(seller.LogoUrl)
            ? $"""<div class="company-name">{safePlatformName}</div>"""
            : $"""<img src="{safeLogoUrl}" alt="{safePlatformName}" class="company-logo" />""";

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="UTF-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
              <title>{{safeInvoiceNo}}</title>
              <style>
                * { margin: 0; padding: 0; box-sizing: border-box; }
                body { font-family: 'Courier New', Courier, monospace; background: #f4f4f4; padding: 12px; color: #000; }
                .invoice-container { background: #fff; max-width: 960px; margin: 0 auto; border: 2px solid #000; box-shadow: 0 4px 14px rgba(0,0,0,0.08); position: relative; }
                .top-line { border-bottom: 2px solid #000; }
                .company-header { padding: 16px 20px 14px; border-bottom: 1px solid #000; text-align: center; }
                .company-logo { max-height: 54px; max-width: 280px; object-fit: contain; margin: 0 auto 8px; display: block; }
                .company-name { font-size: 22px; font-weight: bold; letter-spacing: 2px; margin-bottom: 6px; }
                .company-subtitle { font-size: 12px; font-weight: bold; margin-bottom: 3px; }
                .company-details { font-size: 10px; line-height: 1.55; white-space: pre-line; }
                .invoice-title { padding: 8px 20px; text-align: center; font-weight: bold; font-size: 14px; letter-spacing: 1px; border-bottom: 1px solid #000; }
                .invoice-meta { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; padding: 8px 20px; border-bottom: 1px solid #000; font-size: 11px; }
                .meta-item { display: flex; justify-content: space-between; gap: 10px; }
                .meta-label { font-weight: bold; }
                .meta-value { text-align: right; }
                .party-section { display: grid; grid-template-columns: 1fr 1fr; border-bottom: 1px solid #000; }
                .party-box { padding: 10px 20px; font-size: 11px; line-height: 1.6; min-height: 180px; }
                .party-box:first-child { border-right: 1px solid #000; }
                .party-box-title { font-weight: bold; text-decoration: underline; margin-bottom: 6px; font-size: 12px; }
                .party-box strong { display: block; margin-bottom: 4px; }
                .items-table { width: 100%; border-collapse: collapse; border-bottom: 1px solid #000; }
                .items-table th, .items-table td { padding: 8px 10px; font-size: 10px; border-right: 1px solid #000; border-bottom: 1px solid #000; vertical-align: top; }
                .items-table th:last-child, .items-table td:last-child { border-right: none; }
                .items-table thead th { font-weight: bold; text-align: left; }
                .items-table .center { text-align: center; }
                .items-table .right { text-align: right; }
                .summary-section { display: grid; grid-template-columns: 1fr 1fr; border-bottom: 1px solid #000; }
                .summary-left { padding: 14px 20px; border-right: 1px solid #000; font-size: 10px; line-height: 1.7; }
                .summary-right { padding: 14px 20px; font-size: 11px; }
                .summary-title { font-weight: bold; text-decoration: underline; margin-bottom: 8px; }
                .summary-row { display: flex; justify-content: space-between; margin: 5px 0; }
                .summary-row strong { font-weight: bold; }
                .summary-total { border-top: 1px solid #000; margin-top: 8px; padding-top: 8px; font-size: 13px; font-weight: bold; }
                .footer-section { display: grid; grid-template-columns: 1fr 220px; gap: 0; }
                .footer-left { padding: 12px 20px 18px; border-right: 1px solid #000; font-size: 10px; line-height: 1.6; }
                .footer-right { padding: 12px; text-align: center; font-size: 10px; }
                .qr-box { border: 1px solid #000; padding: 8px; display: inline-block; background: #fff; }
                .qr-box img { width: 132px; height: 132px; display: block; }
                .verify-url { margin-top: 8px; word-break: break-all; font-size: 9px; line-height: 1.4; }
                .paid-stamp { position: absolute; top: 26px; right: 24px; border: 3px solid #1f9d55; color: #1f9d55; padding: 10px 18px; font-size: 18px; font-weight: bold; transform: rotate(-8deg); letter-spacing: 2px; background: rgba(255,255,255,0.9); }
              </style>
            </head>
            <body>
              <div class="invoice-container">
                <div class="top-line"></div>
                {{paidStamp}}
                <div class="company-header">
                  {{logoBlock}}
                  <div class="company-subtitle">{{safeSellerLegalName}}</div>
                  <div class="company-details">{{safeSellerAddress}}</div>
                  <div class="company-details">GSTIN: {{safeSellerGstin}} | PAN: {{safeSellerPan}} | CIN: {{safeSellerCin}}</div>
                  <div class="company-details">{{(string.IsNullOrWhiteSpace(seller.BillingEmail) ? string.Empty : $"Email: {safeSellerEmail} | ")}}{{(string.IsNullOrWhiteSpace(seller.BillingPhone) ? string.Empty : $"Phone: {safeSellerPhone}")}}</div>
                  <div class="company-details">{{(string.IsNullOrWhiteSpace(seller.Website) ? string.Empty : safeSellerWebsite)}}</div>
                </div>

                <div class="invoice-title">{{safeInvoiceLabel}}</div>

                <div class="invoice-meta">
                  <div class="meta-item"><span class="meta-label">Invoice No</span><span class="meta-value">{{safeInvoiceNo}}</span></div>
                  <div class="meta-item"><span class="meta-label">Invoice Date</span><span class="meta-value">{{invoice.IssuedAtUtc:yyyy-MM-dd}}</span></div>
                  <div class="meta-item"><span class="meta-label">Status</span><span class="meta-value">{{Encode(invoice.Status.ToUpperInvariant())}}</span></div>
                  <div class="meta-item"><span class="meta-label">Reference</span><span class="meta-value">{{(string.IsNullOrWhiteSpace(invoice.ReferenceNo) ? "-" : safeReference)}}</span></div>
                  <div class="meta-item"><span class="meta-label">Paid Date</span><span class="meta-value">{{(invoice.PaidAtUtc?.ToString("yyyy-MM-dd") ?? "-")}}</span></div>
                  <div class="meta-item"><span class="meta-label">Billing Cycle</span><span class="meta-value">{{safeBillingCycle}}</span></div>
                </div>

                <div class="party-section">
                  <div class="party-box">
                    <div class="party-box-title">Supplier</div>
                    <strong>{{safeSellerLegalName}}</strong>
                    <div>{{safeSellerAddress}}</div>
                    <div>GSTIN: {{safeSellerGstin}}</div>
                    <div>PAN: {{safeSellerPan}}</div>
                    <div>CIN: {{safeSellerCin}}</div>
                  </div>
                  <div class="party-box">
                    <div class="party-box-title">Bill To</div>
                    <strong>{{safeBuyerCompany}}</strong>
                    <div>{{safeBuyerLegal}}</div>
                    <div>{{(string.IsNullOrWhiteSpace(buyer.Address) ? "-" : safeBuyerAddress)}}</div>
                    <div>GSTIN: {{(string.IsNullOrWhiteSpace(buyer.Gstin) ? "-" : safeBuyerGstin)}}</div>
                    <div>PAN: {{(string.IsNullOrWhiteSpace(buyer.Pan) ? "-" : safeBuyerPan)}}</div>
                    <div>Email: {{(string.IsNullOrWhiteSpace(buyer.BillingEmail) ? "-" : safeBuyerEmail)}}</div>
                  </div>
                </div>

                <table class="items-table">
                  <thead>
                    <tr>
                      <th style="width:48px;">S.No</th>
                      <th>Description of Service</th>
                      <th style="width:96px;">HSN/SAC</th>
                      <th style="width:96px;">Qty</th>
                      <th style="width:120px;">Rate</th>
                      <th style="width:130px;">Amount</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr>
                      <td class="center">1</td>
                      <td>{{safeDescription}}<br />Reference: {{(string.IsNullOrWhiteSpace(invoice.ReferenceNo) ? "-" : safeReference)}}<br />Service period: {{invoice.PeriodStartUtc:yyyy-MM-dd}} to {{invoice.PeriodEndUtc:yyyy-MM-dd}}</td>
                      <td class="center">998314</td>
                      <td class="right">1</td>
                      <td class="right">{{invoice.Subtotal:0.00}}</td>
                      <td class="right"><strong>{{invoice.Subtotal:0.00}}</strong></td>
                    </tr>
                  </tbody>
                </table>

                <div class="summary-section">
                  <div class="summary-left">
                    <div class="summary-title">Tax Summary</div>
                    <div>{{safeTaxLabel}}</div>
                    <div>Supply type: {{(isReverseCharge ? "Reverse Charge" : isTaxExempt ? "Exempt" : "Taxable")}}</div>
                    <div>Verification token: {{Encode(invoice.IntegrityHash)}}</div>
                    <div style="margin-top:10px;">{{safeFooter}}</div>
                  </div>
                  <div class="summary-right">
                    <div class="summary-title">Invoice Value</div>
                    <div class="summary-row"><span>Taxable Value</span><strong>{{invoice.Subtotal:0.00}}</strong></div>
                    <div class="summary-row"><span>{{safeTaxLabel}}</span><strong>{{invoice.TaxAmount:0.00}}</strong></div>
                    <div class="summary-row summary-total"><span>Total Invoice Value</span><strong>{{invoice.Total:0.00}}</strong></div>
                  </div>
                </div>

                <div class="footer-section">
                  <div class="footer-left">
                    <div><strong>Verification</strong></div>
                    <div>Scan the QR code to verify this invoice in real time.</div>
                    <div class="verify-url">{{safeVerificationUrl}}</div>
                  </div>
                  <div class="footer-right">
                    <div class="qr-box">
                      <img src="{{safeQrCodeUrl}}" alt="Invoice verification QR" />
                    </div>
                  </div>
                </div>
              </div>
            </body>
            </html>
            """;
    }

    public static string BuildVerificationPageHtml(
        bool valid,
        string message,
        BillingInvoice? invoice,
        InvoiceSellerProfile sellerInput,
        InvoiceBuyerProfile buyerInput)
    {
        var seller = NormalizeSeller(sellerInput);
        var buyer = NormalizeBuyer(buyerInput);
        var safeMessage = Encode(message);
        var safePlatformName = Encode(seller.PlatformName);
        var safeLogoUrl = Encode(seller.LogoUrl);
        var safeLegalName = Encode(seller.LegalName);
        var safeInvoiceNo = Encode(invoice?.InvoiceNo ?? "-");
        var safeDescription = Encode(invoice?.Description ?? "-");
        var safeBuyer = Encode(string.IsNullOrWhiteSpace(buyer.CompanyName) ? "Customer" : buyer.CompanyName);
        var badgeClass = valid ? "valid" : "invalid";
        var badgeText = valid ? "VALID INVOICE" : "INVALID INVOICE";
        var logoBlock = string.IsNullOrWhiteSpace(seller.LogoUrl)
            ? $"""<div class="brand-name">{safePlatformName}</div>"""
            : $"""<img src="{safeLogoUrl}" alt="{safePlatformName}" class="brand-logo" />""";

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="UTF-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
              <title>Invoice Verification</title>
              <style>
                body { font-family: 'Segoe UI', Arial, sans-serif; margin: 0; background: #f5f7fb; color: #0f172a; }
                .wrap { max-width: 760px; margin: 32px auto; padding: 0 16px; }
                .card { background: #fff; border: 1px solid #dbe2ea; box-shadow: 0 12px 30px rgba(15, 23, 42, 0.08); }
                .header { padding: 24px; border-bottom: 1px solid #e2e8f0; text-align: center; }
                .brand-logo { max-height: 56px; max-width: 260px; object-fit: contain; display: block; margin: 0 auto 8px; }
                .brand-name { font-size: 28px; font-weight: 700; color: #c2410c; }
                .legal { font-size: 13px; color: #475569; margin-top: 6px; }
                .body { padding: 24px; }
                .badge { display: inline-block; padding: 10px 16px; font-size: 12px; font-weight: 700; letter-spacing: 0.08em; border-radius: 999px; }
                .badge.valid { background: #dcfce7; color: #166534; }
                .badge.invalid { background: #fee2e2; color: #991b1b; }
                .message { margin-top: 16px; font-size: 15px; color: #334155; }
                .grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 14px; margin-top: 22px; }
                .row { border: 1px solid #e2e8f0; padding: 14px; background: #f8fafc; }
                .label { font-size: 12px; color: #64748b; text-transform: uppercase; letter-spacing: 0.08em; }
                .value { margin-top: 6px; font-size: 16px; font-weight: 600; color: #0f172a; }
              </style>
            </head>
            <body>
              <div class="wrap">
                <div class="card">
                  <div class="header">
                    {{logoBlock}}
                    <div class="legal">{{safeLegalName}}</div>
                  </div>
                  <div class="body">
                    <span class="badge {{badgeClass}}">{{badgeText}}</span>
                    <div class="message">{{safeMessage}}</div>
                    <div class="grid">
                      <div class="row"><div class="label">Invoice No</div><div class="value">{{safeInvoiceNo}}</div></div>
                      <div class="row"><div class="label">Invoice Date</div><div class="value">{{(invoice?.IssuedAtUtc.ToString("yyyy-MM-dd") ?? "-")}}</div></div>
                      <div class="row"><div class="label">Customer</div><div class="value">{{safeBuyer}}</div></div>
                      <div class="row"><div class="label">Description</div><div class="value">{{safeDescription}}</div></div>
                      <div class="row"><div class="label">Total Amount</div><div class="value">{{(invoice?.Total.ToString("0.00") ?? "-")}}</div></div>
                      <div class="row"><div class="label">Status</div><div class="value">{{Encode(invoice?.Status ?? "not_found")}}</div></div>
                    </div>
                  </div>
                </div>
              </div>
            </body>
            </html>
            """;
    }

    private static InvoiceSellerProfile NormalizeSeller(InvoiceSellerProfile? input)
    {
        var seller = input ?? new InvoiceSellerProfile();
        return new InvoiceSellerProfile
        {
            PlatformName = string.IsNullOrWhiteSpace(seller.PlatformName) ? "Textzy" : seller.PlatformName.Trim(),
            LogoUrl = (seller.LogoUrl ?? string.Empty).Trim(),
            LegalName = string.IsNullOrWhiteSpace(seller.LegalName) ? "TEXTZY DIGITAL SOLUTIONS PRIVATE LIMITED" : seller.LegalName.Trim(),
            Address = string.IsNullOrWhiteSpace(seller.Address) ? "Plot No. 456, Tech Park Building, Bandra Kurla Complex\nMumbai, Maharashtra 400051, India" : seller.Address.Trim(),
            Gstin = string.IsNullOrWhiteSpace(seller.Gstin) ? "27AAFCU5055K1ZO" : seller.Gstin.Trim(),
            Pan = string.IsNullOrWhiteSpace(seller.Pan) ? "AAFCU5055K" : seller.Pan.Trim(),
            Cin = string.IsNullOrWhiteSpace(seller.Cin) ? "U74900MH2020PTC345678" : seller.Cin.Trim(),
            BillingEmail = (seller.BillingEmail ?? string.Empty).Trim(),
            BillingPhone = (seller.BillingPhone ?? string.Empty).Trim(),
            Website = (seller.Website ?? string.Empty).Trim(),
            InvoiceFooter = string.IsNullOrWhiteSpace(seller.InvoiceFooter) ? "This is a computer-generated invoice." : seller.InvoiceFooter.Trim()
        };
    }

    private static InvoiceBuyerProfile NormalizeBuyer(InvoiceBuyerProfile? input)
    {
        var buyer = input ?? new InvoiceBuyerProfile();
        return new InvoiceBuyerProfile
        {
            CompanyName = (buyer.CompanyName ?? string.Empty).Trim(),
            LegalName = (buyer.LegalName ?? string.Empty).Trim(),
            BillingEmail = (buyer.BillingEmail ?? string.Empty).Trim(),
            Address = (buyer.Address ?? string.Empty).Trim(),
            Gstin = (buyer.Gstin ?? string.Empty).Trim(),
            Pan = (buyer.Pan ?? string.Empty).Trim()
        };
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string EncodeMultiline(string value)
        => Encode(value).Replace("\r\n", "<br />").Replace("\n", "<br />");
}
