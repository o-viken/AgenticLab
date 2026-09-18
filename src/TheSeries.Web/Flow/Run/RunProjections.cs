namespace TheSeries.Web.Flow;

/// <summary>
/// The LINQ-heavy collections derived from the run's events and archived turns — the Context chips and
/// size, turn count, prompt signature, anatomy sizes, the simulated inference/embeddings views, the
/// Execution explorer's exchanges and the live A2A topology. Recomputed lazily, once per state change
/// (<see cref="Invalidate"/> is called from the controller's <c>BumpState</c>), rather than on each of
/// the many renders a run triggers.
/// </summary>
internal sealed class RunProjections(FlowRunController owner)
{
    private int _cacheVersion = -1;
    private IReadOnlyList<ContextEntry> _historyEntries = Array.Empty<ContextEntry>();
    private IReadOnlyList<ContextEntry> _currentEntries = Array.Empty<ContextEntry>();
    private int _contextSize;
    private int _totalTurns;
    private PromptSignatureView _promptSignature = PromptSignatureView.Empty;
    private AnatomySizes _anatomySizes = AnatomySizes.Empty;
    private InferenceView _inference = InferenceView.Empty;
    private EmbeddingsView _embeddings = EmbeddingsView.Empty;
    private IReadOnlyList<ExecutionExchange> _exchanges = Array.Empty<ExecutionExchange>();
    private ContextSnapshot _liveContext = ContextSnapshot.Empty;
    private A2AFlowView _liveA2A = A2AFlowView.Empty;

    /// <summary>Drops the cached collections so the next read recomputes them and releases captured payloads.</summary>
    internal void Invalidate()
    {
        _historyEntries = [];
        _currentEntries = [];
        _exchanges = [];
        _promptSignature = PromptSignatureView.Empty;
        _inference = InferenceView.Empty;
        _embeddings = EmbeddingsView.Empty;
        _liveContext = ContextSnapshot.Empty;
        _liveA2A = A2AFlowView.Empty;
    }

    internal void EnsureComputed()
    {
        if (_cacheVersion == owner.StateVersion)
        {
            return;
        }

        _cacheVersion = owner.StateVersion;
        var events = owner.Events;
        var archived = owner.Turns;

        var totalTurns = 0;
        var current = new List<ContextEntry>();
        foreach (var e in events)
        {
            if (e.Turn > totalTurns)
            {
                totalTurns = e.Turn;
            }

            if (FlowEventMapping.IsContentEvent(e))
            {
                var chip = FlowEventMapping.ContextChip(e);
                var preview = e.Kind == "received"
                    ? FlowEventMapping.TruncatePreview(owner.RunMessage)
                    : FlowEventMapping.ContextPreview(e);
                current.Add(new ContextEntry(chip.Label, chip.Source, preview, Turn: e.Turn > 0 ? e.Turn : null));
            }
        }

        var history = new List<ContextEntry>(archived.Count * 2);
        foreach (var turn in archived)
        {
            history.Add(new ContextEntry("User message", "user", FlowEventMapping.TruncatePreview(turn.Message), History: true));
            if (!string.IsNullOrWhiteSpace(turn.Error))
            {
                history.Add(new ContextEntry("Error", "app", FlowEventMapping.TruncatePreview(turn.Error), History: true));
            }
            else if (!string.IsNullOrWhiteSpace(turn.Reply))
            {
                history.Add(new ContextEntry("Final answer", "agent", FlowEventMapping.TruncatePreview(turn.Reply), History: true));
            }
        }

        _totalTurns = totalTurns;
        _currentEntries = current;
        _historyEntries = history;

        // The prompt signature works per conversation exchange (each Send): every archived turn plus the
        // current in-progress run, each labelled by its user message.
        var exchanges = new List<(string Label, IReadOnlyList<FlowEvent> Events)>(archived.Count + 1);
        foreach (var turn in archived)
        {
            exchanges.Add((turn.Message, turn.Events));
        }

        if (events.Count > 0)
        {
            exchanges.Add((owner.RunMessage, events));
        }

        _promptSignature = PromptSignatureBuilder.Build(exchanges);

        // The Context bar's char count is kept in lock-step with the Prompt signature's current request so
        // the two figures always agree.
        _contextSize = _promptSignature.CurrentChars;
        _liveContext = new(_historyEntries, _currentEntries, _contextSize);

        // The anatomy shows the persona and the tool catalogue as their own numbers, split out of the
        // latest captured request — unlike the signature/context totals, this includes the tool catalogue.
        string? latestRequest = null;
        for (var i = exchanges.Count - 1; i >= 0 && latestRequest is null; i--)
        {
            var requestEvents = exchanges[i].Events;
            for (var j = requestEvents.Count - 1; j >= 0; j--)
            {
                if (requestEvents[j].Kind == "llm-request" && !string.IsNullOrWhiteSpace(requestEvents[j].Data))
                {
                    latestRequest = requestEvents[j].Data;
                    break;
                }
            }
        }

        _anatomySizes = latestRequest is null
            ? AnatomySizes.Empty
            : PromptSignatureBuilder.AnatomySizesFor(latestRequest);

        _inference = InferenceBuilder.Build(events);
        _embeddings = EmbeddingBuilder.Build(_inference);

        // The Execution explorer reads every exchange: the archived ones plus the one in the live panels
        // (which exists from the moment a message is sent, even before any event arrives).
        var all = new List<ConversationTurn>(archived.Count + 1);
        all.AddRange(archived);
        var liveIndex = -1;
        if (!string.IsNullOrEmpty(owner.RunExchangeId))
        {
            if (owner.Running)
            {
                liveIndex = all.Count;
            }

            all.Add(owner.CurrentExchange());
        }

        _exchanges = ExecutionReplayBuilder.Build(all, liveIndex, owner.EvictedExchanges);
        _liveA2A = A2AFlowBuilder.Build(owner.RunA2A, events, !owner.Running);
    }

