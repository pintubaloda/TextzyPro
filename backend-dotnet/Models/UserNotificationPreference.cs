namespace Textzy.Api.Models;

public class UserNotificationPreference
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public bool DesktopEnabled { get; set; } = true;
    public bool SoundEnabled { get; set; } = true;
    public string SoundStyle { get; set; } = "whatsapp";
    public decimal SoundVolume { get; set; } = 1m;
    public bool InAppNewMessages { get; set; } = true;
    public bool InAppSystemAlerts { get; set; } = true;
    public DateTime? DndUntilUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

