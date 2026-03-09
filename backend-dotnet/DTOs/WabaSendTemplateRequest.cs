namespace Textzy.Api.DTOs;

public class WabaSendTemplateRequest
{
    public string Recipient { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = "en";
    public List<string> BodyParameters { get; set; } = [];
}
