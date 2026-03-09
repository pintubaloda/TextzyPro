using System.ComponentModel.DataAnnotations.Schema;

namespace Textzy.Api.Models;

[Table("TriggerEvaluationAudit")]
public class TriggerEvaluationAuditRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? FlowId { get; set; }
    public string InboundMessageId { get; set; } = string.Empty;
    public Guid? ConversationId { get; set; }
    public string MessageText { get; set; } = string.Empty;
    public string TriggerType { get; set; } = string.Empty;
    public bool IsMatch { get; set; }
    public int MatchScore { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime EvaluatedAtUtc { get; set; } = DateTime.UtcNow;
}
