namespace AgenticLab.Web.Flow;

/// <summary>
/// Groups the flat stream of captured <see cref="FlowEvent"/>s into the shape the Execution explorer
/// reads: conversation exchanges, each holding its model round-trips, each holding the stages captured
/// for it. Pure and read-only — it never starts, advances or replays real execution, it only re-reads
/// what a run already recorded.
/// </summary>
internal static class ExecutionReplayBuilder
{
    /// <summary>
    /// Builds Context through the selected stage in playback order, excluding later stages and exchanges.
    /// The size uses the existing prompt-signature calculation on that prefix only; before its first
    /// captured request the size is unknown rather than borrowed from a later request.
    /// </summary>
    public static ContextSnapshot ContextAt(
        IReadOnlyList<ExecutionExchange> exchanges, string exchangeId, int? sequence)
    {
        var selected = exchanges.FirstOrDefault(exchange => exchange.Id == exchangeId);
        if (selected is null)
        {
            return ContextSnapshot.Empty;
        }

        var history = new List<ContextEntry>();
        foreach (var earlier in exchanges.TakeWhile(exchange => exchange.Id != exchangeId))
        {
            history.Add(new("User message", "user", FlowEventMapping.TruncatePreview(earlier.Message), History: true));
            if (!string.IsNullOrWhiteSpace(earlier.Error))
            {
                history.Add(new("Error", "app", FlowEventMapping.TruncatePreview(earlier.Error), History: true));
            }
            else if (!string.IsNullOrWhiteSpace(earlier.Reply))
            {
                history.Add(new("Final answer", "agent", FlowEventMapping.TruncatePreview(earlier.Reply), History: true));
            }
        }

        var prefix = PrefixThrough(selected, sequence);
        var current = new List<ContextEntry>();
        foreach (var stage in prefix.Where(FlowEventMapping.IsContentEvent))
        {
            var chip = FlowEventMapping.ContextChip(stage);
            var preview = stage.Kind == "received"
                ? FlowEventMapping.TruncatePreview(selected.Message)
                : FlowEventMapping.ContextPreview(stage);
            current.Add(new(chip.Label, chip.Source, preview, Turn: stage.Turn > 0 ? stage.Turn : null));
        }

        var signature = PromptSignatureBuilder.Build([(selected.Message, prefix)]);
        return new(history, current, signature.HasCurrent ? signature.CurrentChars : null);
    }

    /// <summary>
    /// Builds Comparison and Delta from earlier exchanges and the selected exchange's causal prefix.
    /// Returns an empty signature before the selected exchange has a captured request, so a prior
    /// exchange cannot be mistaken for the current one.
    /// </summary>
    public static PromptSignatureView SignatureAt(
        IReadOnlyList<ExecutionExchange> exchanges, string exchangeId, int? sequence)
    {
        var selected = exchanges.FirstOrDefault(exchange => exchange.Id == exchangeId);
        if (selected is null)
        {
            return PromptSignatureView.Empty;
        }

        var prefix = PrefixThrough(selected, sequence);
        if (!prefix.Any(stage => stage.Kind == "llm-request" && !string.IsNullOrWhiteSpace(stage.Data)))
        {
            return PromptSignatureView.Empty;
        }

        var visible = exchanges.TakeWhile(exchange => exchange.Id != exchangeId)
            .Select(exchange => (Label: exchange.Message, Events: exchange.Stages))
            .ToList();
        visible.Add((selected.Message, prefix));
        return PromptSignatureBuilder.Build(visible);
    }

    internal static IReadOnlyList<FlowEvent> PrefixThrough(ExecutionExchange exchange, int? sequence)
    {
        var stageIndex = exchange.Stages.ToList().FindIndex(stage => stage.Sequence == sequence);
        return exchange.Stages.Take(stageIndex + 1).ToArray();
    }

    /// <summary>
    /// Builds the exchange list for the conversation.
    /// </summary>
    /// <param name="exchanges">Every exchange, oldest first: the archived ones plus the current run.</param>
    /// <param name="liveIndex">The index of the exchange still executing, or -1 when none is.</param>
    /// <param name="numberOffset">Number of older exchanges evicted from local replay history.</param>
    public static IReadOnlyList<ExecutionExchange> Build(IReadOnlyList<ConversationTurn> exchanges, int liveIndex, int numberOffset = 0)
    {
        var built = new List<ExecutionExchange>(exchanges.Count);
        for (var i = 0; i < exchanges.Count; i++)
        {
            built.Add(BuildOne(exchanges[i], numberOffset + i + 1, i == liveIndex));
        }

        return built;
    }

