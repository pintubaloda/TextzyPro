using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using ICSharpCode.SharpZipLib.Zip;

namespace Textzy.Api.Services.Kyc;

public sealed record AadhaarXmlVerificationResult(
    Dictionary<string, object?> Collected,
    byte[] ReportPdf,
    string ReportFileName,
    string ReportMime,
    string SourceFileName,
    string SourceZipSha256,
    string SourceXmlSha256,
    string MobileNumber,
    DateTime ProcessedAtUtc);

public class AadhaarXmlKycService
{
    public async Task<AadhaarXmlVerificationResult> VerifyAsync(IFormFile zipFile, string shareCode, string mobileNumber, Guid sessionId, CancellationToken ct)
    {
        if (zipFile is null || zipFile.Length <= 0)
            throw new InvalidOperationException("Aadhaar ZIP file is required.");

        if (string.IsNullOrWhiteSpace(shareCode))
            throw new InvalidOperationException("Share code is required.");

        var normalizedMobile = NormalizeMobile(mobileNumber);
        if (string.IsNullOrWhiteSpace(normalizedMobile))
            throw new InvalidOperationException("Valid mobile number is required.");

        await using var sourceStream = zipFile.OpenReadStream();
        using var zipBuffer = new MemoryStream();
        await sourceStream.CopyToAsync(zipBuffer, ct);
        var zipBytes = zipBuffer.ToArray();
        if (zipBytes.Length == 0)
            throw new InvalidOperationException("Uploaded ZIP file is empty.");

        var xmlBytes = ExtractXmlBytes(zipBytes, shareCode.Trim());
        var collected = ParseAadhaarXml(xmlBytes);
        collected["mobileNumber"] = normalizedMobile;
        collected["aadhaarVerified"] = true;
        collected["verificationMode"] = "aadhaar_xml_upload";

        var processedAtUtc = DateTime.UtcNow;
        var trail = new Dictionary<string, object?>
        {
            ["sessionId"] = sessionId,
            ["sourceFileName"] = zipFile.FileName ?? "aadhaar.zip",
            ["sourceZipSha256"] = Sha256Hex(zipBytes),
            ["sourceXmlSha256"] = Sha256Hex(xmlBytes),
            ["mobileNumber"] = normalizedMobile,
            ["processedAtUtc"] = processedAtUtc,
            ["trail"] = "aadhaar_xml_upload"
        };

        var reportPdf = BuildVerificationPdf(collected, trail);

        return new AadhaarXmlVerificationResult(
            Collected: collected,
            ReportPdf: reportPdf,
            ReportFileName: "textzy-aadhaar-verification-report.pdf",
            ReportMime: "application/pdf",
            SourceFileName: zipFile.FileName ?? "aadhaar.zip",
            SourceZipSha256: Sha256Hex(zipBytes),
            SourceXmlSha256: Sha256Hex(xmlBytes),
            MobileNumber: normalizedMobile,
            ProcessedAtUtc: processedAtUtc);
    }

