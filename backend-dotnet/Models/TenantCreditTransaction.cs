namespace Textzy.Api.Models;

public class TenantCreditTransaction
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string MetricKey { get; set; } = string.Empty;
    public string TransactionType { get; set; } = string.Empty; // debit|refund|credit
    public int Units { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string ReferenceId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
