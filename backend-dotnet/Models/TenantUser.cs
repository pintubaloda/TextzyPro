namespace Textzy.Api.Models;

public class TenantUser
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "admin";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
