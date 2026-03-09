namespace Textzy.Api.Models;

public class SmsGatewayRequestLog
{
    public Guid Id { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string Provider { get; set; } = "tata";
    public Guid? TenantId { get; set; }
    public string Recipient { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public string PeId { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = "GET";
    public string RequestUrlMasked { get; set; } = string.Empty;
    public string RequestPayloadMasked { get; set; } = string.Empty;
    public int HttpStatusCode { get; set; }
    public string ResponseBody { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string Error { get; set; } = string.Empty;
    public int DurationMs { get; set; }
    public string ProviderMessageId { get; set; } = string.Empty;
}
