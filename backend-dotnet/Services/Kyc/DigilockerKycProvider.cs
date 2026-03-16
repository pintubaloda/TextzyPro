using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;

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
        var scope = string.IsNullOrWhiteSpace(settings.Scope) ? "files.issueddocs" : settings.Scope.Trim();

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
                var userUrl = apiBase + (userPath.StartsWith("/") ? userPath : "/" + userPath);
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

        var issuedPath = string.IsNullOrWhiteSpace(settings.IssuedDocsPath) ? "/files/issued" : settings.IssuedDocsPath.Trim();
        var issuedUrl = apiBase + (issuedPath.StartsWith("/") ? issuedPath : "/" + issuedPath);

        // Best-effort "issued docs" fetch. Exact schema varies by environment; store raw payload anyway.
        using var issuedReq = new HttpRequestMessage(HttpMethod.Get, issuedUrl);
        issuedReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var issuedRes = await http.SendAsync(issuedReq, ct);
        var issuedBody = await issuedRes.Content.ReadAsStringAsync(ct);

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
        if (issuedRes.IsSuccessStatusCode && settings.IncludeFilesInResult && issuedItems.Count > 0)
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

            foreach (var it in unique)
            {
                try
                {
                    var file = await DownloadFileAsync(
                        http,
                        apiBase,
                        settings.FileDownloadPathTemplate,
                        accessToken,
                        it.Uri!,
                        clientSecret,
                        maxBytes,
                        ct);

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
            issuedDocsStatus = (int)issuedRes.StatusCode,
            issuedDocs = TryParseJson(issuedBody),
            files = downloaded,
            fileDownloadErrors = downloadErrors
        };

        var resultJson = JsonSerializer.Serialize(normalized);
        var ok = issuedRes.IsSuccessStatusCode; // treat failure as failed KYC (can be retried by user)
        return new KycProviderCallbackResult(
            ok,
            ok ? "verified" : "failed",
            ok ? string.Empty : "Unable to fetch DigiLocker documents.",
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
            // IMPORTANT: Do NOT include "openid" unless your DigiLocker client is configured for it.
            // Many DigiLocker environments reject openid token flows if enabled incorrectly.
            Scope: Pick("scope", config["Digilocker:Scope"] ?? config["DIGILOCKER_SCOPE"] ?? "issued-documents profile email address picture age-verification"),
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
        => $"v1.{session.Id:N}.{Base64Url(RandomNumberGenerator.GetBytes(16))}";

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

    private sealed record DownloadedFile(string Mime, int SizeBytes, string Sha256Hex, string? HmacBase64, bool? HmacValid, string Base64);

    private static async Task<DownloadedFile> DownloadFileAsync(
        HttpClient http,
        string apiBase,
        string fileDownloadPathTemplate,
        string accessToken,
        string uri,
        string clientSecret,
        int maxBytes,
        CancellationToken ct)
    {
        var template = string.IsNullOrWhiteSpace(fileDownloadPathTemplate) ? "/file/{uri}" : fileDownloadPathTemplate.Trim();
        if (!template.StartsWith("/")) template = "/" + template;
        if (!template.Contains("{uri}", StringComparison.OrdinalIgnoreCase))
            template = template.TrimEnd('/') + "/{uri}";

        var encodedUri = Uri.EscapeDataString(uri);
        var path = template.Replace("{uri}", encodedUri, StringComparison.OrdinalIgnoreCase);
        var url = apiBase.TrimEnd('/') + path;

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();

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
            Base64: Convert.ToBase64String(bytes));
    }
}
