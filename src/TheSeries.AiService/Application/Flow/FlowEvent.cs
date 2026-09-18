using System.Text.Json;
using Microsoft.Extensions.AI;

namespace TheSeries.AiService.Application.Flow;

/// <summary>
/// A single observable step in an agent run, used to animate the data flow in the web UI
/// (User → Client → Harness → Tools → LLM and back).
/// </summary>
/// <param name="Sequence">A monotonically increasing index, starting at 1.</param>
/// <param name="Kind">
/// The step category: <c>received</c>, <c>llm-request</c>, <c>tool-call</c>, <c>tool-result</c>,
/// <c>llm-response</c>, <c>final</c>, <c>error</c> or <c>breakpoint</c> (out-of-band pause state).
/// </param>
/// <param name="Label">A short, human-readable description of the step.</param>
/// <param name="Detail">Optional extra context, e.g. tool arguments or the reply text.</param>
/// <param name="Turn">The 1-based LLM round-trip this step belongs to; 0 before the first round-trip.</param>
/// <param name="Data">
/// Optional full, untruncated payload revealed on demand in the UI: the data sent to the LLM for an
/// <c>llm-request</c>, the model's response for an <c>llm-response</c>/<c>final</c>, or the raw tool
/// arguments/result for a <c>tool-call</c>/<c>tool-result</c>, or a serialized
/// <see cref="BreakpointNotice"/> for a <c>breakpoint</c> control event.
/// </param>
/// <param name="CallId">
/// The model-assigned identifier of the tool call a <c>tool-call</c>/<c>tool-result</c> (or
/// <c>ask-question</c>) step belongs to, so a result can be paired with its call even when the same
/// tool is called several times in one turn; null for every other kind.
/// </param>
/// <param name="ToolCall">Optional structured snapshot of a requested function call.</param>
public sealed record FlowEvent(int Sequence, string Kind, string Label, string? Detail = null, int Turn = 0, string? Data = null, string? CallId = null, FlowToolCall? ToolCall = null);

/// <summary>A function name and immutable JSON arguments, independent of the display-formatted payload.</summary>
public sealed record FlowToolCall(string Name, JsonElement Arguments)
{
    /// <summary>Snapshots a model-requested call before its arguments can change.</summary>
    public static FlowToolCall Capture(FunctionCallContent call) =>
        new(call.Name, JsonSerializer.SerializeToElement(call.Arguments));
}
