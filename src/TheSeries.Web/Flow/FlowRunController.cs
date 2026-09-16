using System.Text.Json;

namespace TheSeries.Web.Flow;

/// <summary>
/// Owns the live flow run: the streamed events, the conversation transcript, the run lifecycle state
/// (running/paused/awaiting), the active node/arrow/tool highlighting, the captured reply/error, the
/// skills catalogue, and the session/conversation ids. It drives the backend-gated stepping loop via
/// <see cref="AiServiceClient"/> and reads its run inputs (message, agent, workspace, mode, delay,
/// disabled tools) from the shared <see cref="FlowViewState"/>. Mutations raise <see cref="Changed"/>
/// so the hosting page can re-render; the page marshals that onto the renderer's sync context.
/// </summary>
internal sealed class FlowRunController(AiServiceClient ai, FlowViewState view) : IDisposable
{
    private readonly List<FlowEvent> _events = new();
    private readonly List<ConversationTurn> _turns = new();
    private readonly List<SkillChip> _knownSkills = new();
    private readonly HashSet<string> _loadedSkills = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<InstructionChip> _knownInstructions = new();
    private readonly List<McpChip> _knownMcp = new();
    private readonly List<A2AChip> _knownA2A = new();

    // The message/agent of the run currently shown in the live panels, archived into _turns on next send.
    private string _runMessage = string.Empty;
    private string? _runAgent;
    // The vendor/workspace the current run was actually sent with, so its history cannot report the
    // values the controls happen to hold later.
    private string? _runVendor;
    private string? _runWorkspace;
    private IReadOnlyList<A2AChip> _runA2A = [];
    // Identifies the exchange in the live panels; kept when it is archived so a selection survives.
    private string _runExchangeId = string.Empty;

    private string _reply = string.Empty;
    private string? _error;
    private string? _activeNode;
    private string? _activeArrow;
    private string? _activeTool;
    private string? _activeToolArgs;
    private string? _activeToolResult;
    private string? _responseHint;

    private bool _running;
    private bool _paused;
    private BreakpointNotice? _breakpoint;
    private bool _breakpointControlPending;
    private bool _breakpointSettingsPending;
    private string? _breakpointError;
    private bool _awaitingStep;
    private bool _awaitingAnswer;
    private string? _pendingQuestion;
    private string _answerText = string.Empty;

    private string _sessionId = string.Empty;
    // Stays the same across sends so the agent remembers prior turns; reset by "New conversation".
    private string _conversationId = Guid.NewGuid().ToString("n");
    private CancellationTokenSource? _cts;
    private bool _disposed;

    // Cache for the derived collections below, recomputed only when _events/_turns change (tracked by
    // _stateVersion) instead of on every render — they were allocating fresh lists and running a regex
    // per chip on each of the many renders a run triggers.
    private int _stateVersion;
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
    private ContextSnapshot _replayContext = ContextSnapshot.Empty;
    private PromptSignatureView _replaySignature = PromptSignatureView.Empty;
    private A2AFlowView _liveA2A = A2AFlowView.Empty;
    private A2AFlowView _replayA2A = A2AFlowView.Empty;
    private int _replayContextVersion = -1;
    private string? _replayContextExchange;
    private int? _replayContextSequence;

    /// <summary>Raised whenever the run state changes so the page can re-render (marshal onto the UI thread).</summary>
    public event Func<Task>? Changed;

    // Renders once per streamed event so each step of the run animates as it happens (the per-event
    // render is the whole point of the visualiser); the page bridges this onto the renderer's context.
    private Task NotifyAsync() => _disposed ? Task.CompletedTask : (Changed?.Invoke() ?? Task.CompletedTask);

    // Marks the derived caches dirty after _events/_turns change.
    private void BumpState() => _stateVersion++;

    // Recomputes the cached derived collections once per state change (lazy, on first access after a bump).
    private void EnsureComputed()
    {
        if (_cacheVersion == _stateVersion)
        {
            return;
        }

        _cacheVersion = _stateVersion;

        var totalTurns = 0;
        var current = new List<ContextEntry>();
        foreach (var e in _events)
        {
            if (e.Turn > totalTurns)
            {
                totalTurns = e.Turn;
            }

            if (FlowEventMapping.IsContentEvent(e))
            {
                var chip = FlowEventMapping.ContextChip(e);
                var preview = e.Kind == "received"
                    ? FlowEventMapping.TruncatePreview(_runMessage)
                    : FlowEventMapping.ContextPreview(e);
                current.Add(new ContextEntry(chip.Label, chip.Source, preview, Turn: e.Turn > 0 ? e.Turn : null));
            }
        }

        var history = new List<ContextEntry>(_turns.Count * 2);
        foreach (var turn in _turns)
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
        // current in-progress run, each labelled by its user message. The builder takes each exchange's
        // last llm-request as its representative prompt.
        var exchanges = new List<(string Label, IReadOnlyList<FlowEvent> Events)>(_turns.Count + 1);
        foreach (var turn in _turns)
        {
            exchanges.Add((turn.Message, turn.Events));
        }

        if (_events.Count > 0)
        {
            exchanges.Add((_runMessage, _events));
        }

        _promptSignature = PromptSignatureBuilder.Build(exchanges);

        // Keep the Context bar's char count in lock-step with the Prompt signature's current request: both
        // report the conversation content the model is carrying now (system prompt + every user/assistant/
        // tool message re-sent in the latest llm-request, excluding the static tool catalogue and JSON
        // structure), so the two figures always agree.
        _contextSize = _promptSignature.CurrentChars;
        _liveContext = new(_historyEntries, _currentEntries, _contextSize);

        // The harness anatomy shows the agent prompt (persona) and the tools-available catalogue as their
        // own numbers, split out of the latest captured request (the most recent exchange's last
        // llm-request) — unlike the signature/context totals, this includes the tool catalogue.
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

        // The simulated inference view reads the current run's events directly: the latest llm-request for
        // the user message + prompt-token estimate, and the latest final answer to replay as generated tokens.
        _inference = InferenceBuilder.Build(_events);

        // The embeddings/NN view reuses the inference view's prompt tokenization (fake vectors + 2-D map).
        _embeddings = EmbeddingBuilder.Build(_inference);

        // The Execution explorer reads every exchange: the archived ones plus the one in the live panels
        // (which exists from the moment a message is sent, even before any event arrives).
        var all = new List<ConversationTurn>(_turns.Count + 1);
        all.AddRange(_turns);
        var liveIndex = -1;
        if (!string.IsNullOrEmpty(_runExchangeId))
        {
            if (_running)
            {
                liveIndex = all.Count;
            }

            all.Add(CurrentExchange());
        }

        _exchanges = ExecutionReplayBuilder.Build(all, liveIndex);
        _liveA2A = A2AFlowBuilder.Build(_runA2A, _events, !_running);
    }

