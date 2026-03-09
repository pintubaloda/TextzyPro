namespace Textzy.Api.Services;

public static class RolePermissionCatalog
{
    public const string Owner = "owner";
    public const string Admin = "admin";
    public const string Manager = "manager";
    public const string Support = "support";
    public const string Marketing = "marketing";
    public const string Finance = "finance";
    public const string SuperAdmin = "super_admin";

    private static readonly Dictionary<string, string[]> Matrix = new(StringComparer.OrdinalIgnoreCase)
    {
        [Owner] = PermissionCatalog.All,
        [Admin] =
        [
            PermissionCatalog.ContactsRead, PermissionCatalog.ContactsWrite,
            PermissionCatalog.CampaignsRead, PermissionCatalog.CampaignsWrite,
            PermissionCatalog.TemplatesRead, PermissionCatalog.TemplatesWrite,
            PermissionCatalog.AutomationRead, PermissionCatalog.AutomationWrite,
            PermissionCatalog.InboxRead, PermissionCatalog.InboxWrite,
            PermissionCatalog.BillingRead, PermissionCatalog.BillingWrite,
            PermissionCatalog.ApiRead, PermissionCatalog.ApiWrite
        ],
        [Manager] =
        [
            PermissionCatalog.ContactsRead, PermissionCatalog.ContactsWrite,
            PermissionCatalog.CampaignsRead, PermissionCatalog.CampaignsWrite,
            PermissionCatalog.TemplatesRead,
            PermissionCatalog.AutomationRead, PermissionCatalog.AutomationWrite,
            PermissionCatalog.InboxRead, PermissionCatalog.InboxWrite,
            PermissionCatalog.BillingRead,
            PermissionCatalog.ApiRead
        ],
        [Support] =
        [
            PermissionCatalog.InboxRead, PermissionCatalog.InboxWrite,
            PermissionCatalog.ContactsRead,
            PermissionCatalog.TemplatesRead,
            PermissionCatalog.ApiRead
        ],
        [Marketing] =
        [
            PermissionCatalog.CampaignsRead, PermissionCatalog.CampaignsWrite,
            PermissionCatalog.TemplatesRead, PermissionCatalog.TemplatesWrite,
            PermissionCatalog.ContactsRead,
            PermissionCatalog.ApiRead
        ],
        [Finance] =
        [
            PermissionCatalog.BillingRead, PermissionCatalog.BillingWrite,
            PermissionCatalog.CampaignsRead,
            PermissionCatalog.ApiRead
        ],
        [SuperAdmin] =
        [
            PermissionCatalog.ContactsRead, PermissionCatalog.ContactsWrite,
            PermissionCatalog.CampaignsRead, PermissionCatalog.CampaignsWrite,
            PermissionCatalog.TemplatesRead, PermissionCatalog.TemplatesWrite,
            PermissionCatalog.AutomationRead, PermissionCatalog.AutomationWrite,
            PermissionCatalog.InboxRead, PermissionCatalog.InboxWrite,
            PermissionCatalog.BillingRead, PermissionCatalog.BillingWrite,
            PermissionCatalog.ApiRead, PermissionCatalog.ApiWrite,
            PermissionCatalog.PlatformTenantsManage,
            PermissionCatalog.PlatformSettingsRead, PermissionCatalog.PlatformSettingsWrite
        ]
    };

    public static IReadOnlyList<string> GetPermissions(string role)
        => Matrix.TryGetValue(role, out var permissions) ? permissions : [];
}
