using System.Text;
using Textzy.Api.Models;

namespace Textzy.Api.Services;

public static class InvoicePdfRenderer
{
    private const int MaxLineWidth = 88;
    private const int LinesPerPage = 52;

    public static byte[] BuildInvoicePdf(
        BillingInvoice invoice,
        InvoiceSellerProfile seller,
        InvoiceBuyerProfile buyer,
        decimal taxRatePercent,
        bool isTaxExempt,
        bool isReverseCharge,
        string verificationUrl)
    {
        var pages = Paginate(BuildLines(invoice, seller, buyer, taxRatePercent, isTaxExempt, isReverseCharge, verificationUrl));
        return BuildPdfDocument(pages);
    }

    private static IReadOnlyList<string> BuildLines(
        BillingInvoice invoice,
        InvoiceSellerProfile seller,
        InvoiceBuyerProfile buyer,
        decimal taxRatePercent,
        bool isTaxExempt,
        bool isReverseCharge,
        string verificationUrl)
    {
        var invoiceLabel = string.Equals(invoice.InvoiceKind, "proforma_invoice", StringComparison.OrdinalIgnoreCase)
            ? "PROFORMA INVOICE"
            : "TAX INVOICE";
        var taxLabel = isReverseCharge
            ? "GST payable under reverse charge"
            : isTaxExempt
                ? "GST exempt supply"
                : $"GST @ {Math.Clamp(taxRatePercent, 0m, 100m):0.##}%";

        var lines = new List<string>
        {
            $"{NormalizeText(seller.PlatformName)} {invoiceLabel}",
            string.Empty
        };

        lines.AddRange(WrapField("Invoice No", invoice.InvoiceNo));
        lines.AddRange(WrapField("Invoice Date", invoice.IssuedAtUtc.ToString("yyyy-MM-dd")));
        lines.AddRange(WrapField("Status", (invoice.Status ?? string.Empty).ToUpperInvariant()));
        lines.AddRange(WrapField("Reference", Fallback(invoice.ReferenceNo)));
        lines.AddRange(WrapField("Paid Date", invoice.PaidAtUtc?.ToString("yyyy-MM-dd") ?? "-"));
        lines.AddRange(WrapField("Billing Cycle", Fallback((invoice.BillingCycle ?? string.Empty).Replace("_", " "))));
        lines.Add(string.Empty);

        lines.Add("Supplier");
        lines.AddRange(WrapBlock(seller.LegalName));
        lines.AddRange(WrapMultiline("Address", seller.Address));
        lines.AddRange(WrapField("GSTIN", Fallback(seller.Gstin)));
        lines.AddRange(WrapField("PAN", Fallback(seller.Pan)));
        lines.AddRange(WrapField("CIN", Fallback(seller.Cin)));
        lines.AddRange(WrapField("Billing Email", Fallback(seller.BillingEmail)));
        lines.AddRange(WrapField("Billing Phone", Fallback(seller.BillingPhone)));
        lines.AddRange(WrapField("Website", Fallback(seller.Website)));
        lines.Add(string.Empty);

        lines.Add("Bill To");
        lines.AddRange(WrapBlock(string.IsNullOrWhiteSpace(buyer.CompanyName) ? "Customer" : buyer.CompanyName));
        lines.AddRange(WrapField("Legal Name", Fallback(string.IsNullOrWhiteSpace(buyer.LegalName) ? buyer.CompanyName : buyer.LegalName)));
        lines.AddRange(WrapMultiline("Address", buyer.Address));
        lines.AddRange(WrapField("Billing Email", Fallback(buyer.BillingEmail)));
        lines.AddRange(WrapField("GSTIN", Fallback(buyer.Gstin)));
        lines.AddRange(WrapField("PAN", Fallback(buyer.Pan)));
        lines.Add(string.Empty);

        lines.Add("Invoice Details");
        lines.AddRange(WrapField("Description", Fallback(invoice.Description, "Platform service purchase")));
        lines.AddRange(WrapField("Service Period", $"{invoice.PeriodStartUtc:yyyy-MM-dd} to {invoice.PeriodEndUtc:yyyy-MM-dd}"));
        lines.AddRange(WrapField("Taxable Value", $"{invoice.Subtotal:0.00}"));
        lines.AddRange(WrapField("Tax", $"{invoice.TaxAmount:0.00} ({taxLabel})"));
        lines.AddRange(WrapField("Total", $"{invoice.Total:0.00}"));
        lines.Add(string.Empty);

        lines.Add("Verification");
        lines.AddRange(WrapField("Integrity", Fallback(invoice.IntegrityHash)));
        lines.AddRange(WrapField("Verify URL", Fallback(verificationUrl)));
        lines.Add(string.Empty);
        lines.AddRange(WrapBlock(Fallback(seller.InvoiceFooter, "This is a computer-generated invoice.")));

        return lines;
    }

    private static IReadOnlyList<IReadOnlyList<string>> Paginate(IReadOnlyList<string> lines)
    {
        var pages = new List<IReadOnlyList<string>>();
        for (var i = 0; i < lines.Count; i += LinesPerPage)
        {
            pages.Add(lines.Skip(i).Take(LinesPerPage).ToArray());
        }

        if (pages.Count == 0)
            pages.Add(["Invoice"]);

        return pages;
    }

