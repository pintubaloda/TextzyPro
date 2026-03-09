namespace Textzy.Api.Models;

public class AutomationNode
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid FlowId { get; set; }
    public Guid? VersionId { get; set; }
    public string NodeKey { get; set; } = string.Empty;
    public string NodeType { get; set; } = string.Empty; // condition/delay/api/send/handoff/subflow
    public string Name { get; set; } = string.Empty;
    public string ConfigJson { get; set; } = string.Empty;
    public string EdgesJson { get; set; } = "[]";
    public int Sequence { get; set; }
    public bool IsReusable { get; set; }
}
