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
            extra[settings.DocTypeParamName.Trim()] = string.Join(",", requestedDocTypes);
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
        var issuedPath = string.IsNullOrWhiteSpace(settings.IssuedDocsPath) ? "/files/issued" : settings.IssuedDocsPath.Trim();
        var issuedUrl = apiBase + (issuedPath.StartsWith("/") ? issuedPath : "/" + issuedPath);

        // Best-effort "issued docs" fetch. Exact schema varies by environment; store raw payload anyway.
        using var issuedReq = new HttpRequestMessage(HttpMethod.Get, issuedUrl);
        issuedReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var issuedRes = await http.SendAsync(issuedReq, ct);
        var issuedBody = await issuedRes.Content.ReadAsStringAsync(ct);

        var docTypes = new List<string>();
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
                }
            }
        }
        catch
        {
            // ignore parsing errors, we still store raw payload
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
            issuedDocsStatus = (int)issuedRes.StatusCode,
            issuedDocs = TryParseJson(issuedBody),
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
        string IssuedDocsPath);

    private async Task<DigilockerSettings> LoadSettingsAsync(CancellationToken ct)
    {
        var map = await db.PlatformSettings
            .AsNoTracking()
            .Where(x => x.Scope == SettingsScope)
            .ToDictionaryAsync(x => x.Key, x => crypto.Decrypt(x.ValueEncrypted), StringComparer.OrdinalIgnoreCase, ct);

        string Pick(string key, string fallback) => map.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;

        // Defaults match common DigiLocker requester OAuth base. Operators can override via platform settings.
        return new DigilockerSettings(
            ClientId: Pick("clientId", config["Digilocker:ClientId"] ?? config["DIGILOCKER_CLIENT_ID"] ?? string.Empty),
            ClientSecret: Pick("clientSecret", config["Digilocker:ClientSecret"] ?? config["DIGILOCKER_CLIENT_SECRET"] ?? string.Empty),
            RedirectUri: Pick("redirectUri", config["Digilocker:RedirectUri"] ?? config["DIGILOCKER_REDIRECT_URI"] ?? string.Empty),
            AuthorizeUrl: Pick("authorizeUrl", config["Digilocker:AuthorizeUrl"] ?? config["DIGILOCKER_AUTHORIZE_URL"] ?? "https://digilocker.meripehchaan.gov.in/public/oauth2/1/authorize"),
            AuthorizeExtraParams: Pick("authorizeExtraParams", config["Digilocker:AuthorizeExtraParams"] ?? config["DIGILOCKER_AUTHORIZE_EXTRA_PARAMS"] ?? string.Empty),
            TokenUrl: Pick("tokenUrl", config["Digilocker:TokenUrl"] ?? config["DIGILOCKER_TOKEN_URL"] ?? "https://digilocker.meripehchaan.gov.in/public/oauth2/1/token"),
            ApiBaseUrl: Pick("apiBaseUrl", config["Digilocker:ApiBaseUrl"] ?? config["DIGILOCKER_API_BASE_URL"] ?? "https://digilocker.meripehchaan.gov.in/public/oauth2/1"),
            Scope: Pick("scope", config["Digilocker:Scope"] ?? config["DIGILOCKER_SCOPE"] ?? "files.issueddocs"),
            DocTypeParamName: Pick("docTypeParamName", "req_doctype"),
            IssuedDocsPath: Pick("issuedDocsPath", "/files/issued")
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

        // DigiLocker ACR known values: pan, aadhaar, driving_licence
        if (dt.Equals("PAN", StringComparison.OrdinalIgnoreCase)) return "pan";
        if (dt.Equals("AADHAAR", StringComparison.OrdinalIgnoreCase) || dt.Equals("AADHAR", StringComparison.OrdinalIgnoreCase)) return "aadhaar";
        if (dt.Equals("DL", StringComparison.OrdinalIgnoreCase)
            || dt.Equals("DRIVING_LICENCE", StringComparison.OrdinalIgnoreCase)
            || dt.Equals("DRIVING_LICENSE", StringComparison.OrdinalIgnoreCase))
            return "driving_licence";

        return string.Empty;
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
}