    /// <summary>The LLM round-trip of the most recent step (0 before the first request).</summary>
    public int CurrentTurn => owner.Events.Count == 0 ? 0 : owner.Events[^1].Turn;

    /// <summary>The total number of LLM round-trips seen so far.</summary>
    public int TotalTurns { get { EnsureComputed(); return _totalTurns; } }

    /// <summary>
    /// How much conversation content the model is carrying in its latest request (system prompt + every
    /// re-sent message, excluding the static tool catalogue). Identical to the Prompt signature's current
    /// total. Always live; replay uses <see cref="RunReplay.DisplayContext"/>.
    /// </summary>
    public int ContextSize { get { EnsureComputed(); return _contextSize; } }

    /// <summary>The width (0–100%) of the live context growth bar, scaled so typical runs fill it gradually.</summary>
    public int ContextBarWidth => Math.Min(100, ContextSize / 80);

    /// <summary>The compact conversation history carried into the model's context from earlier exchanges, rendered dimmed.</summary>
    public IReadOnlyList<ContextEntry> HistoryEntries { get { EnsureComputed(); return _historyEntries; } }

    /// <summary>The current run's content events as Context chips.</summary>
    public IReadOnlyList<ContextEntry> CurrentEntries { get { EnsureComputed(); return _currentEntries; } }

    public int TotalContextEntries { get { EnsureComputed(); return _historyEntries.Count + _currentEntries.Count; } }

    /// <summary>The latest LLM request by message category, compared against the previous one. Always live.</summary>
    public PromptSignatureView PromptSignature { get { EnsureComputed(); return _promptSignature; } }

    /// <summary>The agent prompt (persona) size in the latest captured request.</summary>
    public int PersonaChars { get { EnsureComputed(); return _anatomySizes.PersonaChars; } }

    /// <summary>The tools-available (catalogue) size in the latest captured request.</summary>
    public int ToolsChars { get { EnsureComputed(); return _anatomySizes.ToolsChars; } }

    public string PersonaCharsLabel => $"{PersonaChars:N0} chars";
    public string ToolsCharsLabel => $"{ToolsChars:N0} chars";

    /// <summary>The simulated "inside the LLM" view for the current run. Purely illustrative.</summary>
    public InferenceView Inference { get { EnsureComputed(); return _inference; } }

    /// <summary>The simulated embeddings &amp; neural-network view for the current run. Purely illustrative.</summary>
    public EmbeddingsView Embeddings { get { EnsureComputed(); return _embeddings; } }

    /// <summary>Every exchange in the conversation, oldest first, with each one's stages grouped by model round-trip.</summary>
    public IReadOnlyList<ExecutionExchange> Exchanges { get { EnsureComputed(); return _exchanges; } }

    internal ContextSnapshot LiveContext { get { EnsureComputed(); return _liveContext; } }
    internal A2AFlowView LiveA2A { get { EnsureComputed(); return _liveA2A; } }
}
