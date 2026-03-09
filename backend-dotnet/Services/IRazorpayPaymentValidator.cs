namespace Textzy.Api.Services;

public sealed record RazorpayPaymentValidationResult(bool Ok, string Error, string Raw);

public interface IRazorpayPaymentValidator
{
    Task<RazorpayPaymentValidationResult> ValidateAsync(
        string keyId,
        string keySecret,
        string paymentId,
        string expectedOrderId,
        decimal expectedAmount,
        string expectedCurrency,
        CancellationToken ct = default);
}
