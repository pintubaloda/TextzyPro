namespace Textzy.Api.Services;

public class WorkflowRuntimeOptions
{
    public string EngineMode { get; set; } = "legacy"; // legacy|shadow|new
    public bool ShadowLogOnly { get; set; } = true;
    public bool EnableExecutionState { get; set; } = false;
    public int InlineDelayMaxSeconds { get; set; } = 10;
    public int DelayMaxRetries { get; set; } = 3;
}
