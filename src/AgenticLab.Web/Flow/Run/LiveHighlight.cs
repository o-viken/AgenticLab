namespace AgenticLab.Web.Flow;

/// <summary>
/// What the live run is lighting up in the diagram right now: the active node and arrow, the tool being
/// contacted (with a preview of its arguments and, once it returned, its result) and the response-type
/// hint on the LLM receive arrow. Replaced wholesale as events arrive; <see cref="None"/> between runs.
/// </summary>
internal sealed record LiveHighlight(
    string? Node,
    string? Arrow,
    string? Tool,
    string? ToolArgs,
    string? ToolResult,
    string? ResponseHint)
{
    public static readonly LiveHighlight None = new(null, null, null, null, null, null);

    /// <summary>Applies the next streamed event, keeping the previous response hint when the event carries none.</summary>
    public LiveHighlight Apply(FlowEvent flowEvent)
    {
        var (node, arrow) = FlowEventMapping.MapTarget(flowEvent.Kind);
        var next = this with
        {
            Node = node,
            Arrow = arrow,
            Tool = FlowEventMapping.ToolNameFor(flowEvent),
            ResponseHint = FlowEventMapping.ResponseHintFor(flowEvent) ?? ResponseHint,
        };

        // Capture the call arguments and result so the resource box can show what the tool did.
        return flowEvent.Kind switch
        {
            "tool-call" => next with { ToolArgs = FlowEventMapping.TruncatePreview(flowEvent.Detail ?? flowEvent.Data), ToolResult = null },
            "tool-result" => next with { ToolResult = FlowEventMapping.TruncatePreview(flowEvent.Detail ?? flowEvent.Data) },
            _ => next,
        };
    }
}
