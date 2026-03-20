using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ICSharpCode.SharpZipLib.Zip;

namespace Textzy.Api.Services.Kyc;

public sealed record AadhaarXmlVerificationResult(
    bool VerificationPassed,
    string FailureReason,
    Dictionary<string, object?> Collected,
    byte[] ReportPdf,
    string ReportFileName,
    string ReportMime,
    string RawXmlUtf8,
    string RawXmlBase64,
    string SourceFileName,
    string SourceZipSha256,
    string SourceXmlSha256,
    string MobileNumber,
    string XmlMobileHash,
    string ExpectedMobileHash,
    bool MobileHashMatched,
    bool SignatureValid,
    bool CertificateLooksLikeUidai,
    string CertificateSubject,
    string CertificateIssuer,
    string CertificateThumbprint,
    string SigningAlgorithm,
    string DigestAlgorithm,
    DateTime ProcessedAtUtc);

public class AadhaarXmlKycService
{
    private sealed record PdfSection(string Title, IReadOnlyList<string> Lines);
    private sealed record PdfPage(IReadOnlyList<PdfSection> Sections, bool ShowPhoto);
    private sealed record PdfImage(byte[] Bytes, int Width, int Height);

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
        collected["verificationMode"] = "aadhaar_xml_upload";
        var xmlMobileHash = GetString(collected, "mobileFromXml");
        var expectedMobileHash = ComputeMobileHash(normalizedMobile, shareCode.Trim());
        var mobileHashMatched = string.Equals(xmlMobileHash, expectedMobileHash, StringComparison.OrdinalIgnoreCase);
        var signature = VerifyXmlSignature(xmlBytes);
        var failureReason = ResolveFailureReason(xmlMobileHash, mobileHashMatched, signature);
        var verificationPassed = string.IsNullOrWhiteSpace(failureReason);

        collected["mobileHashMatched"] = mobileHashMatched;
        collected["expectedMobileHash"] = expectedMobileHash;
        collected["signatureValid"] = signature.Valid;
        collected["certificateSubject"] = signature.CertificateSubject;
        collected["certificateIssuer"] = signature.CertificateIssuer;
        collected["certificateThumbprint"] = signature.CertificateThumbprint;
        collected["signingAlgorithm"] = signature.SigningAlgorithm;
        collected["digestAlgorithm"] = signature.DigestAlgorithm;
        collected["uidaiCertificate"] = signature.LooksLikeUidaiCertificate;
        collected["aadhaarVerified"] = verificationPassed;
        collected["verificationStatus"] = verificationPassed ? "verified" : "failed";
        collected["failureReason"] = failureReason;

        var processedAtUtc = DateTime.UtcNow;
        var trail = new Dictionary<string, object?>
        {
            ["sessionId"] = sessionId,
            ["sourceFileName"] = zipFile.FileName ?? "aadhaar.zip",
            ["sourceZipSha256"] = Sha256Hex(zipBytes),
            ["sourceXmlSha256"] = Sha256Hex(xmlBytes),
            ["mobileNumber"] = normalizedMobile,
            ["processedAtUtc"] = processedAtUtc,
            ["trail"] = "aadhaar_xml_upload",
            ["status"] = verificationPassed ? "verified" : "failed",
            ["failureReason"] = failureReason
        };

        var reportPdf = BuildVerificationPdf(collected, trail, processedAtUtc);

