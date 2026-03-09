namespace Textzy.Api.Models;

public class TenantUserPermissionOverride
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Permission { get; set; } = string.Empty;
    public bool IsAllowed { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
