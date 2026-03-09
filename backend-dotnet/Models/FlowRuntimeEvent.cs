namespace Textzy.Api.Models;

public class FlowRuntimeEvent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? FlowId { get; set; }
    public string MetaFlowId { get; set; } = string.Empty;
    public string ConversationExternalId { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty; // open/submission/completion/error/endpoint.exchange
    public string EventSource { get; set; } = string.Empty; // webhook/manual/system
    public bool Success { get; set; }
    public int StatusCode { get; set; }
    public int DurationMs { get; set; }
    public string ScreenId { get; set; } = string.Empty;
    public string ActionName { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string ErrorDetail { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