        return new AadhaarXmlVerificationResult(
            VerificationPassed: verificationPassed,
            FailureReason: failureReason,
            Collected: collected,
            ReportPdf: reportPdf,
            ReportFileName: "aadhaar-xml-verification-report.pdf",
            ReportMime: "application/pdf",
            RawXmlUtf8: Encoding.UTF8.GetString(xmlBytes),
            RawXmlBase64: Convert.ToBase64String(xmlBytes),
            SourceFileName: zipFile.FileName ?? "aadhaar.zip",
            SourceZipSha256: Sha256Hex(zipBytes),
            SourceXmlSha256: Sha256Hex(xmlBytes),
            MobileNumber: normalizedMobile,
            XmlMobileHash: xmlMobileHash,
            ExpectedMobileHash: expectedMobileHash,
            MobileHashMatched: mobileHashMatched,
            SignatureValid: signature.Valid,
            CertificateLooksLikeUidai: signature.LooksLikeUidaiCertificate,
            CertificateSubject: signature.CertificateSubject,
            CertificateIssuer: signature.CertificateIssuer,
            CertificateThumbprint: signature.CertificateThumbprint,
            SigningAlgorithm: signature.SigningAlgorithm,
            DigestAlgorithm: signature.DigestAlgorithm,
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
            ["aadhaarNumberFull"] = uid,
            ["aadhaarMasked"] = MaskId(uid),
            ["name"] = Attr("name"),
            ["dob"] = Attr("dob"),
            ["gender"] = Attr("gender"),
            ["fatherName"] = ExtractFatherName(co),
            ["careOfRaw"] = co,
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
            ["aadhaarNumberFull"] = uid,
            ["aadhaarMasked"] = maskedId,
            ["referenceId"] = referenceId,
            ["name"] = PoiAttr("name"),
            ["dob"] = FirstNonEmpty(PoiAttr("dob"), PoiAttr("yob")),
            ["gender"] = PoiAttr("gender"),
            ["fatherName"] = ExtractFatherName(careOf),
            ["careOfRaw"] = careOf,
            ["address"] = NormalizeWhitespace(string.Join(", ", addressParts)),
            ["photoBase64"] = (pht?.Value ?? string.Empty).Trim(),
            ["email"] = FirstNonEmpty(PoiAttr("e"), RootAttr("e")),
            ["mobileFromXml"] = FirstNonEmpty(PoiAttr("m"), RootAttr("m")),
            ["rawAttributes"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["root"] = ExtractAttributes(offlineRoot, "referenceId", "uid", "txn", "ts", "ver", "ret", "m", "e"),
                ["poi"] = ExtractAttributes(poi, "name", "dob", "yob", "gender", "m", "e"),
                ["poa"] = ExtractAttributes(poa, "co", "careof", "house", "street", "lm", "loc", "vtc", "po", "dist", "subdist", "state", "pc", "country")
            }
        };
    }

    private static byte[] BuildVerificationPdf(Dictionary<string, object?> collected, Dictionary<string, object?> trail, DateTime processedAtUtc)
    {
        var verificationPassed = string.Equals(GetString(collected, "verificationStatus"), "verified", StringComparison.OrdinalIgnoreCase);
        var failureReason = GetString(collected, "failureReason");
        var identity = new List<string>
        {
            $"Name: {GetString(collected, "name")}",
            $"Aadhaar No: {Fallback(FirstNonEmpty(GetString(collected, "aadhaarNumberFull"), GetString(collected, "aadhaarNumber")))}",
        };

        var referenceId = GetString(collected, "referenceId");
        if (!string.IsNullOrWhiteSpace(referenceId))
            identity.Add($"Reference ID: {referenceId}");

        identity.AddRange(
        [
            $"DOB: {GetString(collected, "dob")}",
            $"Gender: {GetString(collected, "gender")}",
            $"Father / Guardian: {Fallback(GetString(collected, "fatherName"))}",
            $"Care Of (raw): {Fallback(GetString(collected, "careOfRaw"))}",
            $"Email: {Fallback(GetString(collected, "email"))}",
            $"Mobile Hash (XML): {Fallback(GetString(collected, "mobileFromXml"))}",
            $"Entered Mobile: {GetString(collected, "mobileNumber")}",
            $"Mobile Hash Matched: {Fallback(GetString(collected, "mobileHashMatched"))}",
        ]);

        var sections = new List<PdfSection>
        {
            new("Verification Summary",
            [
                $"Status: {(verificationPassed ? "Verified" : "Failed")}",
                $"Reason: {Fallback(failureReason)}",
                $"Signature Valid: {Fallback(GetString(collected, "signatureValid"))}",
                $"UIDAI Certificate: {Fallback(GetString(collected, "uidaiCertificate"))}",
                $"Processed At UTC: {processedAtUtc:yyyy-MM-dd HH:mm:ss} UTC"
            ]),
            new("Verified Identity", identity),
            new("Address", WrapParagraph(GetString(collected, "address"), 56)),
            new("Signature",
            [
                $"Signature Valid: {Fallback(GetString(collected, "signatureValid"))}",
                $"UIDAI Certificate: {Fallback(GetString(collected, "uidaiCertificate"))}",
                $"Certificate Subject: {Fallback(GetString(collected, "certificateSubject"))}",
                $"Certificate Issuer: {Fallback(GetString(collected, "certificateIssuer"))}",
                $"Certificate Thumbprint: {Fallback(GetString(collected, "certificateThumbprint"))}",
                $"Signing Algorithm: {Fallback(GetString(collected, "signingAlgorithm"))}",
                $"Digest Algorithm: {Fallback(GetString(collected, "digestAlgorithm"))}"
            ]),
            new("Verification Trail",
            [
            $"Session ID: {GetString(trail, "sessionId")}",
            $"Source File: {GetString(trail, "sourceFileName")}",
            $"Processed At UTC: {processedAtUtc:yyyy-MM-dd HH:mm:ss} UTC",
            $"ZIP SHA256: {GetString(trail, "sourceZipSha256")}",
            $"XML SHA256: {GetString(trail, "sourceXmlSha256")}",
            $"Mode: {GetString(trail, "trail")}",
            ]),
            new("Notes",
            [
                "Verified from a password-protected UIDAI Aadhaar XML ZIP uploaded by the user.",
                verificationPassed
                    ? "This hit was charged as a completed Aadhaar XML verification."
                    : "This hit was charged because the uploaded document was processed successfully, but business validation failed.",
                "This PDF includes signature metadata and a timestamped audit trail."
            ])
        };

        var pages = PaginateSections(sections, showPhotoOnFirstPage: CanRenderPhoto(collected));
        return BuildPdfDocument(pages, collected, processedAtUtc);
    }

    private static IReadOnlyList<PdfPage> PaginateSections(IReadOnlyList<PdfSection> sections, bool showPhotoOnFirstPage)
    {
        const int firstPageCapacity = 44;
        const int nextPageCapacity = 52;

        var pages = new List<PdfPage>();
        var current = new List<PdfSection>();
        var used = 0;
        var pageIndex = 0;

        foreach (var section in sections)
        {
            var sectionSize = 2 + Math.Max(1, section.Lines.Count);
            var capacity = pageIndex == 0 && showPhotoOnFirstPage ? firstPageCapacity : nextPageCapacity;
            if (current.Count > 0 && used + sectionSize > capacity)
            {
                pages.Add(new PdfPage(current.ToArray(), pageIndex == 0 && showPhotoOnFirstPage));
                current = [];
                used = 0;
                pageIndex++;
            }

            current.Add(section);
            used += sectionSize;
        }

        if (current.Count > 0)
            pages.Add(new PdfPage(current.ToArray(), pageIndex == 0 && showPhotoOnFirstPage));

        if (pages.Count == 0)
            pages.Add(new PdfPage([new PdfSection("Verified Identity", ["Aadhaar XML report"])], showPhotoOnFirstPage));

        return pages;
    }

    private static byte[] BuildPdfDocument(IReadOnlyList<PdfPage> pages, IReadOnlyDictionary<string, object?> collected, DateTime processedAtUtc)
    {
        var image = TryDecodeJpegPhoto(GetString(collected, "photoBase64"));
        var objects = new Dictionary<int, string>
        {
            [1] = "<< /Type /Catalog /Pages 2 0 R >>",
            [3] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            [4] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>"
        };

        var pageObjectIds = new List<int>();
        var nextObjectId = 5;
        int? imageObjectId = null;

        if (image is not null)
        {
            imageObjectId = nextObjectId++;
            var hexImage = Convert.ToHexString(image.Bytes) + ">";
            objects[imageObjectId.Value] =
                $"<< /Type /XObject /Subtype /Image /Width {image.Width} /Height {image.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter [/ASCIIHexDecode /DCTDecode] /Length {hexImage.Length} >>\nstream\n{hexImage}\nendstream";
        }

        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            var page = pages[pageIndex];
            var pageObjectId = nextObjectId++;
            var contentObjectId = nextObjectId++;
            pageObjectIds.Add(pageObjectId);

            var contentStream = BuildContentStream(page, processedAtUtc, pageIndex + 1, pages.Count, image is not null ? "Im1" : null);
            var xObjectPart = imageObjectId.HasValue && page.ShowPhoto ? $" /XObject << /Im1 {imageObjectId.Value} 0 R >>" : string.Empty;
            objects[pageObjectId] = $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R /F2 4 0 R >>{xObjectPart} >> /Contents {contentObjectId} 0 R >>";
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

    private static string BuildContentStream(PdfPage page, DateTime processedAtUtc, int pageNumber, int totalPages, string? imageName)
    {
        var builder = new StringBuilder();
        var isFailed = page.Sections.Any(section => string.Equals(section.Title, "Verification Summary", StringComparison.OrdinalIgnoreCase)
            && section.Lines.Any(line => line.Contains("Failed", StringComparison.OrdinalIgnoreCase)));
        builder.Append(isFailed ? "0.99 0.96 0.96 rg 20 790 555 34 re f\n" : "0.98 0.98 1 rg 20 790 555 34 re f\n");
        builder.Append(isFailed ? "0.83 0.19 0.19 rg 20 790 555 34 re f\n" : "0.95 0.45 0.05 rg 20 790 555 34 re f\n");
        builder.Append("1 1 1 rg\n");
        AppendText(builder, 32, 810, "/F2", 16, isFailed ? "AADHAAR XML FAILURE REPORT" : "AADHAAR XML VERIFICATION REPORT");
        builder.Append(isFailed ? "0.72 0.15 0.15 rg 430 798 120 18 re f\n" : "0.96 0.62 0.10 rg 430 798 120 18 re f\n");
        builder.Append("1 1 1 rg\n");
        AppendText(builder, 438, 809, "/F2", 9, isFailed ? "FAILED REVIEW" : "VERIFIED DOCUMENT");
        builder.Append("0 0 0 rg\n");
        AppendText(builder, 32, 776, "/F1", 9, "Offline Aadhaar XML verification report");

        var y = 748m;
        if (page.ShowPhoto)
        {
            DrawSection(builder, "Identity Snapshot", page.Sections.FirstOrDefault(s => s.Title == "Verified Identity")?.Lines ?? [], 28, ref y, 360, "#F8FAFC");
            DrawPhotoPanel(builder, GetStringFromSection(page, "Verified Identity", "Name"), imageName, 408, 748, 144, 170);
            y -= 16;
            foreach (var section in page.Sections.Skip(1))
                DrawSection(builder, section.Title, section.Lines, 28, ref y, 524, "#FFFFFF");
        }
        else
        {
            foreach (var section in page.Sections)
                DrawSection(builder, section.Title, section.Lines, 28, ref y, 524, "#FFFFFF");
        }

        builder.Append("0.90 0.90 0.92 rg 20 18 555 20 re f\n");
        builder.Append("0.25 0.25 0.25 rg\n");
        AppendText(builder, 28, 28, "/F1", 8, $"Generated {processedAtUtc:yyyy-MM-dd HH:mm:ss} UTC  •  Page {pageNumber}/{totalPages}");
        AppendText(builder, 360, 28, "/F1", 8, "Trail: timestamped, signed XML verification");
        return builder.ToString();
    }

    private static void DrawSection(StringBuilder builder, string title, IReadOnlyList<string> lines, decimal x, ref decimal y, decimal width, string fillHex)
    {
        var wrapped = lines.SelectMany(line => WrapParagraph(line, 66)).ToList();
        if (wrapped.Count == 0) wrapped.Add("-");
        var height = 28 + wrapped.Count * 13;
        var boxBottom = y - height;

        ApplyFill(builder, fillHex);
        builder.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0} {1} {2} {3} re f\n", x, boxBottom, width, height);
        builder.Append("0.85 0.88 0.92 RG 1 w\n");
        builder.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0} {1} {2} {3} re S\n", x, boxBottom, width, height);
        builder.Append("0.96 0.62 0.10 rg\n");
        builder.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0} {1} {2} 18 re f\n", x, y - 22, width, 18);
        builder.Append("1 1 1 rg\n");
        AppendText(builder, x + 8, y - 10, "/F2", 10, title);
        builder.Append("0.15 0.18 0.25 rg\n");

        var textY = y - 36;
        foreach (var line in wrapped)
        {
            AppendText(builder, x + 10, textY, "/F1", 10, line);
            textY -= 13;
        }

        y = boxBottom - 12;
    }

    private static void DrawPhotoPanel(StringBuilder builder, string name, string? imageName, decimal x, decimal top, decimal width, decimal height)
    {
        var bottom = top - height;
        builder.Append("0.98 0.99 0.99 rg\n");
        builder.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0} {1} {2} {3} re f\n", x, bottom, width, height);
        builder.Append("0.85 0.88 0.92 RG 1 w\n");
        builder.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0} {1} {2} {3} re S\n", x, bottom, width, height);
        builder.Append("0.96 0.62 0.10 rg\n");
        builder.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0} {1} {2} 18 re f\n", x, top - 22, width, 18);
        builder.Append("1 1 1 rg\n");
        AppendText(builder, x + 8, top - 10, "/F2", 10, "Photo Preview");
        builder.Append("0.15 0.18 0.25 rg\n");

        if (!string.IsNullOrWhiteSpace(imageName))
        {
            var imageX = x + 18;
            var imageY = bottom + 24;
            var imageW = width - 36;
            var imageH = height - 60;
            builder.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "q {0} 0 0 {1} {2} {3} cm /{4} Do Q\n", imageW, imageH, imageX, imageY, imageName);
        }
        else
        {
            builder.Append("0.94 0.95 0.97 rg\n");
            builder.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0} {1} {2} {3} re f\n", x + 18, bottom + 24, width - 36, height - 60);
            builder.Append("0.70 0.72 0.76 rg\n");
            AppendText(builder, x + 36, bottom + 88, "/F1", 10, "Photo not available");
        }

        AppendText(builder, x + 12, bottom + 10, "/F2", 9, Truncate(name, 26));
    }

    private static void AppendText(StringBuilder builder, decimal x, decimal y, string font, int fontSize, string text)
    {
        builder.Append("BT\n");
        builder.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0} {1} Tf\n", font, fontSize);
        builder.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0} {1} Td\n", x, y);
        builder.Append('(').Append(EscapePdfString(text)).Append(") Tj\n");
        builder.Append("ET\n");
    }

    private static IReadOnlyList<string> WrapParagraph(string text, int maxLineLength)
    {
        var words = NormalizeWhitespace(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return ["-"];

        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            if (candidate.Length <= maxLineLength)
            {
                current.Clear();
                current.Append(candidate);
                continue;
            }

            if (current.Length > 0) lines.Add(current.ToString());
            current.Clear();
            current.Append(word);
        }

        if (current.Length > 0) lines.Add(current.ToString());
        return lines;
    }

    private static void ApplyFill(StringBuilder builder, string hex)
    {
        var rgb = ParseHexColor(hex);
        builder.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0} {1} {2} rg\n", rgb.r, rgb.g, rgb.b);
    }

    private static (decimal r, decimal g, decimal b) ParseHexColor(string hex)
    {
        var raw = (hex ?? string.Empty).Trim().TrimStart('#');
        if (raw.Length != 6) return (1m, 1m, 1m);
        return (Convert.ToInt32(raw[..2], 16) / 255m, Convert.ToInt32(raw[2..4], 16) / 255m, Convert.ToInt32(raw[4..6], 16) / 255m);
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

    private static string ResolveFailureReason(string xmlMobileHash, bool mobileHashMatched, XmlSignatureDetails signature)
    {
        if (!string.IsNullOrWhiteSpace(xmlMobileHash) && !mobileHashMatched)
            return "Entered mobile number does not match Aadhaar XML mobile hash.";
        if (!signature.Valid)
            return "Aadhaar XML digital signature verification failed.";
        if (!signature.LooksLikeUidaiCertificate)
            return "Aadhaar XML certificate is not a recognized UIDAI signing certificate.";
        return string.Empty;
    }

    private sealed record XmlSignatureDetails(
        bool Valid,
        bool LooksLikeUidaiCertificate,
        string CertificateSubject,
        string CertificateIssuer,
        string CertificateThumbprint,
        string SigningAlgorithm,
        string DigestAlgorithm);

    private static XmlSignatureDetails VerifyXmlSignature(byte[] xmlBytes)
    {
        var xml = Encoding.UTF8.GetString(xmlBytes);
        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml(xml);

        var ns = new XmlNamespaceManager(document.NameTable);
        ns.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);
        var signatureElement = document.SelectSingleNode("//ds:Signature", ns) as XmlElement
            ?? throw new InvalidOperationException("Aadhaar XML signature block is missing.");

        var signedXml = new SignedXml(document);
        signedXml.LoadXml(signatureElement);

        var cert = ExtractCertificate(signedXml)
            ?? throw new InvalidOperationException("Aadhaar XML signing certificate is missing.");

        var valid = signedXml.CheckSignature(cert, true);
        var digestAlgorithm = string.Empty;
        if (signedXml.SignedInfo?.References is not null && signedXml.SignedInfo.References.Count > 0 && signedXml.SignedInfo.References[0] is Reference reference)
            digestAlgorithm = reference.DigestMethod ?? string.Empty;

        var looksLikeUidai = LooksLikeUidaiCertificate(cert);
        return new XmlSignatureDetails(
            Valid: valid,
            LooksLikeUidaiCertificate: looksLikeUidai,
            CertificateSubject: cert.Subject,
            CertificateIssuer: cert.Issuer,
            CertificateThumbprint: cert.Thumbprint ?? string.Empty,
            SigningAlgorithm: signedXml.SignatureMethod ?? string.Empty,
            DigestAlgorithm: digestAlgorithm);
    }

    private static X509Certificate2? ExtractCertificate(SignedXml signedXml)
    {
        if (signedXml.KeyInfo is null) return null;
        foreach (KeyInfoClause clause in signedXml.KeyInfo)
        {
            if (clause is not KeyInfoX509Data data) continue;
            if (data.Certificates is null) continue;
            foreach (var certificate in data.Certificates)
            {
                if (certificate is X509Certificate cert)
                    return new X509Certificate2(cert);
            }
        }

        return null;
    }

    private static bool LooksLikeUidaiCertificate(X509Certificate2 cert)
    {
        var subject = (cert.Subject ?? string.Empty).ToUpperInvariant();
        return subject.Contains("UNIQUE IDENTIFICATION AUTHORITY OF INDIA")
            || subject.Contains("UIDAI")
            || subject.Contains("DS UNIQUE IDENTIFICATION AUTHORITY OF INDIA");
    }

    private static string ComputeMobileHash(string mobileNumber, string shareCode)
    {
        var raw = $"{mobileNumber}{shareCode}";
        var bytes = Encoding.UTF8.GetBytes(raw);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
    }

    private static Dictionary<string, object?> ExtractAttributes(XElement? element, params string[] names)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (element is null) return result;
        foreach (var name in names)
        {
            var value = (element.Attribute(name)?.Value ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(value))
                result[name] = value;
        }
        return result;
    }

    private static bool CanRenderPhoto(IReadOnlyDictionary<string, object?> collected)
        => TryDecodeJpegPhoto(GetString(collected, "photoBase64")) is not null;

    private static PdfImage? TryDecodeJpegPhoto(string base64)
    {
        try
        {
            var raw = (base64 ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var bytes = Convert.FromBase64String(raw);
            if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8) return null;

            for (var i = 2; i < bytes.Length - 9; i++)
            {
                if (bytes[i] != 0xFF) continue;
                var marker = bytes[i + 1];
                if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC9 or 0xCA or 0xCB)
                {
                    var height = (bytes[i + 5] << 8) + bytes[i + 6];
                    var width = (bytes[i + 7] << 8) + bytes[i + 8];
                    if (width > 0 && height > 0) return new PdfImage(bytes, width, height);
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string GetStringFromSection(PdfPage page, string title, string labelPrefix)
    {
        var section = page.Sections.FirstOrDefault(x => string.Equals(x.Title, title, StringComparison.OrdinalIgnoreCase));
        if (section is null) return string.Empty;
        var line = section.Lines.FirstOrDefault(x => x.StartsWith($"{labelPrefix}:", StringComparison.OrdinalIgnoreCase));
        return line is null ? string.Empty : line[(labelPrefix.Length + 1)..].Trim();
    }

    private static string Fallback(string value) => string.IsNullOrWhiteSpace(value) ? "-" : value;

    private static string Truncate(string value, int max)
    {
        var text = NormalizeWhitespace(value);
        if (text.Length <= max) return text;
        return text[..Math.Max(0, max - 3)] + "...";
    }
}
