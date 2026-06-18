using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Agents.AI;
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
/// <param name="Turn">The 1-based LLM round-trip this step belongs to; 0 before the first round-trip.</param>
/// <param name="Data">
/// Optional full, untruncated payload revealed on demand in the UI: the data sent to the LLM for an
/// <c>llm-request</c>, the model's response for an <c>llm-response</c>/<c>final</c>, or the raw tool
/// arguments/result for a <c>tool-call</c>/<c>tool-result</c>.
/// </param>
public sealed record FlowEvent(int Sequence, string Kind, string Label, string? Detail = null, int Turn = 0, string? Data = null);

/// <summary>
/// Runs an agent and projects its real execution (the LLM round-trips and tool invocations) into an
/// ordered stream of <see cref="FlowEvent"/>s. Each step is gated on a <see cref="FlowSession"/> so the
/// client can pace, pause, single-step or stop the <em>actual</em> backend execution — keeping the
/// animation in sync with the agent (and its telemetry).
/// </summary>
/// <param name="catalog">The catalog used to resolve the requested agent.</param>
/// <param name="registry">The registry the run's <see cref="FlowSession"/> is removed from when it ends.</param>
/// <param name="conversations">The store holding each conversation's thread so the run can continue prior turns.</param>
/// <param name="skills">Discovers the active workspace's skills so their catalogue can be injected into the run.</param>
public sealed class FlowTracer(AgentCatalog catalog, FlowControlRegistry registry, ConversationStore conversations, SkillLoader skills)
{
    /// <summary>
    /// Streams the steps of running <paramref name="message"/> through the selected agent, pacing each
    /// step on <paramref name="session"/>.
    /// </summary>
    /// <param name="message">The user's message.</param>
    /// <param name="agentName">The agent to use, or null/blank for the default.</param>
    /// <param name="conversationId">The conversation to continue, so the run remembers prior turns.</param>
    /// <param name="workspace">The workspace path for agents that require one; null/blank otherwise.</param>
    /// <param name="disabledTools">The names of the agent's tools to hide from the model for this run; null/empty to offer them all.</param>
    /// <param name="session">The control session that paces, pauses and stops the run.</param>
    /// <param name="cancellationToken">A token to cancel the run.</param>
    /// <returns>An ordered, lazily-produced sequence of flow events.</returns>
    public async IAsyncEnumerable<FlowEvent> StreamAsync(
        string message,
        string? agentName,
        string conversationId,
        string? workspace,
        IReadOnlyList<string>? disabledTools,
        FlowSession session,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sequence = 0;
        var currentTurn = 0;
        FlowEvent Step(string kind, string label, string? detail = null, string? data = null) =>
            new(++sequence, kind, label, detail, currentTurn, data);

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

        if (catalog.RequiresWorkspace(resolvedName) && string.IsNullOrWhiteSpace(workspace))
        {
            yield return Step("error", $"Agent '{resolvedName}' requires a workspace", "Set a workspace path before running this agent.");
            registry.Remove(session.Id);
            yield break;
        }

        using var workspaceScope = catalog.RequiresWorkspace(resolvedName)
            ? OpenWorkspace(workspace)
            : null;
        if (catalog.RequiresWorkspace(resolvedName) && workspaceScope is null)
        {
            yield return Step("error", "Invalid workspace", $"Workspace path '{workspace}' is not an existing directory.");
            registry.Remove(session.Id);
            yield break;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.StopToken);
        var token = linked.Token;

        try
        {
            await session.WaitForStepAsync(token);
            yield return Step("received", "Harness received the message", $"Agent: {resolvedName}");

            // Capture the real LLM round-trips (request payloads and responses) for this run only.
            using var capture = FlowCaptureScope.Begin();

            // Hide any tools the caller disabled for this run so the model is only offered the remaining subset.
            using var toolScope = disabledTools is { Count: > 0 }
                ? ToolFilterScope.Begin(disabledTools)
                : null;

            var finalText = new StringBuilder();
            var callNames = new Dictionary<string, string>();
            var emittedTurns = 0;

            // Enumerate manually so we can re-assert the capture scope right before each agent advance.
            // The scope lives in an AsyncLocal that is reset whenever this iterator resumes after a yield,
            // so without re-activating immediately before MoveNextAsync only the first LLM round-trip is
            // recorded and the later turns (the calls made after each tool result) are silently lost.
            var agentSession = await conversations.GetOrCreateAsync(conversationId, agent, token);

            // Surface the workspace's skills (names + descriptions) to the agent for this run only. The
            // scope's AsyncLocal is reset by the earlier yields, so re-assert it before reading skills.
            workspaceScope?.Activate();
            var runOptions = catalog.SupportsSkills(resolvedName) ? BuildSkillRunOptions() : null;

            await using var updates = agent.RunStreamingAsync(message, agentSession, runOptions, token)
                .GetAsyncEnumerator(token);

            while (true)
            {
                capture.Activate();
                workspaceScope?.Activate();
                toolScope?.Activate();
                if (!await updates.MoveNextAsync())
                {
                    break;
                }

                var update = updates.Current;

                // Surface an llm-request for every round-trip captured since the last update. The first
                // turn carries the prompt + tools; later turns carry the tool results fed back to the model.
                var turns = capture.Turns;
                while (emittedTurns < turns.Count)
                {
                    var turn = turns[emittedTurns++];
                    currentTurn = turn.TurnNumber;
                    await session.WaitForStepAsync(token);
                    yield return Step("llm-request", DescribeTurn(turn.TurnNumber), turn.RequestSummary, turn.RequestData);
                }

                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case FunctionCallContent call:
                            callNames[call.CallId] = call.Name;
                            await session.WaitForStepAsync(token);
                            yield return Step(
                                "tool-call",
                                $"LLM → Tool: {DescribeCall(call.Name, call.Arguments)}",
                                DescribeArguments(call.Arguments) ?? "(no arguments)",
                                FullCall(call.Name, call.Arguments));
                            break;

                        case FunctionResultContent result:
                            var resultText = result.Result?.ToString();
                            var toolName = callNames.GetValueOrDefault(result.CallId);
                            var resultLabel = toolName is null ? "Tool → Harness" : $"Tool → Harness: {toolName}";
                            await session.WaitForStepAsync(token);
                            yield return Step("tool-result", resultLabel, Truncate(resultText), resultText);
                            break;

                        case TextContent text when !string.IsNullOrEmpty(text.Text):
                            finalText.Append(text.Text);
                            break;
                    }
                }
            }

