using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;

namespace Textzy.Api.Services.Kyc;

public sealed class GstKycProvider(
    ControlDbContext db,
    SecretCryptoService crypto,
    IHttpClientFactory httpClientFactory,
    ILogger<GstKycProvider> logger) : IKycProvider
{
    public string Code => "gst";

    private const string SettingsScope = "gst";
    private const string DefaultVerifyUrl = "https://appyflow.in/api/verifyGST";

    public async Task<(string RedirectUrl, string State)> BuildRedirectAsync(KycSession session, CancellationToken ct)
    {
        var gstNo = (session.GstNumber ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(gstNo))
        {
            await MarkFailedAsync(session, "gstNo is required.", ct);
            return (string.Empty, string.Empty);
        }

        var settings = await LoadSettingsAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.KeySecret))
        {
            await MarkFailedAsync(session, "GST API key is not configured.", ct);
            return (string.Empty, string.Empty);
        }

        try
        {
            var resultJson = await VerifyGstAsync(settings.VerifyUrl, settings.KeySecret, gstNo, ct);
            await MarkCompletedAsync(session, ok: true, resultJson, string.Empty, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GST verification failed for session {SessionId}", session.Id);
            await MarkFailedAsync(session, ex.Message, ct);
        }

        return (string.Empty, string.Empty);
    }

    public Task<KycProviderCallbackResult> HandleCallbackAsync(KycSession session, string code, string state, CancellationToken ct)
    {
        return Task.FromResult(new KycProviderCallbackResult(
            Ok: false,
            Status: "failed",
            FailureReason: "GST verification does not use OAuth callback.",
            ResultJson: "{}",
            DocumentTypes: Array.Empty<string>()));
    }

    private async Task<string> VerifyGstAsync(string verifyUrl, string keySecret, string gstNo, CancellationToken ct)
    {
        var url = BuildUrl(verifyUrl, keySecret, gstNo);
        var http = httpClientFactory.CreateClient();
        using var res = await http.GetAsync(url, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"GST verification failed. status={(int)res.StatusCode}");

        if (string.IsNullOrWhiteSpace(body))
            throw new InvalidOperationException("GST verification returned empty response.");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var error = false;
        if (root.TryGetProperty("error", out var e))
        {
            if (e.ValueKind == JsonValueKind.True) error = true;
            else if (e.ValueKind == JsonValueKind.False) error = false;
            else if (e.ValueKind == JsonValueKind.String)
                error = string.Equals(e.GetString(), "true", StringComparison.OrdinalIgnoreCase);
        }
        var message = root.TryGetProperty("message", out var m) ? (m.GetString() ?? string.Empty).Trim() : string.Empty;
        if (error)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "GST verification failed." : message);

        var normalized = new
        {
            provider = "gst",
            fetchedAtUtc = DateTime.UtcNow,
            gstNo,
            error = false,
            message,
            taxpayerInfo = root.TryGetProperty("taxpayerInfo", out var tp) ? JsonSerializer.Deserialize<object>(tp.GetRawText()) : null,
            filing = root.TryGetProperty("filing", out var fl) ? JsonSerializer.Deserialize<object>(fl.GetRawText()) : null,
            compliance = root.TryGetProperty("compliance", out var cp) ? JsonSerializer.Deserialize<object>(cp.GetRawText()) : null
        };
        return JsonSerializer.Serialize(normalized);
    }

    private static string BuildUrl(string baseUrl, string keySecret, string gstNo)
    {
        var sb = new StringBuilder();
        sb.Append((baseUrl ?? string.Empty).Trim());
        if (!sb.ToString().Contains("?", StringComparison.Ordinal)) sb.Append('?'); else sb.Append('&');
        sb.Append("key_secret=").Append(Uri.EscapeDataString(keySecret));
        sb.Append("&gstNo=").Append(Uri.EscapeDataString(gstNo));
        return sb.ToString();
    }

    private async Task MarkFailedAsync(KycSession session, string reason, CancellationToken ct)
    {
        session.Status = "failed";
        session.FailureReason = reason ?? string.Empty;
        session.ResultJsonEncrypted = string.Empty;
        session.CompletedAtUtc = DateTime.UtcNow;
        session.UpdatedAtUtc = DateTime.UtcNow;
        db.KycSessions.Update(session);
        await db.SaveChangesAsync(ct);
    }

    private async Task MarkCompletedAsync(KycSession session, bool ok, string resultJson, string failureReason, CancellationToken ct)
    {
        session.Status = ok ? "verified" : "failed";
        session.FailureReason = failureReason ?? string.Empty;
        session.ResultJsonEncrypted = string.IsNullOrWhiteSpace(resultJson) ? string.Empty : crypto.Encrypt(resultJson);
        session.CompletedAtUtc = DateTime.UtcNow;
        session.UpdatedAtUtc = DateTime.UtcNow;
        db.KycSessions.Update(session);
        await db.SaveChangesAsync(ct);
    }

    private async Task<GstSettings> LoadSettingsAsync(CancellationToken ct)
    {
        var map = await db.PlatformSettings
            .AsNoTracking()
            .Where(x => x.Scope == SettingsScope)
            .ToDictionaryAsync(x => x.Key, x => x.ValueEncrypted, StringComparer.OrdinalIgnoreCase, ct);

        string Pick(string key, string fallback = "")
        {
            if (!map.TryGetValue(key, out var v)) return fallback;
            try { return crypto.Decrypt(v).Trim(); }
            catch { return fallback; }
        }

        return new GstSettings(
            KeySecret: Pick("keySecret", string.Empty),
            VerifyUrl: Pick("verifyUrl", DefaultVerifyUrl));
    }

    private sealed record GstSettings(string KeySecret, string VerifyUrl);
}
