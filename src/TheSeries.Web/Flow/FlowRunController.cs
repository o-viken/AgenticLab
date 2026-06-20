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

    // The message/agent of the run currently shown in the live panels, archived into _turns on next send.
    private string _runMessage = string.Empty;
    private string? _runAgent;

    private string _reply = string.Empty;
    private string? _error;
    private string? _activeNode;
    private string? _activeArrow;
    private string? _activeTool;
    private string? _responseHint;

    private bool _running;
    private bool _paused;
    private bool _awaitingStep;
    private bool _awaitingAnswer;
    private string? _pendingQuestion;
    private string _answerText = string.Empty;

    private string _sessionId = string.Empty;
    // Stays the same across sends so the agent remembers prior turns; reset by "New conversation".
    private string _conversationId = Guid.NewGuid().ToString("n");
    private CancellationTokenSource? _cts;

    /// <summary>Raised whenever the run state changes so the page can re-render (marshal onto the UI thread).</summary>
    public event Func<Task>? Changed;

    private Task NotifyAsync() => Changed?.Invoke() ?? Task.CompletedTask;

    // --- Exposed state ----------------------------------------------------

    public IReadOnlyList<FlowEvent> Events => _events;
    public IReadOnlyList<ConversationTurn> Turns => _turns;
    public IReadOnlyList<SkillChip> KnownSkills => _knownSkills;
    public bool IsSkillLoaded(string name) => _loadedSkills.Contains(name);

    public string Reply => _reply;
    public string? Error => _error;

    /// <summary>Surfaces a page-level error (e.g. failing to load the agent list) in the reply panel.</summary>
    public void ReportError(string message) => _error = message;

    public bool Running => _running;
    public bool Paused => _paused;
    public bool AwaitingStep => _awaitingStep;
    public bool AwaitingAnswer => _awaitingAnswer;
    public string? PendingQuestion => _pendingQuestion;

    /// <summary>The user's in-progress reply to a tool's question.</summary>
    public string AnswerText
    {
        get => _answerText;
        set => _answerText = value;
    }

    // --- Diagram highlighting ---------------------------------------------

    /// <summary>
    /// Highlights a node when it is the active target. The Client and AiService are merged into one
    /// "harness" node, and the Tools box lives inside it, so received/final/tool/llm steps (active node
    /// "harness" or "tools") all light it up.
    /// </summary>
    public string NodeClass(string node)
    {
        var active = node switch
        {
            "harness" => _activeNode is "harness" or "tools",
            _ => _activeNode == node,
        };
        return active ? "active" : string.Empty;
    }

    public string ArrowClass(string arrow) => _activeArrow == arrow ? "active" : string.Empty;

    public string? ActiveArrow => _activeArrow;
    public string? ResponseHint => _responseHint;

    /// <summary>The resource currently being used, based on the active tool, or null when none is active.</summary>
    public ResourceInfo? ActiveResourceInfo => ThemeCatalog.ActiveResource(_activeTool);

    // --- Derived run values ----------------------------------------------

    private int VisibleStepCount => view.VisibleEvents(_events).Count;

    public string StepProgress =>
        VisibleStepCount == 0 ? string.Empty : $"{VisibleStepCount} step{(VisibleStepCount == 1 ? "" : "s")}";

    public bool HasVisibleSteps => VisibleStepCount > 0;

    /// <summary>The LLM round-trip of the most recent step (0 before the first request).</summary>
    public int CurrentTurn => _events.Count == 0 ? 0 : _events[^1].Turn;

    /// <summary>The total number of LLM round-trips seen so far.</summary>
    public int TotalTurns => _events.Select(e => e.Turn).DefaultIfEmpty(0).Max();

    /// <summary>A compact label for the loop badge: the live turn while running, or the total when finished.</summary>
    public string LoopLabel =>
        _running
            ? (CurrentTurn == 0 ? "Turn …" : $"Turn {CurrentTurn}")
            : (TotalTurns == 0 ? "—" : $"{TotalTurns} turn{(TotalTurns == 1 ? "" : "s")}");

    /// <summary>Whether the loop badge should pulse (a turn is in flight).</summary>
    public bool LoopActive => _running && CurrentTurn > 0;

    /// <summary>The user's prompt for the current/next run, shown as the "Set by user" anatomy layer.</summary>
    public string CurrentUserPrompt =>
        !string.IsNullOrWhiteSpace(_runMessage) ? _runMessage
        : !string.IsNullOrWhiteSpace(view.Message) ? view.Message
        : "—";

    /// <summary>
    /// A rough measure of how much context the model is carrying, summed from the captured step data
    /// plus the conversation history re-sent each turn. Drives the growing "context" bar.
    /// </summary>
    public int ContextSize =>
        _events.Sum(e => (e.Data?.Length ?? 0) + e.Label.Length)
        + _turns.Sum(t => t.Message.Length + (t.Error?.Length ?? t.Reply.Length));

    /// <summary>The width (0–100%) of the context growth bar, scaled so typical runs fill it gradually.</summary>
    public int ContextBarWidth => Math.Min(100, ContextSize / 80);

    private IReadOnlyList<FlowEvent> ContentEvents => _events.Where(FlowEventMapping.IsContentEvent).ToList();

    /// <summary>
    /// The compact conversation history carried into the model's context from earlier exchanges:
    /// per archived turn, the user's message and the agent's final answer (or error), rendered dimmed.
    /// </summary>
    public IReadOnlyList<ContextEntry> HistoryEntries
    {
        get
        {
            var entries = new List<ContextEntry>(_turns.Count * 2);
            foreach (var turn in _turns)
            {
                entries.Add(new ContextEntry("User message", "user", FlowEventMapping.TruncatePreview(turn.Message), History: true));
                if (!string.IsNullOrWhiteSpace(turn.Error))
                {
                    entries.Add(new ContextEntry("Error", "app", FlowEventMapping.TruncatePreview(turn.Error), History: true));
                }
                else
                {
                    entries.Add(new ContextEntry("Final answer", "user", FlowEventMapping.TruncatePreview(turn.Reply), History: true));
                }
            }

            return entries;
        }
    }

    /// <summary>The current run's content events as Context chips (full-strength, shown below the history).</summary>
    public IReadOnlyList<ContextEntry> CurrentEntries =>
        ContentEvents.Select(e =>
        {
            var chip = FlowEventMapping.ContextChip(e);
            return new ContextEntry(chip.Label, chip.Source, FlowEventMapping.ContextPreview(e), Turn: e.Turn > 0 ? e.Turn : null);
        }).ToList();

    /// <summary>Total number of content chips on display (conversation history + current run).</summary>
    public int TotalContextEntries => HistoryEntries.Count + CurrentEntries.Count;

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
        _responseHint = null;
        _runMessage = view.Message;
        _runAgent = view.SelectedAgent;
        _events.Clear();
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
        _responseHint = null;
        _events.Clear();
        view.ClearExpanded();
        _turns.Clear();
        _loadedSkills.Clear();
        _runMessage = string.Empty;
        _runAgent = null;

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
        if (_events.Count == 0)
        {
            return;
        }

        _turns.Add(new ConversationTurn(
            _runMessage,
            _runAgent,
            _reply,
            _error,
            new List<FlowEvent>(_events)));
    }

    private async Task ConsumeAsync(CancellationToken token)
    {
        try
        {
            await foreach (var flowEvent in ai.StreamFlowAsync(
                view.Message, view.SelectedAgent, _sessionId, _conversationId,
                view.Mode == FlowMode.Manual, view.StepDelayMs, view.Workspace,
                view.DisabledToolsOrNull, token))
            {
                _events.Add(flowEvent);
                (_activeNode, _activeArrow) = FlowEventMapping.MapTarget(flowEvent.Kind);
                _activeTool = FlowEventMapping.ToolNameFor(flowEvent);
                _responseHint = FlowEventMapping.ResponseHintFor(flowEvent) ?? _responseHint;
                _awaitingStep = false;

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
        _awaitingStep = false;
        _awaitingAnswer = false;
        _pendingQuestion = null;
        _activeNode = null;
        _activeArrow = null;
        _activeTool = null;
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
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