            // Emit any round-trip captured right at the end of the stream (defensive; normally none remain).
            var finalTurns = capture.Turns;
            while (emittedTurns < finalTurns.Count)
            {
                var turn = finalTurns[emittedTurns++];
                currentTurn = turn.TurnNumber;
                await session.WaitForStepAsync(token);
                yield return Step("llm-request", DescribeTurn(turn.TurnNumber), turn.RequestSummary, turn.RequestData);
            }

            var lastResponse = finalTurns.Count > 0 ? finalTurns[^1].ResponseData : null;
            var answer = finalText.ToString();
            var responseDetail = string.IsNullOrWhiteSpace(answer)
                ? "The model returned its final answer."
                : $"Answer: {Truncate(answer)}";
            await session.WaitForStepAsync(token);
            yield return Step("llm-response", "LLM → Harness", responseDetail, lastResponse);

            await session.WaitForStepAsync(token);
            yield return Step("final", "Harness → Client", answer, answer);
        }
        finally
        {
            registry.Remove(session.Id);
        }
    }

    // Opens a workspace scope for a path, returning null when the path is missing or not a directory.
    private static WorkspaceScope? OpenWorkspace(string? path)
    {
        try
        {
            return WorkspaceScope.Begin(path);
        }
        catch (Exception ex) when (ex is ArgumentException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    // Builds run options that append the active workspace's skill catalogue to the agent's instructions
    // for this run, or null when the workspace declares no skills. Assumes the workspace scope is active.
    private AgentRunOptions? BuildSkillRunOptions()
    {
        var block = skills.BuildContextBlock();
        return string.IsNullOrEmpty(block)
            ? null
            : new ChatClientAgentRunOptions(new ChatOptions { Instructions = block });
    }

    private static string DescribeTurn(int turnNumber) =>
        turnNumber <= 1 ? "Harness → LLM" : $"Harness → LLM (turn {turnNumber})";

    // A compact call signature for a step label, e.g. Calculate(expression: "2 + 2").
    private static string DescribeCall(string name, IDictionary<string, object?>? arguments) =>
        $"{name}({DescribeArguments(arguments)})";

    // The full, untruncated call rendered as name + one argument per line for the expandable data panel.
    private static string FullCall(string name, IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return $"{name}()";
        }

        var lines = arguments.Select(kvp => $"  {kvp.Key}: {kvp.Value}");
        return $"{name}(" + Environment.NewLine + string.Join(Environment.NewLine, lines) + Environment.NewLine + ")";
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
