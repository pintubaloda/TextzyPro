namespace Textzy.Api.Services;

public class WhatsAppOptions
{
    public string AppId { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string VerifyToken { get; set; } = string.Empty;
    public string GraphApiBase { get; set; } = "https://graph.facebook.com";
    public string ApiVersion { get; set; } = "v21.0";
    public string EmbeddedSignupConfigId { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string WebhookCallbackUrl { get; set; } = string.Empty;
}
