namespace Textzy.Api.Models;

public class MessageEvent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? MessageId { get; set; }
    public string ProviderMessageId { get; set; } = string.Empty;
    public string Direction { get; set; } = "outbound"; // inbound | outbound
    public string EventType { get; set; } = string.Empty; // queued | accepted | sent | delivered | read | failed | received
    public string State { get; set; } = string.Empty;
    public int StatePriority { get; set; }
    public DateTime? EventTimestampUtc { get; set; }
    public string RecipientId { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string ConversationId { get; set; } = string.Empty;
    public string ConversationOriginType { get; set; } = string.Empty;
    public DateTime? ConversationExpirationUtc { get; set; }
    public bool? PricingBillable { get; set; }
    public string PricingCategory { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public string MediaId { get; set; } = string.Empty;
    public string MediaMimeType { get; set; } = string.Empty;
    public string MediaSha256 { get; set; } = string.Empty;
    public string ButtonPayload { get; set; } = string.Empty;
    public string ButtonText { get; set; } = string.Empty;
    public string InteractiveType { get; set; } = string.Empty;
    public string ListReplyId { get; set; } = string.Empty;
    public string ListReplyTitle { get; set; } = string.Empty;
    public string RawPayloadJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

