namespace Textzy.Api.Services;

public static class PermissionCatalog
{
    public const string ContactsRead = "contacts.read";
    public const string ContactsWrite = "contacts.write";
    public const string CampaignsRead = "campaigns.read";
    public const string CampaignsWrite = "campaigns.write";
    public const string TemplatesRead = "templates.read";
    public const string TemplatesWrite = "templates.write";
    public const string AutomationRead = "automation.read";
    public const string AutomationWrite = "automation.write";
    public const string InboxRead = "inbox.read";
    public const string InboxWrite = "inbox.write";
    public const string BillingRead = "billing.read";
    public const string BillingWrite = "billing.write";
    public const string ApiRead = "api.read";
    public const string ApiWrite = "api.write";
    public const string PlatformTenantsManage = "platform.tenants.manage";
    public const string PlatformSettingsRead = "platform.settings.read";
    public const string PlatformSettingsWrite = "platform.settings.write";

    // Tenant-scoped permission catalog. Platform-only permissions are intentionally excluded.
    public static readonly string[] All =
    [
        ContactsRead, ContactsWrite,
        CampaignsRead, CampaignsWrite,
        TemplatesRead, TemplatesWrite,
        AutomationRead, AutomationWrite,
        InboxRead, InboxWrite,
        BillingRead, BillingWrite,
        ApiRead, ApiWrite
    ];
}
