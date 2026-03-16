namespace Textzy.Api.Models;

public class KycWebhookDelivery
{
    public Guid Id { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid TenantId { get; set; }
    public Guid SessionId { get; set; }
    public string Provider { get; set; } = string.Empty; // e.g. "digilocker"
    public string Url { get; set; } = string.Empty;
    public bool Ok { get; set; }
    public int StatusCode { get; set; }
    public int DurationMs { get; set; }
    public string RequestJson { get; set; } = string.Empty;
    public string ResponseBody { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}

