using Textzy.Api.Models;

namespace Textzy.Api.DTOs;

public class SendMessageRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public ChannelType Channel { get; set; }
    public Guid? CampaignId { get; set; }
    public bool UseTemplate { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public string TemplateLanguageCode { get; set; } = "en";
    public List<string> TemplateParameters { get; set; } = [];
    public bool IsMedia { get; set; }
    public string MediaType { get; set; } = string.Empty; // image | video | audio | document
    public string MediaId { get; set; } = string.Empty;
    public string MediaCaption { get; set; } = string.Empty;
    public bool IsInteractive { get; set; }
    public string InteractiveType { get; set; } = string.Empty; // button | flow
    public List<string> InteractiveButtons { get; set; } = [];
    public string InteractiveFlowId { get; set; } = string.Empty;
    public string InteractiveFlowCta { get; set; } = string.Empty;
    public string InteractiveFlowToken { get; set; } = string.Empty;
    public string InteractiveFlowAction { get; set; } = string.Empty; // navigate | data_exchange
    public string InteractiveFlowScreen { get; set; } = string.Empty;
    public string InteractiveFlowDataJson { get; set; } = "{}";
    public int InteractiveFlowMessageVersion { get; set; } = 3;
    public string SmsSenderId { get; set; } = string.Empty;
    public string SmsPeId { get; set; } = string.Empty;
    public string SmsTemplateId { get; set; } = string.Empty;
    public bool ForcePlatformSmsConfig { get; set; }
}
