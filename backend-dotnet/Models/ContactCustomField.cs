namespace Textzy.Api.Models;

public class ContactCustomField
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ContactId { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public string FieldValue { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
