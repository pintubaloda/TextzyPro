using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.IO.Compression;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using System.Reflection;
using System.Runtime.Loader;

namespace Textzy.Api.Services.Kyc;

public interface IKycProvider
{
    string Code { get; }
    Task<(string RedirectUrl, string State)> BuildRedirectAsync(KycSession session, CancellationToken ct);
    Task<KycProviderCallbackResult> HandleCallbackAsync(KycSession session, string code, string state, CancellationToken ct);
}

public sealed record KycProviderCallbackResult(
    bool Ok,
    string Status,
    string FailureReason,
    string ResultJson,
    IReadOnlyList<string> DocumentTypes);

public class DigiLockerKycProvider(
    IHttpClientFactory httpClientFactory,
    ControlDbContext db,
    SecretCryptoService crypto,
    IConfiguration config,
    ILogger<DigiLockerKycProvider> logger) : IKycProvider
{
    public string Code => "digilocker";

    private const string SettingsScope = "digilocker";
    private const int DefaultMaxFileBytes = 2 * 1024 * 1024; // 2 MiB, keep result/webhook sizes bounded
    private const int DefaultMaxFilesPerSession = 5;

    public async Task<(string RedirectUrl, string State)> BuildRedirectAsync(KycSession session, CancellationToken ct)
    {
        var settings = await LoadSettingsAsync(ct);
        var authorizeUrl = Require(settings.AuthorizeUrl, "authorizeUrl");
        var clientId = Require(settings.ClientId, "clientId");
        var redirectUri = Require(settings.RedirectUri, "redirectUri");
        var scope = NormalizeScope(settings.Scope);

        var state = CreateState(session);
        var (verifier, challenge) = CreatePkcePair();

        session.StateEncrypted = crypto.Encrypt(state);
        session.CodeVerifierEncrypted = crypto.Encrypt(verifier);
        session.Status = "created";
        session.UpdatedAtUtc = DateTime.UtcNow;
        db.KycSessions.Update(session);
        await db.SaveChangesAsync(ct);

        var requestedDocTypes = ParseStringList(session.RequestedDocTypesJson);
        var acrFromDocType = TryResolveAcrFromDocTypes(requestedDocTypes);
        var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (requestedDocTypes.Count > 0 && !string.IsNullOrWhiteSpace(settings.DocTypeParamName))
        {
            var param = settings.DocTypeParamName.Trim();
            var values = requestedDocTypes;

            // DigiLocker commonly expects issuer doctype codes in req_doctype, not user-friendly names.
            // Example: PAN -> PANCR, Aadhaar -> ADHAR, Driving License -> DRVLC.
            if (param.Equals("req_doctype", StringComparison.OrdinalIgnoreCase))
            {
                values = requestedDocTypes.Select(MapReqDoctype).ToList();
            }

            extra[param] = string.Join(",", values);
        }

        // Build OAuth2 authorization URL (PKCE).
        var qs = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = scope,
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        };

        // DigiLocker supports extra query params for specific flows (e.g. Aadhaar).
        // Allow platform to configure these, but do not allow overriding core OAuth keys.
        foreach (var kv in ParseAuthorizeExtraParams(settings.AuthorizeExtraParams))
        {
            if (IsReservedAuthorizeKey(kv.Key)) continue;
            var v = ExpandAuthorizePlaceholders(kv.Value, acrFromDocType);
            if (!qs.ContainsKey(kv.Key) && !string.IsNullOrWhiteSpace(v))
                qs[kv.Key] = v;
        }

        foreach (var kv in extra)
        {
            if (IsReservedAuthorizeKey(kv.Key)) continue;
            if (!qs.ContainsKey(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                qs[kv.Key] = kv.Value;
        }

        var redirect = AppendQuery(authorizeUrl, qs);
        return (redirect, state);
    }

    private static string NormalizeScope(string? rawScope)
    {
        // DigiLocker scope names in the wild are inconsistent across docs/samples.
        // Our UI allows "friendly" names; normalize to DigiLocker's actual scope tokens.
        //
        // Common DigiLocker OAuth2 scopes:
        // - files.issueddocs (issued documents)
        // - userdetails (profile info like name/dob/gender)
        // - email, address, picture
        // - avs (age verification)
        //
        // IMPORTANT: Do not include `openid` (causes token exchange issues on some DigiLocker client configs).
        var input = (rawScope ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(input))
            return "files.issueddocs";

        var tokens = new List<string>();
        foreach (var part in input.Split(new[] { ' ', '\t', '\r', '\n', ',', '+', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = part.Trim();
            if (t.Length == 0) continue;
            var key = t.Replace('_', '-').Trim().ToLowerInvariant();

            if (key == "openid") continue;

            var mapped = key switch
            {
                "issued-documents" => "files.issueddocs",
                "issued-docs" => "files.issueddocs",
                "issued-doc" => "files.issueddocs",
                "issueddocuments" => "files.issueddocs",
                "issued-document" => "files.issueddocs",
                "files.issueddocs" => "files.issueddocs",
                "files-issued-docs" => "files.issueddocs",
                "profile" => "userdetails",
                "user-details" => "userdetails",
                "userdetails" => "userdetails",
                "age-verification" => "avs",
                "ageverification" => "avs",
                "avs" => "avs",
                "email" => "email",
                "address" => "address",
                "picture" => "picture",
                _ => t.Trim()
            };

            mapped = mapped.Trim();
            if (mapped.Length == 0) continue;
            tokens.Add(mapped);
        }

        // Stable distinct while preserving order.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var t in tokens)
        {
            if (seen.Add(t)) result.Add(t);
        }

        if (result.Count == 0) return "files.issueddocs";
        return string.Join(' ', result);
    }

    public async Task<KycProviderCallbackResult> HandleCallbackAsync(KycSession session, string code, string state, CancellationToken ct)
    {
        var settings = await LoadSettingsAsync(ct);
        var expectedState = crypto.Decrypt(session.StateEncrypted);
        if (!FixedTimeEquals(state, expectedState))
        {
            return new KycProviderCallbackResult(false, "failed", "Invalid state.", "{}", Array.Empty<string>());
        }

        var tokenEndpoint = Require(settings.TokenUrl, "tokenUrl");
        var clientId = Require(settings.ClientId, "clientId");
        var clientSecret = Require(settings.ClientSecret, "clientSecret");
        var redirectUri = Require(settings.RedirectUri, "redirectUri");
        var verifier = crypto.Decrypt(session.CodeVerifierEncrypted);

        var http = httpClientFactory.CreateClient();
        using var tokenReq = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint);
        tokenReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = verifier
        });

        using var tokenRes = await http.SendAsync(tokenReq, ct);
        var tokenBody = await tokenRes.Content.ReadAsStringAsync(ct);
        if (!tokenRes.IsSuccessStatusCode)
        {
            logger.LogWarning("DigiLocker token exchange failed. status={Status} body={Body}", (int)tokenRes.StatusCode, tokenBody);
            return new KycProviderCallbackResult(false, "failed", "Token exchange failed.", JsonSerializer.Serialize(new { token = tokenBody }), Array.Empty<string>());
        }

        using var tokenJson = JsonDocument.Parse(string.IsNullOrWhiteSpace(tokenBody) ? "{}" : tokenBody);
        var accessToken = tokenJson.RootElement.TryGetProperty("access_token", out var at) ? at.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(accessToken))
            return new KycProviderCallbackResult(false, "failed", "Missing access_token from provider.", tokenBody, Array.Empty<string>());

        var requestedDocTypes = ParseStringList(session.RequestedDocTypesJson);
        var redactedToken = RedactTokenPayload(tokenJson.RootElement);

        var apiBase = Require(settings.ApiBaseUrl, "apiBaseUrl").TrimEnd('/');
        var userDetailsStatus = 0;
        var userDetailsBody = string.Empty;
        if (settings.IncludeUserDetailsInResult)
        {
            try
            {
                var userPath = string.IsNullOrWhiteSpace(settings.UserDetailsPath) ? "/oauth2/1/user" : settings.UserDetailsPath.Trim();
                var userUrl = CombineApiUrl(apiBase, userPath);
                using var userReq = new HttpRequestMessage(HttpMethod.Get, userUrl);
                userReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                using var userRes = await http.SendAsync(userReq, HttpCompletionOption.ResponseContentRead, ct);
                userDetailsStatus = (int)userRes.StatusCode;
                userDetailsBody = await userRes.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex)
            {
                userDetailsStatus = 0;
                userDetailsBody = JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        // Issued docs endpoint differs by DigiLocker environment/version.
        // Try configured path first, then fall back to known endpoints.
        var issuedFetch = await TryGetIssuedDocsAsync(
            http,
            apiBase,
            string.IsNullOrWhiteSpace(settings.IssuedDocsPath) ? "/oauth2/1/files" : settings.IssuedDocsPath.Trim(),
            accessToken,
            ct);
        var issuedDocsUrl = issuedFetch.Url;
        var issuedDocsStatus = issuedFetch.StatusCode;
        var issuedBody = issuedFetch.Body;
        var issuedOk = issuedDocsStatus >= 200 && issuedDocsStatus <= 299;

        var docTypes = new List<string>();
        var issuedItems = new List<IssuedItem>();
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(issuedBody) ? "{}" : issuedBody);
            // Common patterns: { items: [{ doctype: "PAN" }, ...] } OR array at root.
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in doc.RootElement.EnumerateArray())
                {
                    if (it.ValueKind != JsonValueKind.Object) continue;
                    if (it.TryGetProperty("doctype", out var dt) && !string.IsNullOrWhiteSpace(dt.GetString()))
                        docTypes.Add(dt.GetString()!.Trim());
                    issuedItems.Add(ParseIssuedItem(it));
                }
            }
            else if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in items.EnumerateArray())
                {
                    if (it.ValueKind != JsonValueKind.Object) continue;
                    if (it.TryGetProperty("doctype", out var dt) && !string.IsNullOrWhiteSpace(dt.GetString()))
                        docTypes.Add(dt.GetString()!.Trim());
                    else if (it.TryGetProperty("doc_type", out var dt2) && !string.IsNullOrWhiteSpace(dt2.GetString()))
                        docTypes.Add(dt2.GetString()!.Trim());
                    issuedItems.Add(ParseIssuedItem(it));
                }
            }
        }
        catch
        {
            // ignore parsing errors, we still store raw payload
        }

        // Optionally download actual file bytes so tenants can get a "complete record with file"
        // via session GET or webhook payload. Keep strict limits to avoid blowing up DB/webhook sizes.
        var downloaded = new List<object>();
        var downloadErrors = new List<object>();
        var parsedDocs = new List<object>();
        var collected = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        // Always collect basic profile fields from /user even when document download is blocked.
        if (settings.IncludeUserDetailsInResult && userDetailsStatus >= 200 && userDetailsStatus <= 299)
        {
            try
            {
                if (TryCollectFromUserDetails(collected, userDetailsBody, out var parsedUser))
                    parsedDocs.Add(new { source = "userDetails", parsed = parsedUser });
            }
            catch
            {
                // ignore best-effort extraction errors
            }
        }

        // Collect what we can from issued-doc metadata even if file download is blocked.
        // Many DigiLocker environments return PAN/DL identifiers inside the URI itself.
        try
        {
            var meta = TryCollectFromIssuedItems(collected, issuedItems);
            if (meta is not null) parsedDocs.Add(new { source = "issuedDocsMeta", parsed = meta });
        }
        catch
        {
            // ignore best-effort extraction errors
        }
        if (issuedOk && settings.IncludeFilesInResult && issuedItems.Count > 0)
        {
            var maxFiles = settings.MaxFilesPerSession > 0 ? settings.MaxFilesPerSession : DefaultMaxFilesPerSession;
            var maxBytes = settings.MaxFileBytes > 0 ? settings.MaxFileBytes : DefaultMaxFileBytes;

            // De-dupe by URI; DigiLocker may return duplicates in some environments.
            var unique = issuedItems
                .Where(x => !string.IsNullOrWhiteSpace(x.Uri))
                .GroupBy(x => x.Uri!, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Take(maxFiles)
                .ToList();

            // If user requested a single docType, prefer downloading only matching issuer doctype.
            // Prevents showing DL/PAN when tenant asked for Aadhaar, and keeps payload small.
            if (requestedDocTypes.Count == 1)
            {
                var req = (requestedDocTypes[0] ?? string.Empty).Trim().ToUpperInvariant();
                var want = req switch
                {
                    "PAN" => "PANCR",
                    "DL" or "DRIVING_LICENCE" or "DRIVINGLICENSE" or "DRIVING-LICENCE" => "DRVLC",
                    "AADHAAR" or "AADHAR" => "ADHAR",
                    _ => string.Empty
                };

                if (!string.IsNullOrWhiteSpace(want))
                {
                    var filtered = unique.Where(x => string.Equals((x.DocType ?? string.Empty).Trim(), want, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (filtered.Count > 0) unique = filtered;
                }
            }

            foreach (var it in unique)
            {
                try
                {
                    var file = await DownloadFileWithFallbackAsync(
                        http,
                        apiBase,
                        settings.FileDownloadPathTemplate,
                        accessToken,
                        it.Uri!,
                        clientSecret,
                        maxBytes,
                        ct);

                    var parsed = TryParseDigilockerDocument(it, file);
                    if (parsed is not null)
                    {
                        parsedDocs.Add(new { uri = it.Uri, doctype = it.DocType, parsed });
                        MergeCollected(collected, parsed);
                    }

                    downloaded.Add(new
                    {
                        uri = it.Uri,
                        doctype = it.DocType,
                        name = it.Name,
                        mime = file.Mime,
                        sizeBytes = file.SizeBytes,
                        sha256 = file.Sha256Hex,
                        hmac = file.HmacBase64,
                        hmacValid = file.HmacValid,
                        fileBase64 = file.Base64
                    });
                }
                catch (Exception ex)
                {
                    // Best-effort: do not fail the KYC if only the file download fails.
                    downloadErrors.Add(new
                    {
                        uri = it.Uri,
                        doctype = it.DocType,
                        error = ex.Message
                    });
                }
            }
        }

        var normalized = new
        {
            provider = "digilocker",
            fetchedAtUtc = DateTime.UtcNow,
            requestedDocTypes,
            documentTypes = docTypes.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            oauth = new
            {
                tokenExchangeStatus = (int)tokenRes.StatusCode,
                token = redactedToken
            },
            userDetailsStatus,
            userDetails = string.IsNullOrWhiteSpace(userDetailsBody) ? null : TryParseJson(userDetailsBody),
            issuedDocsStatus = issuedDocsStatus,
            issuedDocsUrl = issuedDocsUrl,
            issuedDocs = TryParseJson(issuedBody),
            files = downloaded,
            fileDownloadErrors = downloadErrors,
            parsedDocs,
            collected
        };

        var resultJson = JsonSerializer.Serialize(normalized);
        // Decide success based on what the tenant requested, not only "issued-docs list succeeded".
        // Otherwise "aadhaar" requests can wrongly show PAN/DL records and be marked verified.
        var ok = issuedOk && IsRequestedDocSatisfied(requestedDocTypes, issuedItems, collected);
        var failureReason = string.Empty;
        if (!ok)
        {
            // Keep it short, but include the upstream error when present.
            failureReason = issuedOk
                ? "Requested document not available from DigiLocker."
                : $"Unable to fetch DigiLocker documents. status={issuedDocsStatus}";
            try
            {
                using var err = JsonDocument.Parse(string.IsNullOrWhiteSpace(issuedBody) ? "{}" : issuedBody);
                if (err.RootElement.ValueKind == JsonValueKind.Object &&
                    err.RootElement.TryGetProperty("error", out var ev) &&
                    !string.IsNullOrWhiteSpace(ev.GetString()))
                {
                    failureReason += $" error={ev.GetString()!.Trim()}";
                }
            }
            catch { }
        }
        return new KycProviderCallbackResult(
            ok,
            ok ? "verified" : "failed",
            ok ? string.Empty : failureReason,
            resultJson,
            docTypes.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private sealed record DigilockerSettings(
        string ClientId,
        string ClientSecret,
        string RedirectUri,
        string AuthorizeUrl,
        string AuthorizeExtraParams,
        string TokenUrl,
        string ApiBaseUrl,
        string Scope,
        string DocTypeParamName,
        string IssuedDocsPath,
        string UserDetailsPath,
        string FileDownloadPathTemplate,
        int MaxFileBytes,
        int MaxFilesPerSession,
        bool IncludeUserDetailsInResult,
        bool IncludeFilesInResult);

    private async Task<DigilockerSettings> LoadSettingsAsync(CancellationToken ct)
    {
        var map = await db.PlatformSettings
            .AsNoTracking()
            .Where(x => x.Scope == SettingsScope)
            .ToDictionaryAsync(x => x.Key, x => crypto.Decrypt(x.ValueEncrypted), StringComparer.OrdinalIgnoreCase, ct);

        string Pick(string key, string fallback) => map.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;
        static int ParseInt(string raw, int fallback) => int.TryParse(raw, out var n) ? n : fallback;
        static bool ParseBool(string raw, bool fallback) => bool.TryParse(raw, out var b) ? b : fallback;

        // Defaults match common DigiLocker requester OAuth base. Operators can override via platform settings.
        return new DigilockerSettings(
            ClientId: Pick("clientId", config["Digilocker:ClientId"] ?? config["DIGILOCKER_CLIENT_ID"] ?? string.Empty),
            ClientSecret: Pick("clientSecret", config["Digilocker:ClientSecret"] ?? config["DIGILOCKER_CLIENT_SECRET"] ?? string.Empty),
            RedirectUri: Pick("redirectUri", config["Digilocker:RedirectUri"] ?? config["DIGILOCKER_REDIRECT_URI"] ?? string.Empty),
            AuthorizeUrl: Pick("authorizeUrl", config["Digilocker:AuthorizeUrl"] ?? config["DIGILOCKER_AUTHORIZE_URL"] ?? "https://digilocker.meripehchaan.gov.in/public/oauth2/1/authorize"),
            AuthorizeExtraParams: Pick("authorizeExtraParams", config["Digilocker:AuthorizeExtraParams"] ?? config["DIGILOCKER_AUTHORIZE_EXTRA_PARAMS"] ?? string.Empty),
            TokenUrl: Pick("tokenUrl", config["Digilocker:TokenUrl"] ?? config["DIGILOCKER_TOKEN_URL"] ?? "https://digilocker.meripehchaan.gov.in/public/oauth2/1/token"),
            ApiBaseUrl: Pick("apiBaseUrl", config["Digilocker:ApiBaseUrl"] ?? config["DIGILOCKER_API_BASE_URL"] ?? "https://digilocker.meripehchaan.gov.in/public"),
            // Default to the real DigiLocker scope tokens.
            // UI can still send "friendly" tokens (issued-documents/profile/age-verification); BuildRedirect normalizes.
            // IMPORTANT: Do NOT include "openid" unless your DigiLocker client is configured for it.
            Scope: Pick("scope", config["Digilocker:Scope"] ?? config["DIGILOCKER_SCOPE"] ?? "files.issueddocs userdetails email address picture avs"),
            DocTypeParamName: Pick("docTypeParamName", "req_doctype"),
            // Your desired flow uses /oauth2/1/files; keep configurable because DigiLocker versions differ.
            IssuedDocsPath: Pick("issuedDocsPath", "/oauth2/1/files"),
            UserDetailsPath: Pick("userDetailsPath", "/oauth2/1/user"),
            FileDownloadPathTemplate: Pick("fileDownloadPathTemplate", config["Digilocker:FileDownloadPathTemplate"] ?? "/oauth2/1/files/{uri}"),
            MaxFileBytes: ParseInt(Pick("maxFileBytes", config["Digilocker:MaxFileBytes"] ?? DefaultMaxFileBytes.ToString()), DefaultMaxFileBytes),
            MaxFilesPerSession: ParseInt(Pick("maxFilesPerSession", config["Digilocker:MaxFilesPerSession"] ?? DefaultMaxFilesPerSession.ToString()), DefaultMaxFilesPerSession),
            IncludeUserDetailsInResult: ParseBool(Pick("includeUserDetailsInResult", config["Digilocker:IncludeUserDetailsInResult"] ?? "true"), true),
            IncludeFilesInResult: ParseBool(Pick("includeFilesInResult", config["Digilocker:IncludeFilesInResult"] ?? "true"), true)
        );
    }

    private static string Require(string value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"DigiLocker setting '{key}' is not configured.");
        return value.Trim();
    }

    private static string CombineApiUrl(string apiBase, string pathOrUrl)
    {
        var baseUrl = (apiBase ?? string.Empty).Trim().TrimEnd('/');
        var raw = (pathOrUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(baseUrl)) return raw;
        if (string.IsNullOrWhiteSpace(raw)) return baseUrl;

        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return raw;

        var path = raw.StartsWith("/") ? raw : "/" + raw;

        // Support both configurations:
        // A) apiBaseUrl = https://.../public and paths include /oauth2/1/...
        // B) apiBaseUrl = https://.../public/oauth2/1 and paths are /files, /user, /files/{uri}
        // If someone mixes A+B, strip duplicated prefixes.
        if (baseUrl.EndsWith("/oauth2/1", StringComparison.OrdinalIgnoreCase) && path.StartsWith("/oauth2/1/", StringComparison.OrdinalIgnoreCase))
            path = path["/oauth2/1".Length..];
        if (baseUrl.EndsWith("/public", StringComparison.OrdinalIgnoreCase) && path.StartsWith("/public/", StringComparison.OrdinalIgnoreCase))
            path = path["/public".Length..];
        if (baseUrl.EndsWith("/public/oauth2/1", StringComparison.OrdinalIgnoreCase) && path.StartsWith("/public/oauth2/1/", StringComparison.OrdinalIgnoreCase))
            path = path["/public/oauth2/1".Length..];

        return baseUrl + path;
    }

    private static bool TryCollectFromUserDetails(Dictionary<string, object?> collected, string userDetailsBody, out object parsed)
    {
        parsed = new { };
        if (string.IsNullOrWhiteSpace(userDetailsBody)) return false;

        using var doc = JsonDocument.Parse(userDetailsBody);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;

        var root = doc.RootElement;
        string? GetStr(string key)
            => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        var name = (GetStr("name") ?? string.Empty).Trim();
        var dobRaw = (GetStr("dob") ?? string.Empty).Trim();
        var genderRaw = (GetStr("gender") ?? string.Empty).Trim();
        var email = (GetStr("email") ?? string.Empty).Trim();
        var mobile = (GetStr("mobile") ?? string.Empty).Trim();
        var picture = (GetStr("picture") ?? string.Empty).Trim();
        var eaadhaar = (GetStr("eaadhaar") ?? string.Empty).Trim();
        var referenceKey = (GetStr("reference_key") ?? string.Empty).Trim();
        var address =
            (GetStr("address") ?? GetStr("full_address") ?? GetStr("current_address") ?? GetStr("permanent_address") ?? string.Empty).Trim();

        // Some environments may return masked Aadhaar in /user (rare). If present, mask it again.
        var aadhaarRaw =
            (GetStr("aadhaar") ?? GetStr("masked_aadhaar") ?? GetStr("aadhaar_number") ?? GetStr("aadhaarNo") ?? string.Empty).Trim();
        string? aadhaarMasked = null;
        if (!string.IsNullOrWhiteSpace(aadhaarRaw))
        {
            var digits = new string(aadhaarRaw.Where(char.IsDigit).ToArray());
            if (digits.Length >= 4)
            {
                var last4 = digits[^4..];
                aadhaarMasked = "XXXXXXXX" + last4;
            }
        }

        string? dob = null;
        int? ageYears = null;
        if (!string.IsNullOrWhiteSpace(dobRaw))
        {
            // Common in DigiLocker /user: "ddMMyyyy" (e.g. 15011986)
            if (dobRaw.Length == 8 && dobRaw.All(char.IsDigit))
            {
                var dd = int.Parse(dobRaw[..2]);
                var mm = int.Parse(dobRaw.Substring(2, 2));
                var yy = int.Parse(dobRaw.Substring(4, 4));
                try
                {
                    var dt = new DateTime(yy, mm, dd, 0, 0, 0, DateTimeKind.Utc);
                    dob = dt.ToString("dd/MM/yyyy");
                    ageYears = ComputeAgeYears(dt, DateTime.UtcNow.Date);
                }
                catch { }
            }
            else
            {
                // Keep as-is (some envs send dd-MM-yyyy or yyyy-MM-dd).
                dob = dobRaw;
            }
        }

        var gender = genderRaw;
        if (!string.IsNullOrWhiteSpace(genderRaw))
        {
            var g = genderRaw.Trim().ToUpperInvariant();
            gender = g switch
            {
                "M" => "Male",
                "F" => "Female",
                "O" => "Other",
                _ => genderRaw
            };
        }

        if (!string.IsNullOrWhiteSpace(name)) collected["name"] = name;
        if (!string.IsNullOrWhiteSpace(dob)) collected["dob"] = dob;
        if (!string.IsNullOrWhiteSpace(gender)) collected["gender"] = gender;
        if (ageYears.HasValue && ageYears.Value >= 0 && ageYears.Value <= 150) collected["ageYears"] = ageYears.Value;
        if (!string.IsNullOrWhiteSpace(email)) collected["email"] = email;
        if (!string.IsNullOrWhiteSpace(mobile)) collected["mobile"] = mobile;
        // DigiLocker returns picture as base64 (no data-uri prefix). Store as photoBase64 for frontend.
        if (!string.IsNullOrWhiteSpace(picture)) collected["photoBase64"] = picture;
        if (!string.IsNullOrWhiteSpace(address)) collected["address"] = address;
        if (!string.IsNullOrWhiteSpace(aadhaarMasked)) collected["aadhaarMasked"] = aadhaarMasked;
        if (!string.IsNullOrWhiteSpace(eaadhaar)) collected["aadhaarVerified"] = eaadhaar.Equals("Y", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(referenceKey)) collected["aadhaarReferenceKey"] = referenceKey;

        parsed = new
        {
            name,
            dob,
            gender,
            ageYears,
            email,
            mobile,
            address,
            aadhaarMasked,
            hasPicture = !string.IsNullOrWhiteSpace(picture),
            aadhaarVerified = eaadhaar.Equals("Y", StringComparison.OrdinalIgnoreCase),
            aadhaarReferenceKey = string.IsNullOrWhiteSpace(referenceKey) ? null : referenceKey
        };
        return !string.IsNullOrWhiteSpace(name)
               || !string.IsNullOrWhiteSpace(dob)
               || !string.IsNullOrWhiteSpace(gender)
               || !string.IsNullOrWhiteSpace(email)
               || !string.IsNullOrWhiteSpace(mobile)
               || !string.IsNullOrWhiteSpace(address)
               || !string.IsNullOrWhiteSpace(picture);
    }

    private static object? TryCollectFromIssuedItems(Dictionary<string, object?> collected, List<IssuedItem> items)
    {
        if (items is null || items.Count == 0) return null;

        string? pan = null;
        string? drivingLicense = null;

        foreach (var it in items)
        {
            var doctype = (it.DocType ?? string.Empty).Trim().ToUpperInvariant();
            var uri = (it.Uri ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(uri)) continue;

            // Example URIs:
            // - in.gov.pan-PANCR-BEQPK9277N
            // - in.gov.transport-DRVLC-RJ1820120000536
            var last = uri;
            var idx = uri.LastIndexOf('-');
            if (idx >= 0 && idx + 1 < uri.Length) last = uri[(idx + 1)..];
            last = last.Trim();

            if (string.IsNullOrWhiteSpace(last)) continue;

            if (doctype.Contains("PAN", StringComparison.OrdinalIgnoreCase))
            {
                if (IsPanLike(last)) pan = last.ToUpperInvariant();
                continue;
            }
            if (doctype.Contains("DRV", StringComparison.OrdinalIgnoreCase) || doctype.Contains("DL", StringComparison.OrdinalIgnoreCase))
            {
                drivingLicense ??= last;
                continue;
            }
        }

        if (!string.IsNullOrWhiteSpace(pan)) collected["pan"] = pan;
        if (!string.IsNullOrWhiteSpace(drivingLicense)) collected["drivingLicense"] = drivingLicense;

        if (pan is null && drivingLicense is null) return null;
        return new { pan, drivingLicense };
    }

    private static bool IsRequestedDocSatisfied(IReadOnlyList<string> requestedDocTypes, List<IssuedItem> issuedItems, Dictionary<string, object?> collected)
    {
        if (requestedDocTypes is null || requestedDocTypes.Count == 0) return true;
        var req = (requestedDocTypes[0] ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(req)) return true;

        bool HasDocTypePrefix(string prefix)
            => issuedItems.Any(x => !string.IsNullOrWhiteSpace(x.DocType) && x.DocType!.Trim().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        if (req is "PAN")
        {
            // PAN is typically PANCR (verification record) in DigiLocker.
            return HasDocTypePrefix("PAN") || !string.IsNullOrWhiteSpace(collected.TryGetValue("pan", out var p) ? p?.ToString() : null);
        }
        if (req is "DL" or "DRIVING_LICENCE" or "DRIVING-LICENCE" or "DRIVINGLICENSE")
        {
            return HasDocTypePrefix("DRV") || HasDocTypePrefix("DL") || !string.IsNullOrWhiteSpace(collected.TryGetValue("drivingLicense", out var dl) ? dl?.ToString() : null);
        }
        if (req is "AADHAAR" or "AADHAR")
        {
            // Some DigiLocker clients cannot pull e-Aadhaar document; treat verified account as "satisfied"
            // but keep number/address blank unless we actually parse Aadhaar XML/PDF.
            if (HasDocTypePrefix("ADH") || HasDocTypePrefix("AADH")) return true;
            if (collected.TryGetValue("aadhaarVerified", out var v) && v is bool b && b) return true;
            return false;
        }

        // Unknown docType: do not block verification.
        return true;
    }

    private static bool IsPanLike(string s)
    {
        // PAN format: AAAAA9999A
        if (string.IsNullOrWhiteSpace(s)) return false;
        var t = s.Trim();
        if (t.Length != 10) return false;
        for (var i = 0; i < 10; i++)
        {
            var c = t[i];
            if (i < 5 || i == 9)
            {
                if (!(c >= 'A' && c <= 'Z') && !(c >= 'a' && c <= 'z')) return false;
            }
            else
            {
                if (!(c >= '0' && c <= '9')) return false;
            }
        }
        return true;
    }


    private sealed record IssuedDocsFetch(string Url, int StatusCode, string Body);

    private static async Task<IssuedDocsFetch> TryGetIssuedDocsAsync(
        HttpClient http,
        string apiBase,
        string configuredPath,
        string accessToken,
        CancellationToken ct)
    {
        var candidates = new List<string>();
        void Add(string? p)
        {
            var v = (p ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(v)) return;
            if (!candidates.Any(x => string.Equals(x, v, StringComparison.OrdinalIgnoreCase)))
                candidates.Add(v);
        }

        Add(configuredPath);

        // Known working endpoints across environments.
        // Common: /public/oauth2/1/files
        Add("/oauth2/1/files");
        // Some environments require explicit "issued" list.
        Add("/oauth2/1/files/issued");
        // Newer docs describe oauth2/2 issued list.
        Add("/oauth2/2/files/issued");
        // Older samples show /files/issued when apiBase already includes /oauth2/1.
        Add("/files/issued");

        IssuedDocsFetch last = new(CombineApiUrl(apiBase, candidates[0]), 0, string.Empty);
        foreach (var path in candidates)
        {
            var url = CombineApiUrl(apiBase, path);
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                using var res = await http.SendAsync(req, ct);
                var body = await res.Content.ReadAsStringAsync(ct);
                last = new IssuedDocsFetch(url, (int)res.StatusCode, body);
                if (res.IsSuccessStatusCode) return last;
            }
            catch (Exception ex)
            {
                last = new IssuedDocsFetch(url, 0, JsonSerializer.Serialize(new { error = ex.Message }));
            }
        }

        return last;
    }

    private static (string Verifier, string Challenge) CreatePkcePair()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var verifier = Base64Url(bytes);
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var challenge = Base64Url(hash);
        return (verifier, challenge);
    }

    // Include session id in a parseable state so callbacks can locate the session even when redirect_uri can't carry a dynamic sessionId.
    // Format: v1.<guidN>.<random>
    private static string CreateState(KycSession session)
    {
        // IMPORTANT: IIS requestFiltering denyStrings often blocks sequences like "--" in query strings.
        // Base64url can include '-' and may generate "--". Use hex to keep state URL-safe and filter-safe.
        var nonceHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)); // 32 hex chars
        return $"v1.{session.Id:N}.{nonceHex}";
    }

    private static string Base64Url(byte[] input)
        => Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool FixedTimeEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left ?? string.Empty);
        var b = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static string AppendQuery(string baseUrl, Dictionary<string, string> parameters)
    {
        var sb = new StringBuilder(baseUrl);
        sb.Append(baseUrl.Contains('?') ? '&' : '?');
        var first = true;
        foreach (var kv in parameters)
        {
            if (!first) sb.Append('&');
            first = false;
            sb.Append(Uri.EscapeDataString(kv.Key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(kv.Value ?? string.Empty));
        }
        return sb.ToString();
    }

    private static bool IsReservedAuthorizeKey(string key)
        => key.Equals("response_type", StringComparison.OrdinalIgnoreCase)
           || key.Equals("client_id", StringComparison.OrdinalIgnoreCase)
           || key.Equals("redirect_uri", StringComparison.OrdinalIgnoreCase)
           || key.Equals("scope", StringComparison.OrdinalIgnoreCase)
           || key.Equals("state", StringComparison.OrdinalIgnoreCase)
           || key.Equals("code_challenge", StringComparison.OrdinalIgnoreCase)
           || key.Equals("code_challenge_method", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> ParseAuthorizeExtraParams(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Accept either a typical query string: "a=b&c=d" or a newline/semicolon-separated list.
        var normalized = raw.Trim();
        normalized = normalized.TrimStart('?');
        normalized = normalized.Replace("\r\n", "&").Replace("\n", "&").Replace(";", "&");

        // QueryHelpers requires a leading '?' and will URL-decode values.
        var parsed = QueryHelpers.ParseQuery("?" + normalized);
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in parsed)
        {
            var k = (kv.Key ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(k)) continue;
            var v = kv.Value.Count > 0 ? kv.Value[0] : string.Empty;
            v = (v ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(v)) continue;
            dict[k] = v;
        }

        return dict;
    }

    private static string ExpandAuthorizePlaceholders(string value, string acrFromDocType)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var v = value;
        if (!string.IsNullOrWhiteSpace(acrFromDocType))
        {
            v = v.Replace("{acr}", acrFromDocType, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            // If acr isn't resolvable (e.g. multiple docTypes), keep placeholder so it is obvious.
            // Callers can remove it or enforce single-docType sessions.
        }

        return v.Trim();
    }

    private static string TryResolveAcrFromDocTypes(IReadOnlyList<string> requestedDocTypes)
    {
        if (requestedDocTypes == null || requestedDocTypes.Count != 1) return string.Empty;
        var dt = (requestedDocTypes[0] ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(dt)) return string.Empty;
        dt = dt.Replace("-", "_", StringComparison.OrdinalIgnoreCase).Replace(" ", "_", StringComparison.OrdinalIgnoreCase);

        // DigiLocker ACR known values: pan, aadhaar, driving_licence.
        // Some DigiLocker setups accept multiple ACR values separated by spaces (or + in URLs).
        if (dt.Equals("PAN", StringComparison.OrdinalIgnoreCase)) return "pan";
        if (dt.Equals("AADHAAR", StringComparison.OrdinalIgnoreCase) || dt.Equals("AADHAR", StringComparison.OrdinalIgnoreCase)) return "aadhaar mobile email";
        if (dt.Equals("DL", StringComparison.OrdinalIgnoreCase)
            || dt.Equals("DRIVING_LICENCE", StringComparison.OrdinalIgnoreCase)
            || dt.Equals("DRIVING_LICENSE", StringComparison.OrdinalIgnoreCase))
            return "driving_licence";

        return string.Empty;
    }

    private static string MapReqDoctype(string raw)
    {
        var dt = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(dt)) return dt;
        dt = dt.Replace("-", "_", StringComparison.OrdinalIgnoreCase).Replace(" ", "_", StringComparison.OrdinalIgnoreCase);

        // Common DigiLocker issued doctype codes.
        if (dt.Equals("PAN", StringComparison.OrdinalIgnoreCase)) return "PANCR";
        if (dt.Equals("AADHAAR", StringComparison.OrdinalIgnoreCase) || dt.Equals("AADHAR", StringComparison.OrdinalIgnoreCase)) return "ADHAR";
        if (dt.Equals("DL", StringComparison.OrdinalIgnoreCase)
            || dt.Equals("DRIVING_LICENCE", StringComparison.OrdinalIgnoreCase)
            || dt.Equals("DRIVING_LICENSE", StringComparison.OrdinalIgnoreCase))
            return "DRVLC";

        return dt;
    }

    private static List<string> ParseStringList(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); } catch { return new(); }
    }

    private static object TryParseJson(string raw)
    {
        try
        {
            return JsonSerializer.Deserialize<object>(raw) ?? new { };
        }
        catch
        {
            return new { raw = raw };
        }
    }

    private static object RedactTokenPayload(JsonElement root)
    {
        try
        {
            if (root.ValueKind != JsonValueKind.Object) return new { };
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in root.EnumerateObject())
            {
                var name = p.Name;
                if (string.Equals(name, "access_token", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(name, "refresh_token", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(name, "id_token", StringComparison.OrdinalIgnoreCase)) continue;
                dict[name] = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString(),
                    JsonValueKind.Number => p.Value.TryGetInt64(out var n) ? n : p.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => p.Value.ToString()
                };
            }
            return dict;
        }
        catch
        {
            return new { };
        }
    }

    private sealed record IssuedItem(string? Uri, string? DocType, string? Name);

    private static IssuedItem ParseIssuedItem(JsonElement it)
    {
        string? GetStr(string key)
        {
            if (!it.TryGetProperty(key, out var v)) return null;
            if (v.ValueKind == JsonValueKind.String) return v.GetString();
            return v.ToString();
        }

        var uri = (GetStr("uri") ?? string.Empty).Trim();
        var doctype = (GetStr("doctype") ?? GetStr("doc_type") ?? string.Empty).Trim();
        var name = (GetStr("name") ?? string.Empty).Trim();

        return new IssuedItem(
            Uri: string.IsNullOrWhiteSpace(uri) ? null : uri,
            DocType: string.IsNullOrWhiteSpace(doctype) ? null : doctype,
            Name: string.IsNullOrWhiteSpace(name) ? null : name);
    }

    private sealed record DownloadedFile(
        string Mime,
        int SizeBytes,
        string Sha256Hex,
        string? HmacBase64,
        bool? HmacValid,
        string Base64,
        byte[] Bytes);

    private static object? TryParseDigilockerDocument(IssuedItem it, DownloadedFile file)
    {
        var doctype = (it.DocType ?? string.Empty).Trim().ToUpperInvariant();

        // Aadhaar often comes as XML (sometimes zipped).
        if (doctype is "ADHAR" or "AADHAAR")
        {
            var aadhaar = TryParseAadhaar(file.Bytes);
            if (aadhaar is not null) return new { type = "aadhaar", fields = aadhaar };
        }

        // PAN verification record is usually a PDF.
        if (doctype is "PANCR" or "PAN")
        {
            var pan = TryParsePan(file.Bytes);
            if (pan is not null) return new { type = "pan", fields = pan };
        }

        return null;
    }

    private static void MergeCollected(Dictionary<string, object?> collected, object parsed)
    {
        // parsed is { type, fields = { ... } }
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(parsed));
            if (!doc.RootElement.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Object) return;

            void PutIf(string key, string? val)
            {
                if (string.IsNullOrWhiteSpace(val)) return;
                if (!collected.ContainsKey(key)) collected[key] = val.Trim();
            }

            string? Get(string key)
            {
                return fields.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            }

            PutIf("name", Get("name"));
            PutIf("dob", Get("dob"));
            PutIf("gender", Get("gender"));
            PutIf("email", Get("email"));
            PutIf("address", Get("address"));
            PutIf("fatherName", Get("fatherName"));

            // Prefer masked ids where present.
            PutIf("pan", Get("panNumber"));
            PutIf("aadhaarMasked", Get("aadhaarMasked"));

            var photo = Get("photoBase64");
            if (!string.IsNullOrWhiteSpace(photo) && !collected.ContainsKey("photoBase64"))
                collected["photoBase64"] = photo.Trim();

            // Derived age if dob available.
            if (collected.TryGetValue("dob", out var dobObj) && dobObj is string dobStr && TryParseDate(dobStr, out var dob))
            {
                var age = ComputeAgeYears(dob, DateTime.UtcNow.Date);
                if (!collected.ContainsKey("ageYears")) collected["ageYears"] = age;
            }
        }
        catch
        {
            // ignore
        }
    }

    private static object? TryParsePan(byte[] bytes)
    {
        if (bytes.Length < 4) return null;

        // If it's a PDF, do a best-effort text extraction from FlateDecode streams.
        if (IsPdf(bytes))
        {
            var text = ExtractPdfText(bytes);
            if (string.IsNullOrWhiteSpace(text)) return null;

            var pan = FindFirstRegex(text, @"\b[A-Z]{5}[0-9]{4}[A-Z]\b");
            var dob = FindFirstRegex(text, @"\b\d{2}[-/]\d{2}[-/]\d{4}\b");
            var gender = FindFirstRegex(text, @"\b(MALE|FEMALE|OTHER)\b");

            // Heuristic: capture between NAME and GENDER/DATE OF BIRTH
            var name = FindBetween(text, "NAME", new[] { "GENDER", "DATE OF BIRTH", "DOB", "VERIFIED" });
            name = NormalizePersonName(name);

            return new
            {
                panNumber = pan ?? string.Empty,
                name = name ?? string.Empty,
                dob = dob ?? string.Empty,
                gender = gender ?? string.Empty
            };
        }

        // Some environments may return JSON/XML instead of PDF.
        var maybeXml = TryParseXml(bytes);
        if (maybeXml is not null) return maybeXml;

        return null;
    }

    private static object? TryParseAadhaar(byte[] bytes)
    {
        if (bytes.Length < 2) return null;

        // ZIP container
        if (IsZip(bytes))
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
                foreach (var e in zip.Entries)
                {
                    if (e.Length <= 0) continue;
                    if (!e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
                    using var es = e.Open();
                    using var outMs = new MemoryStream();
                    es.CopyTo(outMs);
                    var parsed = TryParseAadhaarXml(outMs.ToArray());
                    if (parsed is not null) return parsed;
                }
            }
            catch
            {
                // ignore
            }
        }

        // XML direct
        var xml = TryParseAadhaarXml(bytes);
        if (xml is not null) return xml;

        // PDF fallback (best-effort text only)
        if (IsPdf(bytes))
        {
            var text = ExtractPdfText(bytes);
            if (string.IsNullOrWhiteSpace(text)) return null;

            var name = FindBetween(text, "Name", new[] { "Date of Birth", "DOB", "Gender", "Address" });
            name = NormalizePersonName(name);
            var dob = FindFirstRegex(text, @"\b\d{2}[-/]\d{2}[-/]\d{4}\b");
            var gender = FindFirstRegex(text, @"\b(MALE|FEMALE|OTHER)\b");
            var aadhaarMasked = FindFirstRegex(text, @"\bX{4,}\d{4}\b");
            var address = FindBetween(text, "Address", new[] { "Pin Code", "Pincode", "PIN" });

            return new
            {
                aadhaarMasked = aadhaarMasked ?? string.Empty,
                name = name ?? string.Empty,
                dob = dob ?? string.Empty,
                gender = gender ?? string.Empty,
                address = NormalizeWhitespace(address ?? string.Empty),
                photoBase64 = string.Empty
            };
        }

        return null;
    }

    private static object? TryParseAadhaarXml(byte[] xmlBytes)
    {
        try
        {
            var xml = Encoding.UTF8.GetString(xmlBytes);
            if (!xml.Contains("PrintLetterBarcodeData", StringComparison.OrdinalIgnoreCase)
                && !xml.Contains("UidData", StringComparison.OrdinalIgnoreCase))
            {
                // Not an Aadhaar XML we recognize
                // Still attempt parse for best effort.
            }

            var doc = XDocument.Parse(xml);
            var plbd = doc.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals("PrintLetterBarcodeData", StringComparison.OrdinalIgnoreCase));
            var uid = (plbd?.Attribute("uid")?.Value ?? string.Empty).Trim();
            var name = (plbd?.Attribute("name")?.Value ?? string.Empty).Trim();
            var gender = (plbd?.Attribute("gender")?.Value ?? string.Empty).Trim();
            var dob = (plbd?.Attribute("dob")?.Value ?? string.Empty).Trim();
            var yob = (plbd?.Attribute("yob")?.Value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(dob) && !string.IsNullOrWhiteSpace(yob))
                dob = $"{yob}-01-01";

            var co = (plbd?.Attribute("co")?.Value ?? string.Empty).Trim();

            string Attr(string key) => (plbd?.Attribute(key)?.Value ?? string.Empty).Trim();
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
            var address = string.Join(", ", addressParts.Where(x => !string.IsNullOrWhiteSpace(x)));

            var pht = doc.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals("Pht", StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
            pht = pht.Trim();

            var masked = MaskId(uid);

            return new
            {
                aadhaarMasked = masked,
                name,
                dob,
                gender,
                fatherName = ExtractFatherName(co),
                address = NormalizeWhitespace(address),
                photoBase64 = pht
            };
        }
        catch
        {
            return null;
        }
    }

    private static object? TryParseXml(byte[] bytes)
    {
        try
        {
            var xml = Encoding.UTF8.GetString(bytes);
            if (!xml.TrimStart().StartsWith("<", StringComparison.Ordinal)) return null;
            var doc = XDocument.Parse(xml);
            return new { xml = doc.Root?.Name.LocalName ?? "xml" };
        }
        catch
        {
            return null;
        }
    }

    private static bool IsZip(byte[] bytes) => bytes.Length >= 2 && bytes[0] == (byte)'P' && bytes[1] == (byte)'K';
    private static bool IsPdf(byte[] bytes) => bytes.Length >= 4 && bytes[0] == (byte)'%' && bytes[1] == (byte)'P' && bytes[2] == (byte)'D' && bytes[3] == (byte)'F';

    private static string MaskId(string id)
    {
        var s = (id ?? string.Empty).Trim();
        if (s.Length <= 4) return s;
        var last4 = s[^4..];
        return new string('X', Math.Max(0, s.Length - 4)) + last4;
    }

    private static string ExtractFatherName(string co)
    {
        // Examples: "S/O: John Doe" "D/O: Name" "C/O: Name"
        if (string.IsNullOrWhiteSpace(co)) return string.Empty;
        var idx = co.IndexOf(':');
        if (idx >= 0 && idx + 1 < co.Length) return co[(idx + 1)..].Trim();
        return co.Trim();
    }

    private static string? FindFirstRegex(string input, string pattern)
    {
        try
        {
            var m = System.Text.RegularExpressions.Regex.Match(input ?? string.Empty, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return m.Success ? m.Value.Trim() : null;
        }
        catch { return null; }
    }

    private static string NormalizeWhitespace(string s)
        => string.Join(" ", (s ?? string.Empty).Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

    private static string? FindBetween(string input, string startKey, string[] stopKeys)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var idx = input.IndexOf(startKey, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var sub = input[(idx + startKey.Length)..];
        var stop = sub.Length;
        foreach (var k in stopKeys)
        {
            var j = sub.IndexOf(k, StringComparison.OrdinalIgnoreCase);
            if (j >= 0 && j < stop) stop = j;
        }
        var chunk = sub[..stop];
        return NormalizeWhitespace(chunk);
    }

    private static string? NormalizePersonName(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var cleaned = s.Trim();
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"[^A-Za-z\s\.]", " ");
        cleaned = NormalizeWhitespace(cleaned);
        return cleaned;
    }

    private static bool TryParseDate(string raw, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        raw = raw.Trim();
        var formats = new[] { "dd-MM-yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "yyyy/MM/dd" };
        foreach (var f in formats)
        {
            if (DateTime.TryParseExact(raw, f, null, System.Globalization.DateTimeStyles.None, out date))
                return true;
        }
        return DateTime.TryParse(raw, out date);
    }

    private static int ComputeAgeYears(DateTime dob, DateTime onDate)
    {
        var age = onDate.Year - dob.Year;
        if (onDate.Month < dob.Month || (onDate.Month == dob.Month && onDate.Day < dob.Day)) age--;
        return Math.Max(0, age);
    }

    private static string ExtractPdfText(byte[] pdfBytes)
    {
        try
        {
            var text = ExtractPdfTextPdfPig(pdfBytes);
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        catch
        {
            // ignore, try heuristic fallback
        }

        return ExtractPdfTextHeuristic(pdfBytes);
    }

    private static string ExtractPdfTextPdfPig(byte[] pdfBytes)
    {
        try
        {
            // Optional runtime dependency:
            // If PdfPig is deployed, use it for reliable PDF text extraction.
            // If not present, return empty and fallback to heuristic extraction.
            //
            // We support two deployment modes:
            // 1) Normal NuGet reference: assemblies are alongside app base directory.
            // 2) Offline-bundled: assemblies are copied under `Assets/third_party/pdfpig/net8.0` and included in publish output.
            EnsurePdfPigLoaded();

            // Assembly name is typically `UglyToad.PdfPig`. Some forks may use `PdfPig`.
            var tDoc =
                Type.GetType("UglyToad.PdfPig.PdfDocument, UglyToad.PdfPig", throwOnError: false)
                ?? Type.GetType("UglyToad.PdfPig.PdfDocument, PdfPig", throwOnError: false);
            if (tDoc is null) return string.Empty;

            var miOpen = tDoc.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .FirstOrDefault(m =>
                    string.Equals(m.Name, "Open", StringComparison.Ordinal)
                    && m.GetParameters().Length >= 1
                    && typeof(Stream).IsAssignableFrom(m.GetParameters()[0].ParameterType));
            if (miOpen is null) return string.Empty;

            using var ms = new MemoryStream(pdfBytes);
            using var doc = miOpen.Invoke(null, new object?[] { ms }) as IDisposable;
            if (doc is null) return string.Empty;

            var miGetPages = doc.GetType().GetMethod("GetPages", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (miGetPages is null) return string.Empty;

            var pagesObj = miGetPages.Invoke(doc, Array.Empty<object?>());
            if (pagesObj is not System.Collections.IEnumerable pages) return string.Empty;

            var sb = new StringBuilder();
            foreach (var page in pages)
            {
                if (page is null) continue;
                var piText = page.GetType().GetProperty("Text", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var text = piText?.GetValue(page) as string;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.Append(' ');
                    sb.Append(text);
                }
            }

            return NormalizeWhitespace(sb.ToString());
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void EnsurePdfPigLoaded()
    {
        // If already loadable, no-op.
        if (Type.GetType("UglyToad.PdfPig.PdfDocument, UglyToad.PdfPig", throwOnError: false) is not null) return;
        if (Type.GetType("UglyToad.PdfPig.PdfDocument, PdfPig", throwOnError: false) is not null) return;

        // Attempt to load bundled assemblies from publish output folder.
        // In source tree these live under backend-dotnet/Assets/third_party/pdfpig/net8.0 and are copied to output.
        var baseDir = AppContext.BaseDirectory;
        var rel = Path.Combine("Assets", "third_party", "pdfpig", "net8.0");
        var dir = Path.Combine(baseDir, rel);
        if (!Directory.Exists(dir)) return;

        // Load dependencies first, then main assembly.
        var ordered = new[]
        {
            "UglyToad.PdfPig.Core.dll",
            "UglyToad.PdfPig.Tokens.dll",
            "UglyToad.PdfPig.Tokenization.dll",
            "UglyToad.PdfPig.Fonts.dll",
            "UglyToad.PdfPig.DocumentLayoutAnalysis.dll",
            "UglyToad.PdfPig.Package.dll",
            "UglyToad.PdfPig.dll",
        };

        foreach (var name in ordered)
        {
            var path = Path.Combine(dir, name);
            if (!File.Exists(path)) continue;
            try
            {
                // Avoid re-loading if already loaded.
                var simple = Path.GetFileNameWithoutExtension(path);
                var already = AppDomain.CurrentDomain.GetAssemblies()
                    .Any(a => string.Equals(a.GetName().Name, simple, StringComparison.OrdinalIgnoreCase));
                if (already) continue;

                AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }
            catch
            {
                // best-effort
            }
        }
    }

    private static string ExtractPdfTextHeuristic(byte[] pdfBytes)
    {
        // Fallback extraction (no external deps):
        // - Locate FlateDecode streams
        // - ZLib decompress
        // - Pull literal strings (...) used in text operators
        try
        {
            var all = new StringBuilder();
            var ascii = Encoding.ASCII.GetString(pdfBytes);
            var pos = 0;
            while (true)
            {
                var idx = ascii.IndexOf("/FlateDecode", pos, StringComparison.Ordinal);
                if (idx < 0) break;
                var streamIdx = ascii.IndexOf("stream", idx, StringComparison.Ordinal);
                if (streamIdx < 0) break;
                var start = streamIdx + "stream".Length;
                while (start < ascii.Length && (ascii[start] == '\r' || ascii[start] == '\n' || ascii[start] == ' ')) start++;
                var endstreamIdx = ascii.IndexOf("endstream", start, StringComparison.Ordinal);
                if (endstreamIdx < 0) break;

                var byteStart = start;
                var byteEnd = endstreamIdx;
                if (byteStart >= 0 && byteEnd > byteStart && byteEnd <= pdfBytes.Length)
                {
                    var slice = pdfBytes.AsSpan(byteStart, byteEnd - byteStart).ToArray();
                    var inflated = TryInflateZlib(slice);
                    if (inflated.Length > 0)
                    {
                        all.Append(' ');
                        all.Append(ExtractPdfLiteralStrings(inflated));
                    }
                }

                pos = endstreamIdx + 9;
            }

            return NormalizeWhitespace(all.ToString());
        }
        catch
        {
            return string.Empty;
        }
    }

    private static byte[] TryInflateZlib(byte[] input)
    {
        try
        {
            using var ms = new MemoryStream(input);
            using var z = new ZLibStream(ms, CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            z.CopyTo(outMs);
            return outMs.ToArray();
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }

    private static string ExtractPdfLiteralStrings(byte[] content)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < content.Length)
        {
            if (content[i] == (byte)'(')
            {
                i++;
                var str = new StringBuilder();
                var depth = 1;
                while (i < content.Length && depth > 0)
                {
                    var b = content[i++];
                    if (b == (byte)'\\')
                    {
                        if (i < content.Length)
                        {
                            var next = content[i++];
                            str.Append((char)next);
                        }
                        continue;
                    }
                    if (b == (byte)'(') { depth++; str.Append(' '); continue; }
                    if (b == (byte)')') { depth--; if (depth == 0) break; str.Append(' '); continue; }
                    if (b >= 32 && b <= 126) str.Append((char)b);
                }
                var s = NormalizeWhitespace(str.ToString());
                if (!string.IsNullOrWhiteSpace(s))
                {
                    sb.Append(' ');
                    sb.Append(s);
                }
                continue;
            }
            i++;
        }
        return sb.ToString();
    }

    private static async Task<DownloadedFile> DownloadFileWithFallbackAsync(
        HttpClient http,
        string apiBase,
        string fileDownloadPathTemplate,
        string accessToken,
        string uri,
        string clientSecret,
        int maxBytes,
        CancellationToken ct)
    {
        var candidates = BuildFileDownloadCandidates(apiBase, fileDownloadPathTemplate, uri);

        Exception? lastEx = null;
        foreach (var url in candidates)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!res.IsSuccessStatusCode)
                {
                    var body = string.Empty;
                    try
                    {
                        body = await res.Content.ReadAsStringAsync(ct);
                        if (body.Length > 2000) body = body[..2000];
                    }
                    catch { }

                    lastEx = new InvalidOperationException($"File download failed. status={(int)res.StatusCode} url={url} body={body}");
                    continue;
                }

                var mime = res.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
                var contentLen = res.Content.Headers.ContentLength;
                if (contentLen.HasValue && contentLen.Value > maxBytes)
                    throw new InvalidOperationException($"File too large ({contentLen.Value} bytes). Max allowed is {maxBytes} bytes.");

                await using var stream = await res.Content.ReadAsStreamAsync(ct);
                using var ms = new MemoryStream();
                var buffer = new byte[81920];
                while (true)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                    if (read <= 0) break;
                    if (ms.Length + read > maxBytes)
                        throw new InvalidOperationException($"File too large (streamed > {maxBytes} bytes).");
                    ms.Write(buffer, 0, read);
                }

                var bytes = ms.ToArray();

                // Guard: Some endpoints return JSON/HTML (e.g. listing or error) with 200.
                // Only treat as downloadable file if it looks like a real PDF/XML/ZIP payload.
                if (!LooksLikeSupportedFile(bytes))
                {
                    var snippet = SafeUtf8Snippet(bytes, 2000);
                    lastEx = new InvalidOperationException($"File download returned non-file payload. url={url} mime={mime} body={snippet}");
                    continue;
                }
                var sha256 = SHA256.HashData(bytes);
                var sha256Hex = Convert.ToHexString(sha256).ToLowerInvariant();

                string? hmacHeader = null;
                bool? hmacValid = null;
                if (res.Headers.TryGetValues("hmac", out var hv))
                {
                    hmacHeader = hv.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(hmacHeader))
                    {
                        try
                        {
                            using var h = new HMACSHA256(Encoding.UTF8.GetBytes(clientSecret ?? string.Empty));
                            var computed = h.ComputeHash(bytes);
                            var computedB64 = Convert.ToBase64String(computed);
                            hmacValid = FixedTimeEquals(computedB64, hmacHeader.Trim());
                        }
                        catch
                        {
                            hmacValid = null;
                        }
                    }
                }

                return new DownloadedFile(
                    Mime: mime,
                    SizeBytes: bytes.Length,
                    Sha256Hex: sha256Hex,
                    HmacBase64: hmacHeader,
                    HmacValid: hmacValid,
                    Base64: Convert.ToBase64String(bytes),
                    Bytes: bytes);
            }
            catch (Exception ex)
            {
                lastEx = ex;
            }
        }

        throw lastEx ?? new InvalidOperationException("File download failed.");
    }

    private static bool LooksLikeSupportedFile(byte[] bytes)
    {
        if (bytes is null || bytes.Length < 2) return false;
        if (IsPdf(bytes) || IsZip(bytes)) return true;
        // XML usually starts with "<" (possibly with BOM/whitespace)
        for (var i = 0; i < Math.Min(bytes.Length, 64); i++)
        {
            var b = bytes[i];
            if (b == 0xEF || b == 0xBB || b == 0xBF) continue; // UTF-8 BOM bytes
            if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') continue;
            return b == (byte)'<';
        }
        return false;
    }

    private static string SafeUtf8Snippet(byte[] bytes, int maxChars)
    {
        try
        {
            var s = Encoding.UTF8.GetString(bytes);
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            if (s.Length > maxChars) s = s[..maxChars];
            return s;
        }
        catch
        {
            return "<binary>";
        }
    }

    private static IReadOnlyList<string> BuildFileDownloadCandidates(string apiBase, string templateRaw, string uri)
    {
        var encodedUri = Uri.EscapeDataString(uri);
        var list = new List<string>();
        void Add(string? u)
        {
            var v = (u ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(v)) return;
            if (!list.Any(x => string.Equals(x, v, StringComparison.OrdinalIgnoreCase)))
                list.Add(v);
        }

        // Preferred fallback order (stop at first 200):
        // 1) /public/oauth2/1/files/{uri}
        // 2) /public/oauth2/1/files/issued/{uri}
        // 3) /public/oauth2/1/docs/{uri}
        // 4) /public/oauth2/1/file/{uri}
        // Note: CombineApiUrl supports apiBaseUrl being either .../public or .../public/oauth2/1.
        Add(CombineApiUrl(apiBase, "/oauth2/1/files/" + encodedUri));
        Add(CombineApiUrl(apiBase, "/oauth2/1/files/issued/" + encodedUri));
        Add(CombineApiUrl(apiBase, "/oauth2/1/docs/" + encodedUri));
        Add(CombineApiUrl(apiBase, "/oauth2/1/file/" + encodedUri));

        // If a custom template is configured, try it after the known-good list (still deduped).
        var tpl = string.IsNullOrWhiteSpace(templateRaw) ? string.Empty : templateRaw.Trim();
        if (!string.IsNullOrWhiteSpace(tpl))
        {
            if (!tpl.Contains("{uri}", StringComparison.OrdinalIgnoreCase))
                tpl = tpl.TrimEnd('/') + "/{uri}";

            if (tpl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || tpl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                Add(tpl.Replace("{uri}", encodedUri, StringComparison.OrdinalIgnoreCase));
            else
                Add(CombineApiUrl(apiBase, (tpl.StartsWith("/") ? tpl : "/" + tpl).Replace("{uri}", encodedUri, StringComparison.OrdinalIgnoreCase)));
        }

        // Extra legacy fallbacks (older/newer versions).
        Add(CombineApiUrl(apiBase, "/oauth2/2/files/" + encodedUri));
        Add(CombineApiUrl(apiBase, "/oauth2/2/files/issued/" + encodedUri));
        return list;
    }
}
