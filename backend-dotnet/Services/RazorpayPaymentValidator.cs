using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Textzy.Api.Services;

public sealed class RazorpayPaymentValidator : IRazorpayPaymentValidator
{
    public async Task<RazorpayPaymentValidationResult> ValidateAsync(
        string keyId,
        string keySecret,
        string paymentId,
        string expectedOrderId,
        decimal expectedAmount,
        string expectedCurrency,
        CancellationToken ct = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var authBytes = Encoding.UTF8.GetBytes($"{keyId}:{keySecret}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
        using var resp = await client.GetAsync($"https://api.razorpay.com/v1/payments/{Uri.EscapeDataString(paymentId)}", ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            return new RazorpayPaymentValidationResult(false, "Could not validate payment with Razorpay API.", raw);

        var expectedPaise = (int)Math.Round(expectedAmount * 100m, MidpointRounding.AwayFromZero);
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        var status = root.TryGetProperty("status", out var st) ? (st.GetString() ?? string.Empty).Trim().ToLowerInvariant() : string.Empty;
        if (string.Equals(status, "authorized", StringComparison.OrdinalIgnoreCase))
        {
            var capturePayload = JsonSerializer.Serialize(new
            {
                amount = expectedPaise,
                currency = string.IsNullOrWhiteSpace(expectedCurrency) ? "INR" : expectedCurrency.ToUpperInvariant()
            });
            using var captureContent = new StringContent(capturePayload, Encoding.UTF8, "application/json");
            using var captureResp = await client.PostAsync($"https://api.razorpay.com/v1/payments/{Uri.EscapeDataString(paymentId)}/capture", captureContent, ct);
            raw = await captureResp.Content.ReadAsStringAsync(ct);
            if (!captureResp.IsSuccessStatusCode)
                return new RazorpayPaymentValidationResult(false, "Payment was authorized but capture failed.", raw);

            using var captureDoc = JsonDocument.Parse(raw);
            root = captureDoc.RootElement.Clone();
            status = root.TryGetProperty("status", out st) ? (st.GetString() ?? string.Empty).Trim().ToLowerInvariant() : string.Empty;
        }

        var orderId = root.TryGetProperty("order_id", out var ord) ? (ord.GetString() ?? string.Empty) : string.Empty;
        var amountPaise = root.TryGetProperty("amount", out var amt) && amt.TryGetInt32(out var vAmt) ? vAmt : -1;
        var currency = root.TryGetProperty("currency", out var cur) ? (cur.GetString() ?? string.Empty) : string.Empty;

        if (!string.Equals(status, "captured", StringComparison.OrdinalIgnoreCase))
            return new RazorpayPaymentValidationResult(false, $"Payment status is '{status}'.", raw);
        if (!string.Equals(orderId, expectedOrderId, StringComparison.Ordinal))
            return new RazorpayPaymentValidationResult(false, "Payment order mismatch.", raw);
        if (amountPaise != expectedPaise)
            return new RazorpayPaymentValidationResult(false, "Payment amount mismatch.", raw);
        if (!string.Equals(currency, expectedCurrency, StringComparison.OrdinalIgnoreCase))
            return new RazorpayPaymentValidationResult(false, "Payment currency mismatch.", raw);

        return new RazorpayPaymentValidationResult(true, string.Empty, raw);
    }
}
