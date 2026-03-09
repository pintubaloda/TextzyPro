namespace Textzy.Api.Models;

public class AutomationRun
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid FlowId { get; set; }
    public Guid? VersionId { get; set; }
    public string Mode { get; set; } = "live"; // live/simulate
    public string TriggerType { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string TriggerPayloadJson { get; set; } = string.Empty;
    public string Status { get; set; } = "Started";
    public string Log { get; set; } = string.Empty; // human readable log
    public string TraceJson { get; set; } = "[]"; // structured trace for debugger
    public string FailureReason { get; set; } = string.Empty;
    public int RetryCount { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}
