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
/// ordered stream of <see cref="FlowEvent"/>s. Each step is gated on a <see cref="FlowSession"/> so the
/// client can pace, pause, single-step or stop the <em>actual</em> backend execution — keeping the
/// animation in sync with the agent (and its telemetry).
/// </summary>
/// <param name="catalog">The catalog used to resolve the requested agent.</param>
/// <param name="registry">The registry the run's <see cref="FlowSession"/> is removed from when it ends.</param>
public sealed class FlowTracer(AgentCatalog catalog, FlowControlRegistry registry)
{
    /// <summary>
    /// Streams the steps of running <paramref name="message"/> through the selected agent, pacing each
    /// step on <paramref name="session"/>.
    /// </summary>
    /// <param name="message">The user's message.</param>
    /// <param name="agentName">The agent to use, or null/blank for the default.</param>
    /// <param name="session">The control session that paces, pauses and stops the run.</param>
    /// <param name="cancellationToken">A token to cancel the run.</param>
    /// <returns>An ordered, lazily-produced sequence of flow events.</returns>
    public async IAsyncEnumerable<FlowEvent> StreamAsync(
        string message,
        string? agentName,
        FlowSession session,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sequence = 0;
        FlowEvent Step(string kind, string label, string? detail = null) => new(++sequence, kind, label, detail);

        if (string.IsNullOrWhiteSpace(message))
        {
            yield return Step("error", "Empty message", "The message must not be empty.");
            registry.Remove(session.Id);
            yield break;
        }

        if (!catalog.TryResolve(agentName, out var agent, out var resolvedName))
        {
            yield return Step("error", $"Unknown agent '{agentName}'", "Call GET /agents for the available names.");
            registry.Remove(session.Id);
            yield break;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.StopToken);
        var token = linked.Token;

        try
        {
            await session.WaitForStepAsync(token);
            yield return Step("received", "Harness received the message", $"Agent: {resolvedName}");

            await session.WaitForStepAsync(token);
            yield return Step("llm-request", "Harness → LLM", "Sending the prompt and tool definitions to the model.");

            var finalText = new StringBuilder();

            await foreach (var update in agent.RunStreamingAsync(message, cancellationToken: token))
            {
                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case FunctionCallContent call:
                            await session.WaitForStepAsync(token);
                            yield return Step("tool-call", $"LLM → Tool: {call.Name}", DescribeArguments(call.Arguments));
                            break;

                        case FunctionResultContent result:
                            await session.WaitForStepAsync(token);
                            yield return Step("tool-result", "Tool → Harness", Truncate(result.Result?.ToString()));

                            await session.WaitForStepAsync(token);
                            yield return Step("llm-request", "Harness → LLM", "Sending the tool result back to the model.");
                            break;

                        case TextContent text when !string.IsNullOrEmpty(text.Text):
                            finalText.Append(text.Text);
                            break;
                    }
                }
            }

            await session.WaitForStepAsync(token);
            yield return Step("llm-response", "LLM → Harness", "The model returned its final answer.");

            await session.WaitForStepAsync(token);
            yield return Step("final", "Harness → Client", finalText.ToString());
        }
        finally
        {
            registry.Remove(session.Id);
        }
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
