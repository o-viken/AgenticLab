using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;

namespace TheSeries.AiService.Agents;

/// <summary>
/// A single observable step in an agent run, used to animate the data flow in the web UI
/// (User → Client → Harness → Tools → LLM and back).
/// </summary>
/// <param name="Sequence">A monotonically increasing index, starting at 1.</param>
/// <param name="Kind">
/// The step category: <c>received</c>, <c>llm-request</c>, <c>tool-call</c>, <c>tool-result</c>,
/// <c>llm-response</c>, <c>final</c> or <c>error</c>.
/// </param>
/// <param name="Label">A short, human-readable description of the step.</param>
/// <param name="Detail">Optional extra context, e.g. tool arguments or the reply text.</param>
public sealed record FlowEvent(int Sequence, string Kind, string Label, string? Detail = null);

/// <summary>
/// Runs an agent and projects its real execution (the LLM round-trips and tool invocations) into an
/// ordered stream of <see cref="FlowEvent"/>s. An optional per-step delay slows the stream down so the
/// flow can be observed live in the UI.
/// </summary>
/// <param name="catalog">The catalog used to resolve the requested agent.</param>
public sealed class FlowTracer(AgentCatalog catalog)
{
    /// <summary>
    /// Streams the steps of running <paramref name="message"/> through the selected agent.
    /// </summary>
    /// <param name="message">The user's message.</param>
    /// <param name="agentName">The agent to use, or null/blank for the default.</param>
    /// <param name="stepDelayMs">An artificial delay applied after each emitted step, in milliseconds.</param>
    /// <param name="cancellationToken">A token to cancel the run.</param>
    /// <returns>An ordered, lazily-produced sequence of flow events.</returns>
    public async IAsyncEnumerable<FlowEvent> StreamAsync(
        string message,
        string? agentName,
        int stepDelayMs,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sequence = 0;
        FlowEvent Step(string kind, string label, string? detail = null) => new(++sequence, kind, label, detail);

        async Task PaceAsync()
        {
            if (stepDelayMs > 0)
            {
                await Task.Delay(stepDelayMs, cancellationToken);
            }
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            yield return Step("error", "Empty message", "The message must not be empty.");
            yield break;
        }

        if (!catalog.TryResolve(agentName, out var agent, out var resolvedName))
        {
            yield return Step("error", $"Unknown agent '{agentName}'", "Call GET /agents for the available names.");
            yield break;
        }

        yield return Step("received", "Harness received the message", $"Agent: {resolvedName}");
        await PaceAsync();

        yield return Step("llm-request", "Harness → LLM", "Sending the prompt and tool definitions to the model.");
        await PaceAsync();

        var finalText = new StringBuilder();

        await foreach (var update in agent.RunStreamingAsync(message, cancellationToken: cancellationToken))
        {
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case FunctionCallContent call:
                        yield return Step("tool-call", $"LLM → Tool: {call.Name}", DescribeArguments(call.Arguments));
                        await PaceAsync();
                        break;

                    case FunctionResultContent result:
                        yield return Step("tool-result", "Tool → Harness", Truncate(result.Result?.ToString()));
                        await PaceAsync();
                        yield return Step("llm-request", "Harness → LLM", "Sending the tool result back to the model.");
                        await PaceAsync();
                        break;

                    case TextContent text when !string.IsNullOrEmpty(text.Text):
                        finalText.Append(text.Text);
                        break;
                }
            }
        }

        yield return Step("llm-response", "LLM → Harness", "The model returned its final answer.");
        await PaceAsync();

        yield return Step("final", "Harness → Client", finalText.ToString());
    }

    private static string? DescribeArguments(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return null;
        }

        return string.Join(", ", arguments.Select(kvp => $"{kvp.Key}: {Truncate(kvp.Value?.ToString())}"));
    }

    private static string? Truncate(string? value, int max = 200)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var collapsed = value.ReplaceLineEndings(" ").Trim();
        return collapsed.Length <= max ? collapsed : collapsed[..max] + "…";
    }
}