    // The run in the live panels, shaped like an archived exchange so both read the same way.
    private ConversationTurn CurrentExchange() =>
        new(_runExchangeId, _runMessage, _runAgent, _reply, _error, _events, _runVendor, _runWorkspace, _runA2A);

    // --- Exposed state ----------------------------------------------------

    public IReadOnlyList<FlowEvent> Events => _events;
    public IReadOnlyList<ConversationTurn> Turns => _turns;
    public IReadOnlyList<SkillChip> KnownSkills => _knownSkills;
    public IReadOnlyList<McpChip> KnownMcp => _knownMcp;

    /// <summary>The agents the selected agent can delegate to over A2A, shown in the harness A2A box.</summary>
    public IReadOnlyList<A2AChip> KnownA2A => _knownA2A;

    /// <summary>The remote-agent topology and delegation state belonging to the displayed exchange.</summary>
    public A2AFlowView DisplayA2A
    {
        get
        {
            EnsureComputed();
            if (Replaying)
            {
                EnsureReplayComputed();
                return _replayA2A;
            }

            return string.IsNullOrEmpty(_runExchangeId)
                ? A2AFlowBuilder.Build(_knownA2A, [], false) : _liveA2A;
        }
    }

    /// <summary>The caller recorded with the displayed exchange, not a later picker selection.</summary>
    public string A2ACaller => (Replaying ? SelectedExchange?.Agent : _runAgent) ?? view.SelectedAgent ?? "Harness";

    /// <summary>Only live, unheld boundary events may animate; remote internals never animate.</summary>
    public bool AnimateA2A => !Replaying && Running && !Paused && _breakpoint is null && view.Mode == FlowMode.Auto;
    public bool IsSkillLoaded(string name) => _loadedSkills.Contains(name);

    /// <summary>The workspace's custom instructions, always injected into the agent's context when present.</summary>
    public IReadOnlyList<InstructionChip> KnownInstructions => _knownInstructions;

    public string Reply => _reply;
    public string? Error => _error;

    /// <summary>The user message of the run currently shown live, before it is archived into <see cref="Turns"/>.</summary>
    public string RunMessage => _runMessage;

    /// <summary>Surfaces a page-level error (e.g. failing to load the agent list) in the reply panel.</summary>
    public void ReportError(string message) => _error = message;

    public bool Running => _running;
    public bool Paused => _paused;
    /// <summary>Whether execution is currently held at a server-reported breakpoint.</summary>
    public bool BreakpointPaused => _breakpoint is not null;
    /// <summary>Whether a breakpoint release request is in progress.</summary>
    public bool BreakpointControlPending => _breakpointControlPending;
    /// <summary>Whether the active run is accepting a new breakpoint selection.</summary>
    public bool BreakpointSettingsPending => _breakpointSettingsPending;
    /// <summary>A failed breakpoint control request, shown without discarding the paused state.</summary>
    public string? BreakpointError => _breakpointError;
    /// <summary>The human-readable execution boundary currently holding the run.</summary>
    public string? BreakpointReason => _breakpoint is { } notice
        ? $"{FlowViewState.BreakpointOptions.FirstOrDefault(option => option.Kind == notice.Kind).Label ?? notice.Kind}{(notice.Tool is null ? "" : $": {notice.Tool}")}"
        : null;
    public bool AwaitingStep => _awaitingStep;
    public bool AwaitingAnswer => _awaitingAnswer;
    public string? PendingQuestion => _pendingQuestion;

    /// <summary>The user's in-progress reply to a tool's question.</summary>
    public string AnswerText
    {
        get => _answerText;
        set => _answerText = value;
    }

    // --- Execution explorer ----------------------------------------------

    /// <summary>
    /// Every exchange in the conversation, oldest first, with each one's captured stages grouped into the
    /// model round-trips they belong to. Includes the run currently in the live panels.
    /// </summary>
    public IReadOnlyList<ExecutionExchange> Exchanges
    {
        get
        {
            EnsureComputed();
            return _exchanges;
        }
    }

