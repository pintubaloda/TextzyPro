namespace Textzy.Api.DTOs;

public class AuthTokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresInHours { get; set; } = 12;
}
