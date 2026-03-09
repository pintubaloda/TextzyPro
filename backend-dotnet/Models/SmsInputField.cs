namespace Textzy.Api.Models;

public class SmsInputField
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "text";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
