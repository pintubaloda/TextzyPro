using System.ComponentModel.DataAnnotations.Schema;

namespace Textzy.Api.Models;

[Table("ScheduledMessages")]
public class WorkflowScheduledMessage
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid FlowId { get; set; }
    public Guid ConversationId { get; set; }
    public string NodeId { get; set; } = string.Empty;
    public DateTime ScheduledForUtc { get; set; }
    public string MessageContent { get; set; } = "{}";
    public string Status { get; set; } = "pending";
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;
    public DateTime? NextRetryAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public string FailureReason { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