    private static ExecutionExchange BuildOne(ConversationTurn exchange, int number, bool live)
    {
        var intake = new List<FlowEvent>();
        var outcome = new List<FlowEvent>();
        var byTurn = new SortedDictionary<int, List<FlowEvent>>();

        foreach (var e in exchange.Events)
        {
            // Delivery and failure are outcomes of the exchange, not steps of the round-trip they land in.
            if (e.Kind is "final" or "error")
            {
                outcome.Add(e);
            }
            else if (e.Turn <= 0)
            {
                intake.Add(e);
            }
            else
            {
                if (!byTurn.TryGetValue(e.Turn, out var stages))
                {
                    stages = new List<FlowEvent>();
                    byTurn[e.Turn] = stages;
                }

                stages.Add(e);
            }
        }

        var turns = new List<ExecutionTurn>(byTurn.Count);
        foreach (var (turnNumber, stages) in byTurn)
        {
            stages.Sort(CompareStages);
            turns.Add(new ExecutionTurn(turnNumber, stages));
        }

        var flattened = new List<FlowEvent>(exchange.Events.Count);
        flattened.AddRange(intake);
        foreach (var turn in turns)
        {
            flattened.AddRange(turn.Stages);
        }

        flattened.AddRange(outcome);

        return new ExecutionExchange(
            exchange.Id,
            number,
            exchange.Message,
            exchange.Agent,
            exchange.Vendor,
            exchange.Workspace,
            exchange.Reply,
            exchange.Error,
            Status(exchange, outcome, live),
            intake,
            turns,
            outcome,
            flattened,
            exchange.A2AAgents);
    }

    // A round-trip reads request → response → the tool calls that response asked for → their results.
    // The capture emits the tool calls while the response is still streaming, so rank before sequence.
    private static int CompareStages(FlowEvent a, FlowEvent b)
    {
        var rank = Rank(a).CompareTo(Rank(b));
        return rank != 0 ? rank : a.Sequence.CompareTo(b.Sequence);
    }

    private static int Rank(FlowEvent e) => e.Kind switch
    {
        "llm-request" => 0,
        "llm-response" => 1,
        _ => 2,
    };

    private static ExchangeStatus Status(ConversationTurn exchange, IReadOnlyList<FlowEvent> outcome, bool live)
    {
        if (!string.IsNullOrWhiteSpace(exchange.Error))
        {
            return ExchangeStatus.Failed;
        }

        if (live)
        {
            return ExchangeStatus.Running;
        }

        return outcome.Any(e => e.Kind == "final") ? ExchangeStatus.Completed : ExchangeStatus.Stopped;
    }

    /// <summary>Names a stage after what actually happened at it, rather than the diagram's arrow label.</summary>
    public static string StageTitle(FlowEvent e) => e.Kind switch
    {
        "received" => "Message received",
        "llm-request" => "Request sent to model",
        "llm-response" => "Model response",
        "tool-call" => WithTool("Tool call", e),
        "tool-result" => WithTool("Tool result", e),
        "ask-question" => "Question for you",
        "final" => "Answer delivered",
        "error" => "Error",
        _ => e.Label,
    };

    /// <summary>Who handed the data to whom at this stage, shown under the stage title.</summary>
    public static string StageRoute(FlowEvent e) => e.Kind switch
    {
        "received" => "User → Harness",
        "llm-request" => "Harness → Model",
        "llm-response" => "Model → Harness",
        "tool-call" => "Model → Tool",
        "tool-result" => "Tool → Harness",
        "ask-question" => "Agent → User",
        "final" => "Harness → User",
        "error" => "Harness → User",
        _ => e.Label,
    };

    private static string WithTool(string title, FlowEvent e) =>
        FlowEventMapping.StageToolName(e) is { Length: > 0 } tool ? $"{title}: {tool}" : title;
}
