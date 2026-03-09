namespace Textzy.Api.Models;

public class OutboundDeadLetter
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid MessageId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public string Classification { get; set; } = string.Empty; // retryable | permanent | exhausted
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorTitle { get; set; } = string.Empty;
    public string ErrorDetail { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
