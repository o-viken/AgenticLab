using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace TheSeries.AiService.Application;

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
public sealed record FlowEvent(int Sequence, string Kind, string Label, string? Detail = null, int Turn = 0, string? Data = null, string? CallId = null);

/// <summary>
/// Runs an agent and projects its real execution (the LLM round-trips and tool invocations) into an
/// ordered stream of <see cref="FlowEvent"/>s. Each step is gated on a <see cref="FlowSession"/> so the
/// client can pace, pause, single-step or stop the <em>actual</em> backend execution — keeping the
/// animation in sync with the agent (and its telemetry).
/// </summary>
/// <param name="catalog">The catalog used to resolve the requested agent.</param>
/// <param name="workspaceAgents">Resolves user-authored agents declared in the active workspace's agents/ folder.</param>
/// <param name="registry">The registry the run's <see cref="FlowSession"/> is removed from when it ends.</param>
/// <param name="conversations">The store holding each conversation's thread so the run can continue prior turns.</param>
/// <param name="skills">Discovers the active workspace's skills so their catalogue can be injected into the run.</param>
/// <param name="instructions">Discovers the active workspace's custom instructions so their content can be injected into the run.</param>
/// <param name="vendors">Resolves a brand/vendor key to a harness prompt that replaces the shared harness for the run.</param>
public sealed class FlowTracer(AgentCatalog catalog, WorkspaceAgentResolver workspaceAgents, FlowControlRegistry registry, ConversationStore conversations, SkillLoader skills, InstructionLoader instructions, VendorHarnessCatalog vendors)
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
    /// <param name="vendor">A brand/vendor key whose harness replaces the shared harness for this run; null/blank to keep the agent's own.</param>
    /// <param name="session">The control session that paces, pauses and stops the run.</param>
    /// <param name="cancellationToken">A token to cancel the run.</param>
    /// <returns>An ordered, lazily-produced sequence of flow events.</returns>
    public async IAsyncEnumerable<FlowEvent> StreamAsync(
        string message,
        string? agentName,
        string conversationId,
        string? workspace,
        IReadOnlyList<string>? disabledTools,
        IReadOnlyList<string>? disabledSkills,
        IReadOnlyList<string>? enabledInstructions,
        string? vendor,
        FlowSession session,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sequence = 0;
        var currentTurn = 0;
        FlowEvent Step(string kind, string label, string? detail = null, string? data = null, string? callId = null) =>
            new(++sequence, kind, label, detail, currentTurn, data, callId);

        if (string.IsNullOrWhiteSpace(message))
        {
            yield return Step("error", "Empty message", "The message must not be empty.");
            registry.Remove(session.Id);
            yield break;
        }

        // A built-in agent resolves from the catalog; an unknown name may be a user-authored agent in the
        // workspace's agents/ folder, which can only be discovered once the workspace scope is open.
        // A selected brand/vendor swaps in its harness prompt in place of the shared harness for this run.
        var harness = vendors.Resolve(vendor);
        var fromCatalog = catalog.TryResolve(agentName, harness, out var agent, out var resolvedName);
        var requiresWorkspace = !fromCatalog || catalog.RequiresWorkspace(resolvedName);
        var supportsSkills = fromCatalog && catalog.SupportsSkills(resolvedName);

        if (requiresWorkspace && string.IsNullOrWhiteSpace(workspace))
        {
            yield return fromCatalog
                ? Step("error", $"Agent '{resolvedName}' requires a workspace", "Set a workspace path before running this agent.")
                : Step("error", $"Unknown agent '{agentName}'", "Call GET /agents for the available names.");
            registry.Remove(session.Id);
            yield break;
        }

        using var workspaceScope = requiresWorkspace ? OpenWorkspace(workspace) : null;
        if (requiresWorkspace && workspaceScope is null)
        {
            yield return Step("error", "Invalid workspace", $"Workspace path '{workspace}' is not an existing directory.");
            registry.Remove(session.Id);
            yield break;
        }

        if (!fromCatalog)
        {
            if (!workspaceAgents.TryResolve(agentName, out agent, out var definition, harness))
            {
                yield return Step("error", $"Unknown agent '{agentName}'", "Call GET /agents for the available names.");
                registry.Remove(session.Id);
                yield break;
            }

            resolvedName = definition.Name;
            // Workspace-defined agents always support workspace skills (see WorkspaceDefinedAgent), so the
            // skill catalogue is injected for their runs regardless of the agent file's `skills` field.
            supportsSkills = true;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.StopToken);
        var token = linked.Token;

        try
        {
            await session.WaitForStepAsync(token);
            yield return Step("received", "Harness received the message", $"Agent: {resolvedName}");

            // Capture the real LLM round-trips (request payloads and responses) for this run only.
            using var capture = FlowCaptureScope.Begin();
            using var execution = new FlowExecutionScope(session);

            // Hide any tools the caller disabled for this run so the model is only offered the remaining subset.
            using var toolScope = disabledTools is { Count: > 0 }
                ? ToolFilterScope.Begin(disabledTools)
                : null;

            // Custom instructions are opt-in (default off): only enabled ones are injected. Skills default
            // on: the caller can disable a subset.
            using var instructionScope = InstructionFilterScope.Begin(enabledInstructions ?? Array.Empty<string>());
            using var skillScope = disabledSkills is { Count: > 0 }
                ? SkillFilterScope.Begin(disabledSkills)
                : null;

            // Lets an AskQuestion tool call pause the run and resume when the user answers (via /chat/control).
            using var userInput = UserInputScope.Begin();
            session.UserInput = userInput;

            var finalText = new StringBuilder();
            var callNames = new Dictionary<string, string>();
            var emittedTurns = 0;
            var emittedResponses = 0;

            // Surfaces each captured round-trip in causal order: the request the harness sent, then the
            // model's own response as soon as that round-trip completes. Turn 1 carries the prompt +
            // tools; later turns carry the tool results fed back to the model. Without the per-turn
            // response the UI would only ever see the last one, hiding the response that asked for a tool.
            async IAsyncEnumerable<FlowEvent> DrainCapturedAsync()
            {
                var turns = capture.Turns;
                while (true)
                {
                    // A response can only be surfaced once its own request has been shown.
                    if (emittedResponses < emittedTurns && turns[emittedResponses].ResponseData is { } response)
                    {
                        var completed = turns[emittedResponses++];
                        currentTurn = completed.TurnNumber;
                        await session.WaitForStepAsync(token);
                        yield return Step("llm-response", DescribeResponse(completed.TurnNumber), completed.ResponseSummary, response);
                        continue;
                    }

                    if (emittedTurns < turns.Count)
                    {
                        var started = turns[emittedTurns++];
                        currentTurn = started.TurnNumber;
                        await session.WaitForStepAsync(token);
                        yield return Step("llm-request", DescribeTurn(started.TurnNumber), started.RequestSummary, started.RequestData);
                        continue;
                    }

                    break;
                }
            }

            // Enumerate manually so we can re-assert the capture scope right before each agent advance.
            // The scope lives in an AsyncLocal that is reset whenever this iterator resumes after a yield,
            // so without re-activating immediately before MoveNextAsync only the first LLM round-trip is
            // recorded and the later turns (the calls made after each tool result) are silently lost.
            var agentSession = await conversations.GetOrCreateAsync(conversationId, agent, token);

            // Surface the workspace's custom instructions (opted in) and its skills (names +
            // descriptions, when the agent uses them) to the agent for this run only. The scope's
            // AsyncLocal is reset by the earlier yields, so re-assert them before reading them.
            workspaceScope?.Activate();
            instructionScope.Activate();
            skillScope?.Activate();
            var runOptions = BuildRunOptions(supportsSkills);

            await using var updates = agent.RunStreamingAsync(message, agentSession, runOptions, token)
                .GetAsyncEnumerator(token);

            while (true)
            {
                capture.Activate();
                workspaceScope?.Activate();
                toolScope?.Activate();
                instructionScope.Activate();
                skillScope?.Activate();
                userInput.Activate();
                execution.Activate();
                await foreach (var notice in execution.AdvanceAsync(updates, token))
                {
                    currentTurn = capture.Turns.Count;
                    yield return Step("breakpoint", notice.Paused ? "Paused at breakpoint" : "Breakpoint released",
                        notice.Kind, JsonSerializer.Serialize(notice));
                }

                if (!execution.HasUpdate)
                {
                    break;
                }

                var update = updates.Current;

                // Surface an llm-request for every round-trip captured since the last update, followed by
                // the response it produced once the model has finished returning it.
                await foreach (var step in DrainCapturedAsync())
                {
                    yield return step;
                }

                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case FunctionCallContent call:
                            callNames[call.CallId] = call.Name;
                            await session.WaitForStepAsync(token);
                            if (string.Equals(call.Name, "AskQuestion", StringComparison.OrdinalIgnoreCase))
                            {
                                // A blocking question: the tool now waits for the user's answer. Surface
                                // the question text so the UI can prompt for and submit a reply.
                                var question = QuestionText(call.Arguments);
                                yield return Step(
                                    "ask-question",
                                    "Agent → User: question",
                                    question,
                                    question,
                                    call.CallId);
                                break;
                            }

                            yield return Step(
                                "tool-call",
                                $"LLM → Tool: {DescribeCall(call.Name, call.Arguments)}",
                                DescribeArguments(call.Arguments) ?? "(no arguments)",
                                FullCall(call.Name, call.Arguments),
                                call.CallId);
                            break;

                        case FunctionResultContent result:
                            var resultText = result.Result?.ToString();
                            var toolName = callNames.GetValueOrDefault(result.CallId);
                            var resultLabel = toolName is null ? "Tool → Harness" : $"Tool → Harness: {toolName}";
                            await session.WaitForStepAsync(token);
                            yield return Step("tool-result", resultLabel, Truncate(resultText), resultText, result.CallId);
                            break;

                        case TextContent text when !string.IsNullOrEmpty(text.Text):
                            finalText.Append(text.Text);
                            break;
                    }
                }
            }

            // Flush the round-trip captured right at the end of the stream (the one that produced the answer).
            await foreach (var step in DrainCapturedAsync())
            {
                yield return step;
            }

            var answer = finalText.ToString();
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

    // Builds run options that append the active workspace's custom instructions (always, when present)
    // and its skill catalogue (only when the agent supports skills) to the agent's instructions for this
    // run, or null when neither is present. Assumes the workspace scope is active.
    private AgentRunOptions? BuildRunOptions(bool supportsSkills)
    {
        var instructionBlock = instructions.BuildContextBlock();
        var skillBlock = supportsSkills ? skills.BuildContextBlock() : null;
        var combined = string.Join("\n", new[] { instructionBlock, skillBlock }.Where(b => !string.IsNullOrEmpty(b)));
        return string.IsNullOrEmpty(combined)
            ? null
            : new ChatClientAgentRunOptions(new ChatOptions { Instructions = combined });
    }

    private static string DescribeTurn(int turnNumber) =>
        turnNumber <= 1 ? "Harness → LLM" : $"Harness → LLM (turn {turnNumber})";

    private static string DescribeResponse(int turnNumber) =>
        turnNumber <= 1 ? "LLM → Harness" : $"LLM → Harness (turn {turnNumber})";

    // Extracts the question text from an AskQuestion tool call's arguments.
    private static string QuestionText(IDictionary<string, object?>? arguments)
    {
        var question = arguments is not null && arguments.TryGetValue("question", out var value)
            ? value?.ToString()
            : null;
        return string.IsNullOrWhiteSpace(question) ? "(no question)" : question.Trim();
    }

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
