namespace Textzy.Api.Services;

public class TenancyContext
{
    public Guid TenantId { get; private set; }
    public string TenantSlug { get; private set; } = string.Empty;
    public string DataConnectionString { get; private set; } = string.Empty;
    public bool IsSet => TenantId != Guid.Empty;

    public void SetTenant(Guid tenantId, string tenantSlug, string dataConnectionString)
    {
        TenantId = tenantId;
        TenantSlug = tenantSlug;
        DataConnectionString = dataConnectionString;
    }
}
