namespace Textzy.Api.DTOs;

public class RegisterRequest
{
    public string CompanyName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public string PlanCode { get; set; } = "starter";
}