    private static byte[] BuildPdfDocument(IReadOnlyList<IReadOnlyList<string>> pages)
    {
        var objects = new Dictionary<int, string>
        {
            [1] = "<< /Type /Catalog /Pages 2 0 R >>",
            [3] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };

        var pageObjectIds = new List<int>();
        var nextObjectId = 4;

        foreach (var pageLines in pages)
        {
            var pageObjectId = nextObjectId++;
            var contentObjectId = nextObjectId++;
            pageObjectIds.Add(pageObjectId);

            var contentStream = BuildContentStream(pageLines);
            objects[pageObjectId] = $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {contentObjectId} 0 R >>";
            objects[contentObjectId] = $"<< /Length {Encoding.ASCII.GetByteCount(contentStream)} >>\nstream\n{contentStream}\nendstream";
        }

        objects[2] = $"<< /Type /Pages /Count {pageObjectIds.Count} /Kids [{string.Join(" ", pageObjectIds.Select(id => $"{id} 0 R"))}] >>";

        using var stream = new MemoryStream();
        WriteAscii(stream, "%PDF-1.4\n%TZPDF\n");

        var maxObjectId = objects.Keys.Max();
        var offsets = new long[maxObjectId + 1];
        for (var objectId = 1; objectId <= maxObjectId; objectId++)
        {
            offsets[objectId] = stream.Position;
            WriteAscii(stream, $"{objectId} 0 obj\n{objects[objectId]}\nendobj\n");
        }

        var xrefOffset = stream.Position;
        WriteAscii(stream, $"xref\n0 {maxObjectId + 1}\n");
        WriteAscii(stream, "0000000000 65535 f \n");
        for (var objectId = 1; objectId <= maxObjectId; objectId++)
        {
            WriteAscii(stream, $"{offsets[objectId]:0000000000} 00000 n \n");
        }

        WriteAscii(stream, $"trailer\n<< /Size {maxObjectId + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF");
        return stream.ToArray();
    }

    private static string BuildContentStream(IReadOnlyList<string> lines)
    {
        var builder = new StringBuilder();
        builder.Append("BT\n/F1 11 Tf\n48 800 Td\n");

        for (var i = 0; i < lines.Count; i++)
        {
            builder.Append('(').Append(EscapePdfString(lines[i])).Append(") Tj\n");
            if (i < lines.Count - 1)
                builder.Append("0 -14 Td\n");
        }

        builder.Append("ET");
        return builder.ToString();
    }

    private static IEnumerable<string> WrapField(string label, string value)
    {
        var prefix = $"{NormalizeText(label)}: ";
        return WrapText(prefix + NormalizeText(value), prefix.Length);
    }

    private static IEnumerable<string> WrapMultiline(string label, string value)
    {
        var normalized = NormalizeText(value);
        if (string.IsNullOrWhiteSpace(normalized))
            return WrapField(label, "-");

        var parts = normalized.Split('\n', StringSplitOptions.None);
        var output = new List<string>();
        for (var i = 0; i < parts.Length; i++)
        {
            output.AddRange(i == 0 ? WrapField(label, parts[i]) : WrapText(parts[i], 0));
        }

        return output;
    }

    private static IEnumerable<string> WrapBlock(string value) => WrapText(NormalizeText(value), 0);

    private static IEnumerable<string> WrapText(string text, int hangingIndent)
    {
        var normalized = NormalizeText(text);
        if (string.IsNullOrWhiteSpace(normalized))
            return ["-"];

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return ["-"];

        var lines = new List<string>();
        var current = new StringBuilder();
        var indent = new string(' ', Math.Max(0, hangingIndent));

        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            if (candidate.Length <= MaxLineWidth)
            {
                current.Clear();
                current.Append(candidate);
                continue;
            }

            if (current.Length > 0)
                lines.Add(current.ToString());

            current.Clear();
            current.Append(indent);
            if (indent.Length > 0)
                current.Append(word);
            else
                current.Append(word);

            if (current.Length <= MaxLineWidth)
                continue;

            lines.AddRange(SplitLongWord(current.ToString(), indent.Length));
            current.Clear();
        }

        if (current.Length > 0)
            lines.Add(current.ToString());

        return lines;
    }

    private static IEnumerable<string> SplitLongWord(string value, int hangingIndent)
    {
        var lines = new List<string>();
        var indent = new string(' ', Math.Max(0, hangingIndent));
        var available = Math.Max(8, MaxLineWidth - indent.Length);
        var remainder = value.TrimStart();

        while (remainder.Length > available)
        {
            lines.Add(indent + remainder[..available]);
            remainder = remainder[available..];
        }

        if (!string.IsNullOrWhiteSpace(remainder))
            lines.Add(indent + remainder);

        return lines;
    }

    private static string NormalizeText(string? value, string fallback = "-")
    {
        var raw = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var builder = new StringBuilder(raw.Length);
        foreach (var ch in raw.Replace("\r", string.Empty))
        {
            if (ch == '\n')
            {
                builder.Append('\n');
                continue;
            }

            builder.Append(ch is >= ' ' and <= '~' ? ch : '?');
        }

        return builder.ToString();
    }

    private static string Fallback(string? value, string fallback = "-")
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string EscapePdfString(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }
}