    private static byte[] ExtractXmlBytes(byte[] zipBytes, string shareCode)
    {
        using var input = new MemoryStream(zipBytes);
        using var zip = new ZipInputStream(input) { Password = shareCode };
        ZipEntry? entry;
        while ((entry = zip.GetNextEntry()) is not null)
        {
            if (!entry.IsFile) continue;
            if (!entry.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
            using var outMs = new MemoryStream();
            zip.CopyTo(outMs);
            var bytes = outMs.ToArray();
            if (bytes.Length > 0) return bytes;
        }

        throw new InvalidOperationException("Unable to read Aadhaar XML from ZIP. Check share code and file contents.");
    }

    private static Dictionary<string, object?> ParseAadhaarXml(byte[] xmlBytes)
    {
        try
        {
            var xml = Encoding.UTF8.GetString(xmlBytes);
            var doc = XDocument.Parse(xml);
            var plbd = doc.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals("PrintLetterBarcodeData", StringComparison.OrdinalIgnoreCase));
            if (plbd is not null)
                return ParsePrintLetterBarcodeData(doc, plbd);

            var offlineRoot = doc.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals("OfflinePaperlessKyc", StringComparison.OrdinalIgnoreCase))
                ?? (doc.Root?.Name.LocalName.Equals("OfflinePaperlessKyc", StringComparison.OrdinalIgnoreCase) == true ? doc.Root : null);
            if (offlineRoot is not null)
                return ParseOfflinePaperlessKyc(doc, offlineRoot);

            throw new InvalidOperationException("Uploaded XML is not a valid Aadhaar XML file.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to parse Aadhaar XML.", ex);
        }
    }

    private static Dictionary<string, object?> ParsePrintLetterBarcodeData(XDocument doc, XElement plbd)
    {
        string Attr(string key) => (plbd.Attribute(key)?.Value ?? string.Empty).Trim();

        var uid = Attr("uid");
        var co = Attr("co");
        var addressParts = new List<string>();
        void Add(string v) { if (!string.IsNullOrWhiteSpace(v)) addressParts.Add(v); }
        Add(co);
        Add(Attr("house"));
        Add(Attr("street"));
        Add(Attr("lm"));
        Add(Attr("loc"));
        Add(Attr("vtc"));
        Add(Attr("po"));
        Add(Attr("dist"));
        Add(Attr("subdist"));
        Add(Attr("state"));
        Add(Attr("pc"));

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["aadhaarNumber"] = uid,
            ["aadhaarMasked"] = MaskId(uid),
            ["name"] = Attr("name"),
            ["dob"] = Attr("dob"),
            ["gender"] = Attr("gender"),
            ["fatherName"] = ExtractFatherName(co),
            ["address"] = NormalizeWhitespace(string.Join(", ", addressParts)),
            ["photoBase64"] = (doc.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals("Pht", StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty).Trim()
        };
    }

    private static Dictionary<string, object?> ParseOfflinePaperlessKyc(XDocument doc, XElement offlineRoot)
    {
        var uidData = offlineRoot.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals("UidData", StringComparison.OrdinalIgnoreCase));
        var poi = uidData?.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals("Poi", StringComparison.OrdinalIgnoreCase))
            ?? offlineRoot.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals("Poi", StringComparison.OrdinalIgnoreCase));
        var poa = uidData?.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals("Poa", StringComparison.OrdinalIgnoreCase))
            ?? offlineRoot.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals("Poa", StringComparison.OrdinalIgnoreCase));
        var pht = uidData?.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals("Pht", StringComparison.OrdinalIgnoreCase))
            ?? offlineRoot.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals("Pht", StringComparison.OrdinalIgnoreCase));

        if (poi is null && poa is null)
            throw new InvalidOperationException("Uploaded XML does not contain Aadhaar identity data.");

        string RootAttr(string key) => (offlineRoot.Attribute(key)?.Value ?? string.Empty).Trim();
        string PoiAttr(string key) => (poi?.Attribute(key)?.Value ?? string.Empty).Trim();
        string PoaAttr(string key) => (poa?.Attribute(key)?.Value ?? string.Empty).Trim();

        var referenceId = RootAttr("referenceId");
        var uid = RootAttr("uid");
        if (string.IsNullOrWhiteSpace(uid))
            uid = PoiAttr("uid");

        var careOf = FirstNonEmpty(PoaAttr("co"), PoaAttr("careof"), PoiAttr("co"));
        var addressParts = new List<string>();
        void Add(string value) { if (!string.IsNullOrWhiteSpace(value)) addressParts.Add(value); }
        Add(careOf);
        Add(PoaAttr("house"));
        Add(PoaAttr("street"));
        Add(PoaAttr("lm"));
        Add(PoaAttr("loc"));
        Add(PoaAttr("vtc"));
        Add(PoaAttr("po"));
        Add(PoaAttr("dist"));
        Add(PoaAttr("subdist"));
        Add(PoaAttr("state"));
        Add(PoaAttr("pc"));

        var maskedId = !string.IsNullOrWhiteSpace(uid) ? MaskId(uid) : MaskReferenceId(referenceId);
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["aadhaarNumber"] = uid,
            ["aadhaarMasked"] = maskedId,
            ["referenceId"] = referenceId,
            ["name"] = PoiAttr("name"),
            ["dob"] = FirstNonEmpty(PoiAttr("dob"), PoiAttr("yob")),
            ["gender"] = PoiAttr("gender"),
            ["fatherName"] = ExtractFatherName(careOf),
            ["address"] = NormalizeWhitespace(string.Join(", ", addressParts)),
            ["photoBase64"] = (pht?.Value ?? string.Empty).Trim(),
            ["email"] = PoiAttr("e")
        };
    }

    private static byte[] BuildVerificationPdf(Dictionary<string, object?> collected, Dictionary<string, object?> trail)
    {
        var lines = new List<string>
        {
            "TEXTZY AADHAAR XML VERIFICATION REPORT",
            string.Empty,
            "Verified Identity",
            $"Name: {GetString(collected, "name")}",
            $"Aadhaar (masked): {GetString(collected, "aadhaarMasked")}",
            $"DOB: {GetString(collected, "dob")}",
            $"Gender: {GetString(collected, "gender")}",
            $"Father / Guardian: {GetString(collected, "fatherName")}",
            $"Mobile Number: {GetString(collected, "mobileNumber")}",
            string.Empty,
            "Address",
            GetString(collected, "address"),
            string.Empty,
            "Verification Trail",
            $"Session ID: {GetString(trail, "sessionId")}",
            $"Source File: {GetString(trail, "sourceFileName")}",
            $"Processed At UTC: {GetString(trail, "processedAtUtc")}",
            $"ZIP SHA256: {GetString(trail, "sourceZipSha256")}",
            $"XML SHA256: {GetString(trail, "sourceXmlSha256")}",
            $"Mode: {GetString(trail, "trail")}",
            string.Empty,
            "Notes",
            "This PDF was generated by Textzy from a password-protected Aadhaar XML ZIP uploaded by the user."
        };

        return BuildPdfDocument(Paginate(lines));
    }

    private static IReadOnlyList<IReadOnlyList<string>> Paginate(IReadOnlyList<string> lines)
    {
        const int linesPerPage = 52;
        var pages = new List<IReadOnlyList<string>>();
        for (var i = 0; i < lines.Count; i += linesPerPage)
            pages.Add(lines.Skip(i).Take(linesPerPage).ToArray());
        return pages.Count == 0 ? [new[] { "Textzy Aadhaar XML Verification Report" }] : pages;
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
            WriteAscii(stream, $"{offsets[objectId]:0000000000} 00000 n \n");

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

    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string EscapePdfString(string value)
        => (value ?? string.Empty).Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    private static string NormalizeWhitespace(string value)
        => string.Join(" ", (value ?? string.Empty).Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

    private static string MaskId(string id)
    {
        var value = (id ?? string.Empty).Trim();
        if (value.Length <= 4) return value;
        return new string('X', Math.Max(0, value.Length - 4)) + value[^4..];
    }

    private static string ExtractFatherName(string co)
    {
        if (string.IsNullOrWhiteSpace(co)) return string.Empty;
        var idx = co.IndexOf(':');
        return idx >= 0 && idx + 1 < co.Length ? co[(idx + 1)..].Trim() : co.Trim();
    }

    private static string MaskReferenceId(string referenceId)
    {
        var value = NormalizeWhitespace(referenceId);
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (value.Length <= 8) return value;
        return $"{value[..4]}...{value[^4..]}";
    }

    private static string NormalizeMobile(string raw)
    {
        var digits = new string((raw ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 10) return digits;
        if (digits.Length == 12 && digits.StartsWith("91", StringComparison.Ordinal)) return digits[2..];
        return string.Empty;
    }

    private static string Sha256Hex(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
    }

    private static string GetString(IReadOnlyDictionary<string, object?> values, string key)
        => values.TryGetValue(key, out var value) ? (value?.ToString() ?? string.Empty) : string.Empty;

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;
}
