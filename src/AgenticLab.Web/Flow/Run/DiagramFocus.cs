namespace AgenticLab.Web.Flow;

/// <summary>
/// What the diagram highlights: the active node/arrow, the resource box's tool call preview, the LLM
/// receive arrow's response hint and the loop badge. Follows the live run normally; while a captured
/// stage is pinned in the Execution explorer it derives everything from that stage instead.
/// </summary>
internal sealed class DiagramFocus(FlowRunController owner)
{
    private RunReplay Replay => owner.Replay;
    private LiveHighlight Live => owner.Highlight;

    private (string? Node, string? Arrow) CursorTarget =>
        Replay.SelectedStage is { } stage ? FlowEventMapping.MapTarget(stage.Kind) : (null, null);

    private string? CursorNode => Replay.Replaying ? CursorTarget.Node : Live.Node;
    private string? CursorArrow => Replay.Replaying ? CursorTarget.Arrow : Live.Arrow;

    /// <summary>
    /// Highlights a node when it is the active target. The Client and AiService are merged into one
    /// "harness" node and the Tools box lives inside it, so an active "harness" or "tools" lights it up.
    /// </summary>
    public string NodeClass(string node)
    {
        var current = CursorNode;
        var active = node switch
        {
            "harness" => current is "harness" or "tools",
            _ => current == node,
        };
        return active ? "active" : string.Empty;
    }

    public string ArrowClass(string arrow) => CursorArrow == arrow ? "active" : string.Empty;

    public string? ActiveArrow => CursorArrow;

    public string? ResponseHint =>
        Replay.Replaying ? (Replay.SelectedStage is { } stage ? FlowEventMapping.ResponseHintFor(stage) : null) : Live.ResponseHint;

    /// <summary>The resource currently being used, based on the active tool, or null when none is active.</summary>
    public ResourceInfo? ActiveResourceInfo => VendorCatalog.ActiveResource(ActiveToolName);

    /// <summary>The specific tool function that contacted the active resource, or null.</summary>
    public string? ActiveToolName =>
        Replay.Replaying ? (Replay.SelectedStage is { } stage ? FlowEventMapping.StageToolName(stage) : null) : Live.Tool;

    public string? ActiveToolArgs => Replay.Replaying ? ReplayToolArgs : Live.ToolArgs;

    public string? ActiveToolResult => Replay.Replaying ? ReplayToolResult : Live.ToolResult;

    private string? ReplayToolArgs
    {
        get
        {
            if (Replay.SelectedStage is not { } stage)
            {
                return null;
            }

            var call = stage.Kind == "tool-call" ? stage : Replay.CallFor(stage);
            return call is null ? null : FlowEventMapping.TruncatePreview(call.Detail ?? call.Data);
        }
    }

    // Only a result stage has a result: standing on the call must not reveal what came back afterwards.
    private string? ReplayToolResult =>
        Replay.SelectedStage is { Kind: "tool-result" } stage
            ? FlowEventMapping.TruncatePreview(stage.Detail ?? stage.Data)
            : null;

    /// <summary>A compact label for the loop badge: the live turn while running, or the total when finished.</summary>
    public string LoopLabel
    {
        get
        {
            var projections = owner.Projections;
            return Replay.Replaying
                ? (Replay.SelectedStage is { Turn: > 0 } stage ? $"Turn {stage.Turn}" : "—")
                : owner.Running
                    ? (projections.CurrentTurn == 0 ? "Turn …" : $"Turn {projections.CurrentTurn}")
                    : (projections.TotalTurns == 0 ? "—" : $"{projections.TotalTurns} turn{(projections.TotalTurns == 1 ? "" : "s")}");
        }
    }

    /// <summary>Whether the loop badge should pulse (a turn is in flight).</summary>
    public bool LoopActive => !Replay.Replaying && owner.Running && owner.Projections.CurrentTurn > 0;

    /// <summary>The caller recorded with the displayed exchange, not a later picker selection.</summary>
    public string A2ACaller =>
        (Replay.Replaying ? Replay.SelectedExchange?.Agent : owner.RunAgent) ?? owner.View.SelectedAgent ?? "Harness";

    /// <summary>Only live, unheld boundary events may animate; remote internals never animate.</summary>
    public bool AnimateA2A =>
        !Replay.Replaying && owner.Running && !owner.Paused && !owner.BreakpointPaused && owner.View.Options.Mode == FlowMode.Auto;
}