    /// <summary>The exchange the explorer is showing: the pinned one, or the newest.</summary>
    public ExecutionExchange? SelectedExchange
    {
        get
        {
            var all = Exchanges;
            if (all.Count == 0)
            {
                return null;
            }

            if (view.CursorExchangeId is { } id)
            {
                foreach (var exchange in all)
                {
                    if (exchange.Id == id)
                    {
                        return exchange;
                    }
                }
            }

            return all[^1];
        }
    }

    /// <summary>
    /// The stage the explorer is showing: the pinned one, the first stage of a freshly opened exchange, or
    /// the newest captured stage while following the live run.
    /// </summary>
    public FlowEvent? SelectedStage
    {
        get
        {
            if (SelectedExchange is not { Stages.Count: > 0 } exchange)
            {
                return null;
            }

            if (view.CursorSequence is { } sequence)
            {
                foreach (var stage in exchange.Stages)
                {
                    if (stage.Sequence == sequence)
                    {
                        return stage;
                    }
                }
            }

            return view.FollowingLive ? exchange.Stages[^1] : exchange.Stages[0];
        }
    }

    /// <summary>Whether the explorer is pinned to a captured stage instead of following the live run.</summary>
    public bool Replaying => !view.FollowingLive;

    private int SelectedStageIndex
    {
        get
        {
            if (SelectedExchange is not { } exchange || SelectedStage is not { } stage)
            {
                return -1;
            }

            for (var i = 0; i < exchange.Stages.Count; i++)
            {
                if (exchange.Stages[i].Sequence == stage.Sequence)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    /// <summary>Whether an earlier captured stage exists to step back to.</summary>
    public bool CanStepBack => SelectedStageIndex > 0;

    /// <summary>Whether a later captured stage exists to step forward to.</summary>
    public bool CanStepForward =>
        SelectedExchange is { } exchange && SelectedStageIndex is var i && i >= 0 && i < exchange.Stages.Count - 1;

    /// <summary>Selects the previous captured stage. Inspection only — it never re-runs anything.</summary>
    public void StepBack() => StepBy(-1);

    /// <summary>Selects the next captured stage. Inspection only — it never re-runs anything.</summary>
    public void StepForward() => StepBy(1);

    private void StepBy(int delta)
    {
        if (SelectedExchange is not { } exchange)
        {
            return;
        }

        var index = SelectedStageIndex + delta;
        if (index < 0 || index >= exchange.Stages.Count)
        {
            return;
        }

        view.SelectStage(exchange.Id, exchange.Stages[index].Sequence);
    }

    /// <summary>The tool call a captured result answers, matched on the model's own call id.</summary>
    public FlowEvent? CallFor(FlowEvent result) =>
        result.CallId is { } id && SelectedExchange is { } exchange
            ? exchange.Stages.FirstOrDefault(s => s.Kind == "tool-call" && s.CallId == id)
            : null;

    // --- Diagram highlighting ---------------------------------------------

    // While a captured stage is pinned the diagram shows that stage instead of the live run, built only
    // from what was captured up to it — a later tool result never leaks back into an earlier stage.
    private (string? Node, string? Arrow) CursorTarget =>
        SelectedStage is { } stage ? FlowEventMapping.MapTarget(stage.Kind) : (null, null);

    private string? CursorNode => Replaying ? CursorTarget.Node : _activeNode;

    private string? CursorArrow => Replaying ? CursorTarget.Arrow : _activeArrow;

    /// <summary>
    /// Highlights a node when it is the active target. The Client and AiService are merged into one
    /// "harness" node, and the Tools box lives inside it, so received/final/tool/llm steps (active node
    /// "harness" or "tools") all light it up.
    /// </summary>
    public string NodeClass(string node)
    {
        var current = CursorNode;
        var active = node switch
        {
            "harness" => current is "harness" or "tools",
            _ => current == node,
        };
        return active ? "active" : string.Empty;
    }

    public string ArrowClass(string arrow) => CursorArrow == arrow ? "active" : string.Empty;

    public string? ActiveArrow => CursorArrow;

    public string? ResponseHint =>
        Replaying ? (SelectedStage is { } stage ? FlowEventMapping.ResponseHintFor(stage) : null) : _responseHint;

    /// <summary>The resource currently being used, based on the active tool, or null when none is active.</summary>
    public ResourceInfo? ActiveResourceInfo => VendorCatalog.ActiveResource(ActiveToolName);

    /// <summary>The specific tool function that contacted the active resource (e.g. "FindPeople"), or null.</summary>
    public string? ActiveToolName =>
        Replaying ? (SelectedStage is { } stage ? FlowEventMapping.StageToolName(stage) : null) : _activeTool;

    /// <summary>A short preview of the arguments the model passed to the active tool call, or null.</summary>
    public string? ActiveToolArgs => Replaying ? ReplayToolArgs : _activeToolArgs;

    /// <summary>A short preview of the result the active tool returned to the harness, or null.</summary>
    public string? ActiveToolResult => Replaying ? ReplayToolResult : _activeToolResult;

    private string? ReplayToolArgs
    {
        get
        {
            if (SelectedStage is not { } stage)
            {
                return null;
            }

            var call = stage.Kind == "tool-call" ? stage : CallFor(stage);
            return call is null ? null : FlowEventMapping.TruncatePreview(call.Detail ?? call.Data);
        }
    }

    // Only a result stage has a result: standing on the call must not reveal what came back afterwards.
    private string? ReplayToolResult =>
        SelectedStage is { Kind: "tool-result" } stage
            ? FlowEventMapping.TruncatePreview(stage.Detail ?? stage.Data)
            : null;

    // --- Derived run values ----------------------------------------------

    /// <summary>The LLM round-trip of the most recent step (0 before the first request).</summary>
    public int CurrentTurn => _events.Count == 0 ? 0 : _events[^1].Turn;

    /// <summary>The total number of LLM round-trips seen so far.</summary>
    public int TotalTurns
    {
        get
        {
            EnsureComputed();
            return _totalTurns;
        }
    }

    /// <summary>A compact label for the loop badge: the live turn while running, or the total when finished.</summary>
    public string LoopLabel =>
        Replaying
            ? (SelectedStage is { Turn: > 0 } stage ? $"Turn {stage.Turn}" : "—")
            : _running
                ? (CurrentTurn == 0 ? "Turn …" : $"Turn {CurrentTurn}")
                : (TotalTurns == 0 ? "—" : $"{TotalTurns} turn{(TotalTurns == 1 ? "" : "s")}");

    /// <summary>Whether the loop badge should pulse (a turn is in flight).</summary>
    public bool LoopActive => !Replaying && _running && CurrentTurn > 0;

    /// <summary>What the agent is doing right now, resolved from the run state in announcement order.</summary>
    public AgentActivity Activity =>
        !string.IsNullOrWhiteSpace(_error) ? AgentActivity.Failed
        : _awaitingAnswer ? AgentActivity.AwaitingAnswer
        : _breakpoint is not null ? AgentActivity.AtBreakpoint
        : _paused ? AgentActivity.Paused
        // _awaitingStep means a Next request is already in flight, so the run is moving again.
        : _running && view.Mode == FlowMode.Manual && !_awaitingStep ? AgentActivity.AwaitingStep
        : _running ? AgentActivity.Thinking
        : !string.IsNullOrWhiteSpace(_reply) ? AgentActivity.Done
        : AgentActivity.Idle;

    /// <summary>The short status shown beside the agent's name while a turn is active, or null when it is not.</summary>
    public string? AgentStatusLabel => Activity switch
    {
        AgentActivity.Failed => "Error",
        AgentActivity.AwaitingAnswer => "Waiting for you",
        AgentActivity.Paused => "Paused",
        AgentActivity.Thinking or AgentActivity.AwaitingStep or AgentActivity.AtBreakpoint => "In progress",
        _ => null,
    };

    /// <summary>Where the run has got to, named after the same execution boundaries as the breakpoints.</summary>
    public string? ProgressLabel => _events.Count == 0 ? null : FlowEventMapping.ProgressLabelFor(_events[^1]);

    /// <summary>The one-line detail under the status: where the run is and what it is waiting on.</summary>
    public string? AgentStatusNote => Activity switch
    {
        AgentActivity.AtBreakpoint => $"{BreakpointReason} — waiting for Next",
        AgentActivity.AwaitingStep => ProgressLabel is { } step ? $"{step} — waiting for Next" : "Waiting for Next",
        AgentActivity.Paused => ProgressLabel is { } at ? $"{at} — paused" : "Stepping paused — resume or stop",
        AgentActivity.AwaitingAnswer => "Waiting for your answer",
        AgentActivity.Thinking => ProgressLabel,
        _ => null,
    };

    /// <summary>The live turn meta shown on the agent's entry, kept in step with the diagram's loop badge.</summary>
    public string? TurnMeta =>
        _running ? (CurrentTurn == 0 ? null : $"turn {CurrentTurn}")
        : TotalTurns == 0 ? null
        : $"{TotalTurns} turn{(TotalTurns == 1 ? "" : "s")}";

    /// <summary>A note under the composer explaining why it cannot send right now, or null when it can.</summary>
    public string? ComposerHint =>
        _running ? "A turn is active. Continue or stop it before sending another message."
        : view.SelectedAgentRequiresWorkspace && string.IsNullOrWhiteSpace(view.Workspace)
            ? "This agent needs a workspace folder — set one under Settings."
            : null;

    /// <summary>The user's prompt for the current/next run, shown as the "Set by user" anatomy layer.</summary>
    public string CurrentUserPrompt =>
        !string.IsNullOrWhiteSpace(_runMessage) ? _runMessage
        : !string.IsNullOrWhiteSpace(view.Message) ? view.Message
        : "—";

    /// <summary>
    /// How much conversation content the model is carrying in its latest request — the system prompt plus
    /// every user/assistant/tool message re-sent in the current exchange's <c>llm-request</c> (the static
    /// tool catalogue and JSON structure excluded). Kept identical to the Prompt signature's current total
    /// (<see cref="PromptSignatureView.CurrentChars"/>). Replay uses <see cref="DisplayContext"/> and
    /// <see cref="DisplayPromptSignature"/> instead; this telemetry value stays live.
    /// </summary>
    public int ContextSize
    {
        get
        {
            EnsureComputed();
            return _contextSize;
        }
    }

    /// <summary>
    /// Context shown in the anatomy: live content, or a cached prefix at the replay cursor. Cursor changes
    /// invalidate the replay snapshots; live signature, inference and execution caches stay independent.
    /// </summary>
    public ContextSnapshot DisplayContext
    {
        get
        {
            EnsureComputed();
            if (!Replaying)
            {
                return _liveContext;
            }

            EnsureReplayComputed();
            return _replayContext;
        }
    }

    /// <summary>Prompt signature at the replay cursor, or the live signature when following execution.</summary>
    public PromptSignatureView DisplayPromptSignature
    {
        get
        {
            EnsureComputed();
            if (!Replaying)
            {
                return _promptSignature;
            }

            EnsureReplayComputed();
            return _replaySignature;
        }
    }

    private void EnsureReplayComputed()
    {
        var exchange = SelectedExchange;
        var sequence = SelectedStage?.Sequence;
        if (_replayContextVersion == _stateVersion
            && _replayContextExchange == exchange?.Id
            && _replayContextSequence == sequence)
        {
            return;
        }

        _replayContext = exchange is null
            ? ContextSnapshot.Empty
            : ExecutionReplayBuilder.ContextAt(_exchanges, exchange.Id, sequence);
        _replaySignature = exchange is null
            ? PromptSignatureView.Empty
            : ExecutionReplayBuilder.SignatureAt(_exchanges, exchange.Id, sequence);
        _replayA2A = exchange is null ? A2AFlowView.Empty
            : A2AFlowBuilder.Build(exchange.A2AAgents ?? [], ExecutionReplayBuilder.PrefixThrough(exchange, sequence),
                SelectedStage?.Kind is "final" or "error" ||
                (SelectedStage == exchange.Stages.LastOrDefault() && exchange.Status != ExchangeStatus.Running));
        _replayContextVersion = _stateVersion;
        _replayContextExchange = exchange?.Id;
        _replayContextSequence = sequence;
    }

    /// <summary>The width (0–100%) of the live context growth bar, scaled so typical runs fill it gradually.</summary>
    public int ContextBarWidth => Math.Min(100, ContextSize / 80);

    /// <summary>
    /// The <em>agent prompt</em> (persona) size in the latest captured request — the text inside the
    /// instructions' <c>&lt;agentMode&gt;</c> tags. Shown as a separate number on the Agent Persona anatomy box.
    /// </summary>
    public int PersonaChars
    {
        get
        {
            EnsureComputed();
            return _anatomySizes.PersonaChars;
        }
    }

    /// <summary>
    /// The <em>tools available</em> size in the latest captured request — the tool catalogue's name +
    /// description + parameters (deliberately excluded from the signature/context totals). Shown as a
    /// separate number on the Tools / MCP anatomy box.
    /// </summary>
    public int ToolsChars
    {
        get
        {
            EnsureComputed();
            return _anatomySizes.ToolsChars;
        }
    }

    /// <summary>The agent prompt size formatted for display (e.g. "2,690 chars").</summary>
    public string PersonaCharsLabel => $"{PersonaChars:N0} chars";

    /// <summary>The tools-available size formatted for display (e.g. "2,415 chars").</summary>
    public string ToolsCharsLabel => $"{ToolsChars:N0} chars";

    /// <summary>
    /// The compact conversation history carried into the model's context from earlier exchanges:
    /// per archived turn, the user's message and the agent's final answer (or error), rendered dimmed.
    /// </summary>
    public IReadOnlyList<ContextEntry> HistoryEntries
    {
        get
        {
            EnsureComputed();
            return _historyEntries;
        }
    }

    /// <summary>The current run's content events as Context chips (full-strength, shown below the history).</summary>
    public IReadOnlyList<ContextEntry> CurrentEntries
    {
        get
        {
            EnsureComputed();
            return _currentEntries;
        }
    }

    /// <summary>Total number of content chips on display (conversation history + current run).</summary>
    public int TotalContextEntries
    {
        get
        {
            EnsureComputed();
            return _historyEntries.Count + _currentEntries.Count;
        }
    }

    /// <summary>
    /// The latest LLM request broken down by message category, compared against the previous request
    /// (with a prefix-stability match score). Always live; the panel uses DisplayPromptSignature.
    /// </summary>
    public PromptSignatureView PromptSignature
    {
        get
        {
            EnsureComputed();
            return _promptSignature;
        }
    }

    /// <summary>
    /// The simulated "inside the LLM" inference view for the current run: the latest user message tokenized
    /// and the final answer replayed as autoregressively generated tokens (with fabricated candidate lists).
    /// Drives the Inference panel. Purely illustrative — the backend exposes no tokens.
    /// </summary>
    public InferenceView Inference
    {
        get
        {
            EnsureComputed();
            return _inference;
        }
    }

    /// <summary>
    /// The simulated embeddings &amp; neural-network view for the current run: the prompt's tokens as fake
    /// vectors with 2-D positions, plus the predicted next token. Drives the Embeddings panel. Purely
    /// illustrative — the backend exposes no embeddings.
    /// </summary>
    public EmbeddingsView Embeddings
    {
        get
        {
            EnsureComputed();
            return _embeddings;
        }
    }

    // --- Run lifecycle ----------------------------------------------------

    /// <summary>Starts a run for the current message (no-op when already running or the message is blank).</summary>
    public Task SendAsync()
    {
        if (_running || string.IsNullOrWhiteSpace(view.Message))
        {
            return Task.CompletedTask;
        }

        _running = true;
        // Archive the just-finished run (with its reply/error/events intact) before resetting them.
        ArchiveCurrentRun();
        _paused = false;
        _awaitingStep = false;
        _awaitingAnswer = false;
        _pendingQuestion = null;
        _answerText = string.Empty;
        _error = null;
        _reply = string.Empty;
        _activeNode = null;
        _activeArrow = null;
        _activeTool = null;
        _activeToolArgs = null;
        _activeToolResult = null;
        _responseHint = null;
        _runMessage = view.Message;
        _runAgent = view.SelectedAgent;
        _runVendor = view.VendorKey;
        _runWorkspace = view.Workspace;
        _runA2A = view.SelectedAgentSupportsA2A ? _knownA2A.ToArray() : [];
        _runExchangeId = Guid.NewGuid().ToString("n");
        // A new message always takes the explorer back to the run that is happening now.
        view.FollowLive();
        // Remember the workspace this run used so it can be suggested again next time.
        view.AddRecentWorkspace(view.Workspace);
        // Clear the composer so the sent message moves into the conversation log, not lingering in the box.
        view.Message = string.Empty;
        _events.Clear();
        BumpState();
        view.ClearExpanded();
        _loadedSkills.Clear();
        _sessionId = Guid.NewGuid().ToString("n");
        _cts = new CancellationTokenSource();

        // The backend paces the run; we render each step as it arrives and drive pace via control calls.
        _ = ConsumeAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <summary>Forgets the conversation's history on the server and starts a fresh one, clearing the panel.</summary>
    public async Task NewConversationAsync()
    {
        if (_running)
        {
            return;
        }

        var previous = _conversationId;
        _conversationId = Guid.NewGuid().ToString("n");
        _reply = string.Empty;
        _error = null;
        _activeNode = null;
        _activeArrow = null;
        _activeTool = null;
        _activeToolArgs = null;
        _activeToolResult = null;
        _responseHint = null;
        _events.Clear();
        view.ClearExpanded();
        _turns.Clear();
        BumpState();
        _loadedSkills.Clear();
        _runMessage = string.Empty;
        _runAgent = null;
        _runVendor = null;
        _runWorkspace = null;
        _runA2A = [];
        _runExchangeId = string.Empty;
        view.FollowLive();

        try
        {
            await ai.ResetConversationAsync(previous);
        }
        catch (Exception ex)
        {
            _error = $"Could not reset the conversation: {ex.Message}";
        }

        await NotifyAsync();
    }

    // Moves the run currently in the live panels into the conversation transcript, preserving its history.
    private void ArchiveCurrentRun()
    {
        // Archive by exchange, not by event count: a run that was stopped before anything arrived is still
        // a real exchange and stays inspectable.
        if (string.IsNullOrEmpty(_runExchangeId))
        {
            return;
        }

        _turns.Add(CurrentExchange() with { Events = new List<FlowEvent>(_events) });
        BumpState();
    }

    private async Task ConsumeAsync(CancellationToken token)
    {
        try
        {
            await foreach (var flowEvent in ai.StreamFlowAsync(
                _runMessage, view.SelectedAgent, _sessionId, _conversationId,
                view.Mode == FlowMode.Manual, view.StepDelayMs, view.Workspace,
                view.DisabledToolsOrNull, view.DisabledSkillsOrNull, view.EnabledInstructionsOrNull, view.VendorKey, token, view.Breakpoints))
            {
                if (flowEvent.Kind == "breakpoint")
                {
                    var notice = JsonSerializer.Deserialize<BreakpointNotice>(flowEvent.Data!);
                    if (notice is not null)
                    {
                        view.Mode = notice.Manual ? FlowMode.Manual : FlowMode.Auto;
                        if (notice.Paused)
                        {
                            _breakpoint = notice;
                            _paused = true;
                            _awaitingStep = false;
                        }
                        else if (_breakpoint?.Id == notice.Id)
                        {
                            _breakpoint = null;
                            _paused = false;
                        }
                        await NotifyAsync();
                    }
                    continue;
                }

                _events.Add(flowEvent);
                BumpState();
                (_activeNode, _activeArrow) = FlowEventMapping.MapTarget(flowEvent.Kind);
                _activeTool = FlowEventMapping.ToolNameFor(flowEvent);
                _responseHint = FlowEventMapping.ResponseHintFor(flowEvent) ?? _responseHint;
                _awaitingStep = false;

                // Capture the call arguments and result so the resource box can show what the tool did.
                if (flowEvent.Kind == "tool-call")
                {
                    _activeToolArgs = FlowEventMapping.TruncatePreview(flowEvent.Detail ?? flowEvent.Data);
                    _activeToolResult = null;
                }
                else if (flowEvent.Kind == "tool-result")
                {
                    _activeToolResult = FlowEventMapping.TruncatePreview(flowEvent.Detail ?? flowEvent.Data);
                }

                TrackSkills(flowEvent);

                if (flowEvent.Kind == "ask-question")
                {
                    // The agent paused to ask the user something; show the inline answer box.
                    _pendingQuestion = flowEvent.Detail ?? flowEvent.Data ?? "The agent asked a question.";
                    _answerText = string.Empty;
                    _awaitingAnswer = true;
                }
                else if (flowEvent.Kind == "final")
                {
                    _reply = flowEvent.Detail ?? string.Empty;
                }
                else if (flowEvent.Kind == "error")
                {
                    _error = flowEvent.Detail ?? flowEvent.Label;
                }

                await NotifyAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped by the user; keep whatever has been shown so far.
        }
        catch (Exception ex)
        {
            _error ??= $"Error talking to the AI service: {ex.Message}";
        }
        finally
        {
            Finish();
            await NotifyAsync();
        }
    }

    /// <summary>Advances one step in manual mode.</summary>
    public async Task NextAsync()
    {
        if (BreakpointPaused)
        {
            await ReleaseBreakpointAsync(manual: true);
            return;
        }

        if (!_running || view.Mode != FlowMode.Manual || _awaitingStep)
        {
            return;
        }

        _awaitingStep = true;
        await NotifyAsync();
        await SafeControlAsync("next");
    }

    /// <summary>Pauses or resumes an auto-mode run.</summary>
    public async Task TogglePauseAsync()
    {
        if (BreakpointPaused)
        {
            await ContinueAsync();
            return;
        }

        if (!_running)
        {
            return;
        }

        _paused = !_paused;
        await NotifyAsync();
        await SafeControlAsync(_paused ? "pause" : "resume");
    }

    /// <summary>Pushes the current auto-step delay to the running backend session.</summary>
    public async Task SetDelayAsync()
    {
        if (_running && view.Mode == FlowMode.Auto)
        {
            await SafeControlAsync(action: null, delayMs: view.StepDelayMs);
        }
    }

    /// <summary>Stops the run.</summary>
    public async Task StopAsync()
    {
        _cts?.Cancel();
        await SafeControlAsync("stop");
    }

    /// <summary>Sends the user's reply to a tool's question back to the paused run, which then resumes.</summary>
    public async Task SubmitAnswerAsync()
    {
        if (!_awaitingAnswer || string.IsNullOrWhiteSpace(_answerText))
        {
            return;
        }

        var answer = _answerText;
        _awaitingAnswer = false;
        _pendingQuestion = null;
        _answerText = string.Empty;
        await NotifyAsync();
        await SafeControlAsync("answer", answer: answer);
    }

    /// <summary>Continues automatically until another enabled breakpoint is reached.</summary>
    public Task ContinueAsync() => ReleaseBreakpointAsync(manual: false);

    private async Task ReleaseBreakpointAsync(bool manual)
    {
        if (!_running || _breakpoint is not { } notice || _breakpointControlPending)
        {
            return;
        }

        _breakpointControlPending = true;
        _breakpointError = null;
        await NotifyAsync();
        try
        {
            await ai.SendControlAsync(_sessionId, manual ? "next" : "resume", breakpointId: notice.Id);
        }
        catch (Exception ex)
        {
            _breakpointError = $"Could not release breakpoint: {ex.Message}";
        }
        finally
        {
            _breakpointControlPending = false;
            await NotifyAsync();
        }
    }

    /// <summary>Updates a breakpoint locally and on the active run, reverting if the server rejects it.</summary>
    public async Task SetBreakpointAsync(string kind, bool enabled)
    {
        if (_breakpointSettingsPending) return;
        var previous = view.IsBreakpointEnabled(kind);
        view.SetBreakpoint(kind, enabled);
        _breakpointError = null;
        if (!_running) return;
        _breakpointSettingsPending = true;
        await NotifyAsync();
        try
        {
            await ai.SendControlAsync(_sessionId, breakpoints: view.Breakpoints);
        }
        catch (Exception ex)
        {
            view.SetBreakpoint(kind, previous);
            _breakpointError = $"Could not update breakpoints: {ex.Message}";
        }
        finally
        {
            _breakpointSettingsPending = false;
            await NotifyAsync();
        }
    }

    private async Task SafeControlAsync(string? action, int? delayMs = null, string? answer = null)
    {
        if (string.IsNullOrEmpty(_sessionId))
        {
            return;
        }

        try
        {
            await ai.SendControlAsync(_sessionId, action, delayMs: delayMs, answer: answer);
        }
        catch
        {
            // Best-effort: control is advisory and the run may already have ended.
        }
    }

    private void Finish()
    {
        _running = false;
        _paused = false;
        // The exchange's status changes with it, so let the explorer recompute.
        BumpState();
        _breakpoint = null;
        _breakpointControlPending = false;
        _breakpointSettingsPending = false;
        _breakpointError = null;
        _awaitingStep = false;
        _awaitingAnswer = false;
        _pendingQuestion = null;
        _activeNode = null;
        _activeArrow = null;
        _activeTool = null;
        _activeToolArgs = null;
        _activeToolResult = null;
        _responseHint = null;
    }

    // --- Skills -----------------------------------------------------------

    /// <summary>
    /// Loads the workspace's skill catalogue so the harness Skills box can list it before a run. Resets
    /// any loaded markers and clears the list when the selected agent does not use skills or no workspace
    /// is set. Called when the agent or workspace changes.
    /// </summary>
    public async Task RefreshKnownSkillsAsync()
    {
        _knownSkills.Clear();
        _loadedSkills.Clear();

        if (!view.SelectedAgentSupportsSkills || string.IsNullOrWhiteSpace(view.Workspace))
        {
            await NotifyAsync();
            return;
        }

        try
        {
            var response = await ai.GetSkillsAsync(view.Workspace);
            if (response is not null)
            {
                _knownSkills.AddRange(response.Skills.Select(s => new SkillChip(s.Name, s.Description)));
            }
        }
        catch
        {
            // Best-effort: the catalogue is informational and the path may still be mid-edit.
        }

        await NotifyAsync();
    }

    // --- Custom instructions ----------------------------------------------

    /// <summary>
    /// Loads the workspace's custom instructions so the harness anatomy can list them before a run. Clears
    /// the list when the selected agent does not use a workspace or no workspace is set. Their full content
    /// is always injected into the agent's context per run by the backend. Called when the agent or
    /// workspace changes.
    /// </summary>
    public async Task RefreshKnownInstructionsAsync()
    {
        _knownInstructions.Clear();

        if (!view.SelectedAgentSupportsInstructions || string.IsNullOrWhiteSpace(view.Workspace))
        {
            await NotifyAsync();
            return;
        }

        try
        {
            var response = await ai.GetInstructionsAsync(view.Workspace);
            if (response is not null)
            {
                _knownInstructions.AddRange(response.Instructions.Select(i => new InstructionChip(i.Name, i.Description)));
            }
        }
        catch
        {
            // Best-effort: the list is informational and the path may still be mid-edit.
        }

        await NotifyAsync();
    }

    /// <summary>
    /// Loads the workspace's user-authored agents so the picker can append them after the vendor roster.
    /// Clears them when the current vendor has no workspace-requiring agent or no workspace is set. Called
    /// when the workspace or vendor changes.
    /// </summary>
    public async Task RefreshWorkspaceAgentsAsync()
    {
        if (!view.VendorHasWorkspaceAgent || string.IsNullOrWhiteSpace(view.Workspace))
        {
            view.SetWorkspaceAgents(Array.Empty<AgentInfo>());
            return;
        }

        try
        {
            var response = await ai.GetWorkspaceAgentsAsync(view.Workspace);
            view.SetWorkspaceAgents(response?.Agents ?? Array.Empty<AgentInfo>());
        }
        catch
        {
            // Best-effort: the roster is informational and the path may still be mid-edit.
            view.SetWorkspaceAgents(Array.Empty<AgentInfo>());
        }
    }

    /// <summary>
    /// Refreshes everything that depends on the workspace path — the skill catalogue and the user-authored
    /// agent roster — in one call. Wired to the workspace input losing focus and to vendor changes.
    /// </summary>
    public async Task RefreshWorkspaceContextAsync()
    {
        await RefreshKnownSkillsAsync();
        await RefreshKnownInstructionsAsync();
        await RefreshWorkspaceAgentsAsync();
        await RefreshHarnessPromptAsync();
    }

    /// <summary>
    /// Loads the repo sub-folders under the configured base folders so the workspace input can suggest
    /// them. Clears the suggestions when no base folders are set. Wired to the base-folders input changing.
    /// </summary>
    public async Task RefreshWorkspaceSuggestionsAsync()
    {
        var bases = view.WorkspaceBasePaths;
        if (bases.Count == 0)
        {
            view.SetWorkspaceDirectories(Array.Empty<WorkspaceEntry>());
            return;
        }

        try
        {
            var response = await ai.GetWorkspaceDirectoriesAsync(bases);
            view.SetWorkspaceDirectories(response?.Directories ?? Array.Empty<WorkspaceEntry>());
        }
        catch
        {
            // Best-effort: suggestions are informational and a base path may still be mid-edit.
            view.SetWorkspaceDirectories(Array.Empty<WorkspaceEntry>());
        }
    }

    /// <summary>
    /// Loads the active system (harness) prompt for the selected vendor and agent so the harness
    /// anatomy can show it before a run. A selected brand vendor's harness replaces the agent's own.
    /// Called when the vendor or agent changes.
    /// </summary>
    public async Task RefreshHarnessPromptAsync()
    {
        try
        {
            var response = await ai.GetHarnessAsync(view.SelectedAgent, view.VendorKey);
            view.SetHarnessPrompt(response?.Prompt);
        }
        catch
        {
            // Best-effort: the prompt is informational; keep the descriptive fallback on failure.
            view.SetHarnessPrompt(null);
        }
    }

    /// <summary>
    /// Reacts to the selected agent changing: refreshes the skill catalogue and the active system prompt
    /// (both depend on which agent is selected). Wired to the agent picker.
    /// </summary>
    public async Task OnAgentChangedAsync()
    {
        await RefreshKnownSkillsAsync();
        await RefreshKnownInstructionsAsync();
        await RefreshKnownMcpAsync();
        await RefreshKnownA2AAsync();
        await RefreshHarnessPromptAsync();
    }

    /// <summary>
    /// Refreshes the MCP discovery box: the tools the AI service discovered from its MCP server(s). The
    /// connection is global (not workspace-scoped), so this just reflects what the agent supports.
    /// </summary>
    public async Task RefreshKnownMcpAsync()
    {
        _knownMcp.Clear();
        if (!view.SelectedAgentSupportsMcp)
        {
            await NotifyAsync();
            return;
        }

        try
        {
            var response = await ai.GetMcpAsync();
            if (response is not null)
            {
                _knownMcp.AddRange(response.Servers.SelectMany(s => s.Tools.Select(t => new McpChip(t.Name, t.Description))));
            }
        }
        catch
        {
            // Best-effort: the MCP catalogue is informational.
        }

        await NotifyAsync();
    }

    /// <summary>
    /// Refreshes the A2A box: the agents the AI service can reach over the A2A protocol, which the selected
    /// agent can delegate to. The connection is global (not workspace-scoped), so this just reflects what
    /// the agent supports.
    /// </summary>
    public async Task RefreshKnownA2AAsync()
    {
        _knownA2A.Clear();
        if (!view.SelectedAgentSupportsA2A)
        {
            await NotifyAsync();
            return;
        }

        try
        {
            var response = await ai.GetA2AAsync();
            if (response is not null)
            {
                _knownA2A.AddRange(response.Agents.Select(a => new A2AChip(a.Name, a.Description)));
            }
        }
        catch
        {
            // Best-effort: the A2A catalogue is informational.
        }

        await NotifyAsync();
    }


    // Keeps the harness Skills box in sync with the run: the catalogue of skills the workspace offers
    // (parsed once from the first llm-request's instructions) and the ones the model has actually loaded.
    private void TrackSkills(FlowEvent flowEvent)
    {
        if (flowEvent.Kind == "llm-request" && _knownSkills.Count == 0 && !string.IsNullOrWhiteSpace(flowEvent.Data))
        {
            _knownSkills.AddRange(SkillParsing.ParseKnownSkills(flowEvent.Data));
        }
        else if (flowEvent.Kind == "tool-call")
        {
            var loaded = SkillParsing.LoadedSkillFor(flowEvent, _knownSkills);
            if (loaded is not null)
            {
                _loadedSkills.Add(loaded);
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
