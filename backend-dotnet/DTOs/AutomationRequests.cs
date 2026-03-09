namespace Textzy.Api.DTOs;

public class CreateAutomationFlowRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Channel { get; set; } = "waba";
    public string TriggerType { get; set; } = "keyword";
    public string TriggerConfigJson { get; set; } = "{}";
    public string DefinitionJson { get; set; } = "{}";
}

public class UpdateAutomationFlowRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Channel { get; set; } = "waba";
    public string TriggerType { get; set; } = "keyword";
    public string TriggerConfigJson { get; set; } = "{}";
    public bool IsActive { get; set; } = true;
}

public class CreateFlowVersionRequest
{
    public string ChangeNote { get; set; } = string.Empty;
    public string? DefinitionJson { get; set; }
    public bool IsStagedRelease { get; set; }
}

public class PublishFlowVersionRequest
{
    public bool RequireApproval { get; set; }
}

public class RequestFlowApprovalRequest
{
    public Guid VersionId { get; set; }
}

public class DecideFlowApprovalRequest
{
    public string Decision { get; set; } = "approved";
    public string Comment { get; set; } = string.Empty;
}

public class SimulateAutomationRequest
{
    public Guid? VersionId { get; set; }
    public string TriggerType { get; set; } = string.Empty;
    public string TriggerPayloadJson { get; set; } = "{}";
}

public class UpsertAutomationNodeRequest
{
    public Guid? VersionId { get; set; }
    public string NodeKey { get; set; } = string.Empty;
    public string NodeType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ConfigJson { get; set; } = "{}";
    public string EdgesJson { get; set; } = "[]";
    public int Sequence { get; set; }
    public bool IsReusable { get; set; }
}

public class RunAutomationRequest
{
    public Guid? VersionId { get; set; }
    public string TriggerType { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public bool IsRetry { get; set; }
    public string TriggerPayloadJson { get; set; } = "{}";
}

public class ValidateFlowDefinitionRequest
{
    public string DefinitionJson { get; set; } = "{}";
}

public class SendFlowToUserRequest
{
    public Guid? VersionId { get; set; }
    public string Recipient { get; set; } = string.Empty;
    public string Body { get; set; } = "Please continue to the next step.";
    public string FlowId { get; set; } = string.Empty;
    public string FlowCta { get; set; } = "Open";
    public string FlowToken { get; set; } = string.Empty;
    public string FlowAction { get; set; } = "navigate";
    public string FlowScreen { get; set; } = string.Empty;
    public string FlowDataJson { get; set; } = "{}";
    public int FlowMessageVersion { get; set; } = 3;
}

public class FlowDataExchangeRequest
{
    public Guid? VersionId { get; set; }
    public string ScreenId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
}

public class ImportMetaFlowRequest
{
    public string MetaFlowId { get; set; } = string.Empty;
    public bool CreateNewVersion { get; set; } = true;
    public string ChangeNote { get; set; } = "Imported from Meta flow";
}

public class UpdateMetaFlowRequest
{
    public string Name { get; set; } = string.Empty;
    public string EndpointUri { get; set; } = string.Empty;
    public string CategoriesJson { get; set; } = string.Empty;
    public string DataApiVersion { get; set; } = string.Empty;
    public string JsonVersion { get; set; } = string.Empty;
    public string FlowJson { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class CreateMetaFlowRequest
{
    public string Name { get; set; } = string.Empty;
    public string CategoriesJson { get; set; } = string.Empty;
    public string EndpointUri { get; set; } = string.Empty;
    public string DataApiVersion { get; set; } = string.Empty;
    public string JsonVersion { get; set; } = string.Empty;
    public string FlowJson { get; set; } = string.Empty;
}

public class PublishMetaFlowRequest
{
    public string FlowJson { get; set; } = string.Empty;
}
