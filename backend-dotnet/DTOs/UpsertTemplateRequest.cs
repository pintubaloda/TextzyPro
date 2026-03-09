using Textzy.Api.Models;

namespace Textzy.Api.DTOs;

public class UpsertTemplateRequest
{
    public string Name { get; set; } = string.Empty;
    public ChannelType Channel { get; set; }
    public string Category { get; set; } = "UTILITY";
    public string Language { get; set; } = "en";
    public string LifecycleStatus { get; set; } = "draft";
    public int Version { get; set; } = 1;
    public string VariantGroup { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string DltEntityId { get; set; } = string.Empty;
    public string DltTemplateId { get; set; } = string.Empty;
    public string SmsSenderId { get; set; } = string.Empty;
    public string HeaderType { get; set; } = "none";
    public string HeaderText { get; set; } = string.Empty;
    public string HeaderMediaId { get; set; } = string.Empty;
    public string HeaderMediaName { get; set; } = string.Empty;
    public string FooterText { get; set; } = string.Empty;
    public string ButtonsJson { get; set; } = string.Empty;
}
