namespace Textzy.Api.Models;

public class TenantCompanyProfile
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? OwnerGroupId { get; set; }

    public string CompanyName { get; set; } = string.Empty;
    public string LegalName { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string CompanySize { get; set; } = string.Empty;
    public string Gstin { get; set; } = string.Empty;
    public string Pan { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string BillingEmail { get; set; } = string.Empty;
    public string BillingPhone { get; set; } = string.Empty;
    public bool PublicApiEnabled { get; set; }
    public string ApiUsername { get; set; } = string.Empty;
    public string ApiPasswordEncrypted { get; set; } = string.Empty;
    public string ApiKeyEncrypted { get; set; } = string.Empty;
    public string ApiIpWhitelist { get; set; } = string.Empty;
    public decimal TaxRatePercent { get; set; } = 18m;
    public bool IsTaxExempt { get; set; }
    public bool IsReverseCharge { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
