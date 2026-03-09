namespace Textzy.Api.Models;

public class Template
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public ChannelType Channel { get; set; }
    public string Category { get; set; } = "UTILITY";
    public string Language { get; set; } = "en";
    public string Body { get; set; } = string.Empty;
    public string LifecycleStatus { get; set; } = "draft";
    public int Version { get; set; } = 1;
    public string VariantGroup { get; set; } = string.Empty;
    public string Status { get; set; } = "Approved";
    public string DltEntityId { get; set; } = string.Empty;
    public string DltTemplateId { get; set; } = string.Empty;
    public string SmsSenderId { get; set; } = string.Empty;
    public string HeaderType { get; set; } = "none";
    public string HeaderText { get; set; } = string.Empty;
    public string HeaderMediaId { get; set; } = string.Empty;
    public string HeaderMediaName { get; set; } = string.Empty;
    public string FooterText { get; set; } = string.Empty;
    public string ButtonsJson { get; set; } = string.Empty;
    public string RejectionReason { get; set; } = string.Empty;
    public string SmsOperator { get; set; } = "all";
    public DateTime? EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
