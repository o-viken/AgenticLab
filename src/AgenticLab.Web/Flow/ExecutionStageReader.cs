using System.Text;
using System.Text.Json;

namespace AgenticLab.Web.Flow;

/// <summary>
/// One readable block of a captured stage, e.g. the system prompt, a single message the harness re-sent,
/// or a tool call the model asked for.
/// </summary>
/// <param name="Title">What the block is.</param>
/// <param name="Meta">Optional qualifier, e.g. the call id a result answers.</param>
/// <param name="Body">The captured content itself.</param>
/// <param name="Source">Who contributed it: <c>app</c>, <c>agent</c>, <c>user</c> or <c>tool</c>.</param>
internal sealed record StageSection(string Title, string? Meta, string Body, string Source);

/// <summary>
/// Turns a captured stage's payload into readable blocks for the Execution explorer's Data view. It only
/// re-reads what the run recorded; anything the capture does not hold is reported as unavailable rather
/// than reconstructed.
/// </summary>
internal static class ExecutionStageReader
{
    private const string Missing = "(not captured)";

    /// <summary>Breaks a stage's captured payload into the blocks shown in the Data view.</summary>
    public static IReadOnlyList<StageSection> Sections(FlowEvent stage)
    {
        var data = stage.Data;
        if (string.IsNullOrWhiteSpace(data))
        {
            return [new StageSection(ExecutionReplayBuilder.StageTitle(stage), null, stage.Detail ?? Missing, SourceOf(stage))];
        }

        try
        {
            return stage.Kind switch
            {
                "llm-request" => RequestSections(data),
                "llm-response" => ResponseSections(data),
                _ => [new StageSection(BodyTitle(stage), null, data, SourceOf(stage))],
            };
        }
        catch (JsonException)
        {
            // The capture is display-formatted, so fall back to showing it verbatim rather than guessing.
            return [new StageSection(BodyTitle(stage), null, data, SourceOf(stage))];
        }
    }

    private static IReadOnlyList<StageSection> RequestSections(string data)
    {
        using var doc = JsonDocument.Parse(PromptSignatureBuilder.ToStrictJson(data));
        var root = doc.RootElement;
        var sections = new List<StageSection>();

        if (root.TryGetProperty("instructions", out var instructions) && instructions.ValueKind == JsonValueKind.String)
        {
            sections.Add(new StageSection("System prompt", "harness + persona", instructions.GetString() ?? string.Empty, "app"));
        }

        if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var message in messages.EnumerateArray())
            {
                index++;
                var role = message.TryGetProperty("role", out var r) ? r.GetString() ?? "message" : "message";
                sections.Add(new StageSection($"{index}. {role}", "re-sent in this request", MessageBody(message), RoleSource(role)));
            }
        }

        if (root.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Array)
        {
            var names = new List<string>();
            foreach (var tool in tools.EnumerateArray())
            {
                var name = tool.TryGetProperty("name", out var n) ? n.GetString() : null;
                var description = tool.TryGetProperty("description", out var d) ? d.GetString() : null;
                names.Add(string.IsNullOrWhiteSpace(description) ? $"{name}" : $"{name} — {description}");
            }

            if (names.Count > 0)
            {
                sections.Add(new StageSection($"Tools offered ({names.Count})", "the model may call these", string.Join("\n", names), "app"));
            }
        }

        return sections;
    }

    private static IReadOnlyList<StageSection> ResponseSections(string data)
    {
        using var doc = JsonDocument.Parse(PromptSignatureBuilder.ToStrictJson(data));
        var root = doc.RootElement;
        var sections = new List<StageSection>();

        var text = root.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            sections.Add(new StageSection("Text returned", null, text, "agent"));
        }

        if (root.TryGetProperty("toolCalls", out var calls) && calls.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var call in calls.EnumerateArray())
            {
                index++;
                var name = call.TryGetProperty("name", out var n) ? n.GetString() : "tool";
                var arguments = call.TryGetProperty("arguments", out var a) ? Render(a) : Missing;
                sections.Add(new StageSection($"Tool call {index}: {name}", "the model asked the harness to run this", arguments, "agent"));
            }
        }

        if (sections.Count == 0)
        {
            sections.Add(new StageSection("Response", null, "The model returned no text and no tool calls.", "agent"));
        }

        return sections;
    }

    private static string MessageBody(JsonElement message)
    {
        if (!message.TryGetProperty("contents", out var contents) || contents.ValueKind != JsonValueKind.Array)
        {
            return Missing;
        }

        var body = new StringBuilder();
        foreach (var content in contents.EnumerateArray())
        {
            var type = content.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (body.Length > 0)
            {
                body.AppendLine();
            }

            switch (type)
            {
                case "text":
                    body.Append(content.TryGetProperty("text", out var text) ? text.GetString() : Missing);
                    break;
                case "toolCall":
                    var name = content.TryGetProperty("name", out var n) ? n.GetString() : "tool";
                    var args = content.TryGetProperty("arguments", out var a) ? Render(a) : Missing;
                    body.Append($"→ calls {name} with {args}");
                    break;
                case "toolResult":
                    var callId = content.TryGetProperty("callId", out var c) ? c.GetString() : null;
                    var result = content.TryGetProperty("result", out var r) ? r.GetString() : Missing;
                    body.Append(callId is null ? $"← result: {result}" : $"← result for {callId}: {result}");
                    break;
                default:
                    body.Append($"({type ?? "unknown content"} — not captured in readable form)");
                    break;
            }
        }

        return body.Length == 0 ? Missing : body.ToString();
    }

    private static string Render(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Null or JsonValueKind.Undefined => Missing,
        _ => element.ToString(),
    };

    private static string BodyTitle(FlowEvent stage) => stage.Kind switch
    {
        "received" => "Message from the user",
        "tool-call" => "Call the model requested",
        "tool-result" => "What the tool returned",
        "ask-question" => "Question put to the user",
        "final" => "Answer delivered to the user",
        "error" => "Failure",
        _ => ExecutionReplayBuilder.StageTitle(stage),
    };

    private static string SourceOf(FlowEvent stage) => stage.Kind switch
    {
        "received" => "user",
        "llm-response" or "tool-call" or "ask-question" => "agent",
        "tool-result" => "tool",
        _ => "app",
    };

    private static string RoleSource(string role) => role.ToLowerInvariant() switch
    {
        "user" => "user",
        "assistant" => "agent",
        "tool" => "tool",
        _ => "app",
    };
}
