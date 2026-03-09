namespace Textzy.Api.Models;

public class Contact
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? SegmentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NameEncrypted { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string EmailEncrypted { get; set; } = string.Empty;
    public string TagsCsv { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string PhoneEncrypted { get; set; } = string.Empty;
    public string PhoneHash { get; set; } = string.Empty;
    public string OptInStatus { get; set; } = "unknown";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
