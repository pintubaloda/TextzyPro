namespace Textzy.Api.Models;

public class PlatformSetting
{
    public Guid Id { get; set; }
    public string Scope { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string ValueEncrypted { get; set; } = string.Empty;
    public Guid UpdatedByUserId { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
