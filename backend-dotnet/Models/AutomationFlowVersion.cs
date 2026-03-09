namespace Textzy.Api.Models;

public class AutomationFlowVersion
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid FlowId { get; set; }
    public int VersionNumber { get; set; } = 1;
    public string Status { get; set; } = "draft"; // draft/test/published/archived
    public string DefinitionJson { get; set; } = "{}"; // nodes, edges, triggers
    public string ChangeNote { get; set; } = string.Empty;
    public bool IsStagedRelease { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAtUtc { get; set; }
}
