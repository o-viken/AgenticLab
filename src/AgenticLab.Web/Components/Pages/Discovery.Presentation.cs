namespace AgenticLab.Web.Components.Pages;

/// <summary>The Discovery page's presentation helpers: labels, CSS classes per source/phase/step and the expand state of detail rows.</summary>
public sealed partial class Discovery
{
    private bool IsRunningSource(string source) => _running && (_runningSource is "all" || _runningSource == source);

    private string SourceTitle(string source) => source switch
    {
        "mcp" => "MCP tools",
        "a2a" => "A2A agents",
        _ => source,
    };

    private string SourceSubtitle(string source) => source switch
    {
        "mcp" => "Model Context Protocol — tools discovered from a remote MCP server.",
        "a2a" => "Agent2Agent — agents this service can delegate to.",
        _ => string.Empty,
    };

    private string ServerLabel(string source) => source switch
    {
        "mcp" => "mcpserver",
        "a2a" => "a2aserver",
        _ => "server",
    };

    private string ServerIcon(string source) => source switch
    {
        "mcp" => "🧩",
        "a2a" => "🤝",
        _ => "🖥️",
    };

    private string StateClass(string source) => _state.GetValueOrDefault(source) switch
    {
        "Connected" => "state-connected",
        "Failed" => "state-failed",
        "Connecting" => "state-connecting",
        "NoEndpoint" => "state-none",
        _ => "state-notrun",
    };

    private static string StateLabel(string state) => state switch
    {
        "Connected" => "Connected",
        "Failed" => "Failed",
        "Connecting" => "Discovering…",
        "NoEndpoint" => "No endpoint",
        _ => "Not run",
    };

    // Flow-diagram helpers: which nodes/arrows are lit for a source's current phase.
    private string ClientNodeClass(string source) =>
        IsRunningSource(source) && _phase.GetValueOrDefault(source) is "cleanup" or "endpoint" or "connecting" or "listing" or "receiving"
            ? "active" : "";

    private string ServerNodeClass(string source) =>
        _phase.GetValueOrDefault(source) is "connecting" or "listing" or "receiving" or "done"
            ? "active" : "";

    // Arrows animate only while the source is actively running, so they stop when the run finishes.
    private string SendArrowClass(string source) =>
        IsRunningSource(source) && _phase.GetValueOrDefault(source) is "connecting" or "listing" ? "active" : "";

    private string RecvArrowClass(string source) =>
        IsRunningSource(source) && _phase.GetValueOrDefault(source) is "receiving" ? "active" : "";

    private static string StepClass(string kind) => kind switch
    {
        "Error" => "step-error",
        "Done" or "Complete" => "step-done",
        "Item" => "step-item",
        "NoEndpoint" => "step-warn",
        "Connecting" or "Listing" => "step-send",
        _ => "step-info",
    };

    // Expand/collapse state for step and item detail panels, keyed so each row toggles independently.
    private readonly HashSet<string> _expanded = [];

    private bool IsExpanded(string key) => _expanded.Contains(key);

    private void Toggle(string key)
    {
        if (!_expanded.Remove(key))
        {
            _expanded.Add(key);
        }
    }

    // The rich detail for a step: for a discovered tool/agent it's the definition (name + description);
    // otherwise the step message.
    private static bool HasStepDetail(DiscoveryEventDto evt) =>
        evt.Item is not null || !string.IsNullOrWhiteSpace(evt.Message);

    private static string StepDetailTitle(DiscoveryEventDto evt) =>
        evt.Item is { } item ? item.Name : evt.Kind;

    private static string StepDetailBody(DiscoveryEventDto evt) =>
        evt.Item is { Description: var d } && !string.IsNullOrWhiteSpace(d) ? d : evt.Message;

    /// <inheritdoc />
}
