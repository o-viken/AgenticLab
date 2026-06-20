using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace TheSeries.AiService.Application;

/// <summary>
/// A chat-client middleware that records each LLM round-trip (the messages and tool definitions sent, and
/// the response returned) into the active <see cref="FlowCaptureScope"/>. When no scope is active it is a
/// transparent pass-through, so the shared singleton client only captures during a traced flow run.
/// </summary>
/// <param name="inner">The next client in the pipeline.</param>
public sealed class CapturingChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        // Render <, >, & and other characters literally instead of as \u003C escapes so the
        // captured system prompt and messages stay human-readable in the flow visualizer.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var turn = FlowCaptureScope.Current?.AddTurn(
            RenderRequest(messages, options),
            SummarizeRequest(messages, options));
        var text = new System.Text.StringBuilder();
        var toolCalls = new List<object>();

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            if (turn is not null)
            {
                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case TextContent t when !string.IsNullOrEmpty(t.Text):
                            text.Append(t.Text);
                            break;
                        case FunctionCallContent call:
                            toolCalls.Add(new { name = call.Name, arguments = call.Arguments });
                            break;
                    }
                }
            }

            yield return update;
        }

        if (turn is not null)
        {
            turn.ResponseData = RenderResponse(text.ToString(), toolCalls);
        }
    }

    private static string RenderRequest(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var payload = new
        {
            instructions = string.IsNullOrWhiteSpace(options?.Instructions) ? null : options!.Instructions,
            messages = messages.Select(m => new
            {
                role = m.Role.Value,
                contents = m.Contents.Select(RenderContent),
            }),
            tools = options?.Tools?.OfType<AIFunction>().Select(f => new
            {
                name = f.Name,
                description = f.Description,
                parameters = f.JsonSchema,
            }),
        };

        return Serialize(payload);
    }

    /// <summary>
    /// Builds a short, single-line summary of a request: the message breakdown by role, the tools
    /// offered to the model, and a preview of the newest message the harness is sending.
    /// </summary>
    private static string SummarizeRequest(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var list = messages.ToList();

        var counts = list
            .GroupBy(m => m.Role.Value)
            .Select(g => $"{g.Count()} {g.Key}")
            .ToList();

        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(options?.Instructions))
        {
            parts.Add($"system prompt: {Preview(options!.Instructions!, 80)}");
        }

        parts.Add($"{list.Count} message{(list.Count == 1 ? "" : "s")} ({string.Join(", ", counts)})");

        var tools = options?.Tools?.OfType<AIFunction>().Select(f => f.Name).ToList();
        if (tools is { Count: > 0 })
        {
            parts.Add($"tools: {string.Join(", ", tools)}");
        }

        var newest = list.LastOrDefault(m => !string.IsNullOrWhiteSpace(m.Text));
        if (newest is not null)
        {
            parts.Add($"newest [{newest.Role.Value}]: {Preview(newest.Text)}");
        }

        return string.Join(" · ", parts);
    }

    private static string Preview(string value, int max = 120)
    {
        var collapsed = value.ReplaceLineEndings(" ").Trim();
        return collapsed.Length <= max ? collapsed : collapsed[..max] + "…";
    }

    private static string RenderResponse(string text, IReadOnlyList<object> toolCalls)
    {
        var payload = new
        {
            text = string.IsNullOrEmpty(text) ? null : text,
            toolCalls = toolCalls.Count == 0 ? null : toolCalls,
        };

        return Serialize(payload);
    }

    /// <summary>
    /// Serializes a captured payload to indented JSON, then unescapes the <c>\n</c>/<c>\r</c> escape
    /// sequences inside string values into real line breaks. The result is display-only (no longer
    /// strict JSON), so multi-line content like the system prompt renders on actual lines in the UI.
    /// </summary>
    private static string Serialize(object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return json.Replace("\\r\\n", "\n").Replace("\\n", "\n").Replace("\\r", "\n");
    }

    private static object RenderContent(AIContent content) => content switch
    {
        TextContent t => new { type = "text", text = t.Text },
        FunctionCallContent c => new { type = "toolCall", name = c.Name, arguments = (object?)c.Arguments },
        FunctionResultContent r => new { type = "toolResult", callId = r.CallId, result = r.Result?.ToString() },
        _ => new { type = content.GetType().Name },
    };
}
