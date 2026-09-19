using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgenticLab.Web.Flow;

/// <summary>
/// Pure mapping helpers that translate a streamed <see cref="FlowEvent"/> into the bits the UI needs:
/// which node/arrow it lights up, the tool it concerns, a short "response type" hint, and how it maps
/// into the growing Context stack. No UI or state dependency.
/// </summary>
internal static partial class FlowEventMapping
{
    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();

    /// <summary>Maps a flow event kind to the diagram node it targets and the arrow it animates.</summary>
    public static (string? Node, string? Arrow) MapTarget(string kind) => kind switch
    {
        "received" => ("harness", "user-node-send"),
        "llm-request" => ("llm", "node-llm-send"),
        "tool-call" => ("tools", "node-llm-recv"),
        "ask-question" => ("harness", "user-node-recv"),
        "tool-result" => ("harness", "node-llm-recv"),
        "llm-response" => ("harness", "node-llm-recv"),
        "final" => ("harness", "user-node-recv"),
        "error" => ("harness", null),
        _ => (null, null),
    };

    /// <summary>
    /// Extracts the tool name from a tool-result event so the matching resource lights up only once the
    /// external resource has actually been contacted (never on tool-call, which is just the LLM's request).
    /// Returns null for other steps. The tool-result label is "Tool → Harness: Name".
    /// </summary>
    public static string? ToolNameFor(FlowEvent flowEvent)
    {
        var marker = flowEvent.Kind switch
        {
            "tool-result" => "Harness: ",
            _ => null,
        };

        return marker is null ? null : ToolFromLabel(flowEvent.Label, marker);
    }

    /// <summary>
    /// The tool a captured stage concerns, for the Execution explorer. Unlike <see cref="ToolNameFor"/>
    /// — which ignores a tool-call so the live diagram's resource only lights up once the tool has
    /// actually been contacted — this names the tool on the call as well as on its result.
    /// </summary>
    public static string? StageToolName(FlowEvent flowEvent) => flowEvent.Kind switch
    {
        "tool-call" => ToolFromLabel(flowEvent.Label, "Tool: "),
        "tool-result" => ToolFromLabel(flowEvent.Label, "Harness: "),
        _ => null,
    };

    /// <summary>
    /// Names where a run has got to, reusing the execution-boundary wording of the breakpoints so the
    /// live status and the Settings tab's breakpoint list read the same way. Null for steps with no
    /// meaningful boundary.
    /// </summary>
    public static string? ProgressLabelFor(FlowEvent flowEvent) => flowEvent.Kind switch
    {
        "received" => "Message received",
        "llm-request" => "Model request sent",
        "llm-response" => "After model response",
        "tool-call" => WithTool("Before tool execution", ToolFromLabel(flowEvent.Label, "Tool: ")),
        "tool-result" => WithTool("After tool result", ToolNameFor(flowEvent)),
        "ask-question" => "Question asked",
        "final" => "Final answer",
        _ => null,
    };

    private static string WithTool(string boundary, string? tool) =>
        string.IsNullOrWhiteSpace(tool) ? boundary : $"{boundary}: {tool}";

    // Both tool labels read "… <marker><Name>[(args)]", so the name is what follows the marker.
    private static string? ToolFromLabel(string label, string marker)
    {
        var start = label.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var name = label[(start + marker.Length)..];
        var paren = name.IndexOf('(');
        return (paren >= 0 ? name[..paren] : name).Trim();
    }

    /// <summary>
    /// Derives a short "response type" hint for the LLM receive arrow so the kind of the latest model
    /// response (a function/tool call, or final text) is visible without scrolling the transcript.
    /// </summary>
    public static string? ResponseHintFor(FlowEvent flowEvent) => flowEvent.Kind switch
    {
        "tool-call" => "\U0001F527 function call",
        "llm-response" => ResponseKind(flowEvent.Data),
        _ => null,
    };

    /// <summary>
    /// Names what a round-trip's response actually carried. Every round-trip emits a response, so the
    /// one that asked for a tool must not be labelled as text; a response carrying both is reported as a
    /// function call, because that is what sends the agent round the loop again.
    /// </summary>
    private static string? ResponseKind(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return "\U0001F4AC text";
        }

        try
        {
            using var doc = JsonDocument.Parse(PromptSignatureBuilder.ToStrictJson(data));
            var root = doc.RootElement;

            if (root.TryGetProperty("toolCalls", out var calls)
                && calls.ValueKind == JsonValueKind.Array
                && calls.GetArrayLength() > 0)
            {
                return calls.GetArrayLength() == 1
                    ? "\U0001F527 function call"
                    : $"\U0001F527 {calls.GetArrayLength()} function calls";
            }

            var hasText = root.TryGetProperty("text", out var text)
                && text.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(text.GetString());

            return hasText ? "\U0001F4AC text" : null;
        }
        catch (JsonException)
        {
            return "\U0001F4AC text";
        }
    }

    /// <summary>
    /// True for events whose payload is conversation content rather than structural scaffolding.
    /// Excludes "llm-request" (system prompt + tool schemas) and "tool-call" (the model's tool request,
    /// an agent action) — both are represented by the coloured boxes.
    /// </summary>
    public static bool IsContentEvent(FlowEvent e) => e.Kind switch
    {
        "received" or "tool-result" or "llm-response" or "final" or "error" => true,
        _ => false,
    };

    /// <summary>
    /// Maps a flow event to a short context chip (what got appended to the model's context) and the
    /// party that contributed it: "app" (application/harness), "agent" (the model/persona) or "user".
    /// </summary>
    public static (string Label, string Source) ContextChip(FlowEvent e) => e.Kind switch
    {
        "received" => ("User message", "user"),
        "llm-request" => ("System prompt + tools", "app"),
        "tool-call" => ("Assistant: tool call", "agent"),
        "tool-result" => ("Tool result", "app"),
        "llm-response" => ("Assistant message", "agent"),
        "final" => ("Final answer", "agent"),
        "error" => ("Error", "app"),
        _ => (e.Kind, "app"),
    };

    /// <summary>
    /// A short, single-line preview of the actual content an event contributed, so the Context stack
    /// shows what flowed into the model — not just its category. Falls back to the detail/label.
    /// </summary>
    public static string ContextPreview(FlowEvent e)
    {
        var text = !string.IsNullOrWhiteSpace(e.Data) ? e.Data
            : !string.IsNullOrWhiteSpace(e.Detail) ? e.Detail
            : string.Empty;

        return TruncatePreview(text);
    }

    /// <summary>Collapses whitespace and caps a string to a single short line for a Context chip preview.</summary>
    public static string TruncatePreview(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        text = WhitespaceRegex().Replace(text, " ").Trim();
        return text.Length > 90 ? text[..90] + "…" : text;
    }
}
