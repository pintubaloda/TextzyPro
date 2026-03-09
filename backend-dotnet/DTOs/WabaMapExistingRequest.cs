namespace Textzy.Api.DTOs;

public class WabaMapExistingRequest
{
    public Guid? TenantId { get; set; }
    public string WabaId { get; set; } = string.Empty;
    public string PhoneNumberId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
}
