using System.Text.Json;
using System.Diagnostics.Metrics;

namespace TheSeries.Web.Flow;

/// <summary>
/// Owns the live flow run: the streamed events, the archived conversation, the run lifecycle
/// (send / step / pause / stop / answer / breakpoints), the captured reply or error and the session and
/// conversation ids. It drives the backend-gated stepping loop via <see cref="AiServiceClient"/> and
/// reads its run inputs from the shared <see cref="FlowViewState"/>. Everything derived from that state
/// lives on the collaborators: <see cref="Projections"/> (cached derived collections),
/// <see cref="Replay"/> (the Execution explorer's read-only cursor views), <see cref="Focus"/> (what the
/// diagram highlights), <see cref="Status"/> (human-readable run status) and <see cref="Catalogs"/> (the
/// skills / instructions / MCP / A2A catalogues and their refreshers). Mutations raise
/// <see cref="Changed"/> so the hosting page can re-render; the page marshals that onto the renderer.
/// </summary>
internal sealed class FlowRunController : IDisposable
{
    internal const string MeterName = "TheSeries.Web.Flow";
    private static readonly Meter Meter = new(MeterName);
    private static readonly UpDownCounter<long> ActivePagesMetric = Meter.CreateUpDownCounter<long>("flow.active_pages");
    private static readonly UpDownCounter<long> EventCountMetric = Meter.CreateUpDownCounter<long>("flow.retained_events");
    private static readonly UpDownCounter<long> PayloadMetric = Meter.CreateUpDownCounter<long>("flow.retained_payload_bytes", "By");

    private readonly AiServiceClient _ai;
    private readonly FlowViewState _view;
    private readonly List<FlowEvent> _events = new();
    private readonly ReplayHistory _history;

    // The message/agent of the run currently shown in the live panels, archived on next send. The vendor/
    // workspace are the ones the run was actually sent with, so its history cannot report later edits.
    private string _runMessage = string.Empty;
    private string? _runAgent;
    private string? _runVendor;
    private string? _runWorkspace;
    private IReadOnlyList<A2AChip> _runA2A = [];
    // Identifies the exchange in the live panels; kept when it is archived so a selection survives.
    private string _runExchangeId = string.Empty;

    private string _reply = string.Empty;
    private string? _error;
    private LiveHighlight _highlight = LiveHighlight.None;

    private bool _running;
    private bool _paused;
    private BreakpointNotice? _breakpoint;
    private bool _breakpointControlPending;
    private bool _breakpointSettingsPending;
    private string? _breakpointError;
    private bool _awaitingStep;
    private bool _awaitingAnswer;
    private string? _pendingQuestion;

    private string _sessionId = string.Empty;
    // Stays the same across sends so the agent remembers prior turns; reset by "New conversation".
    private string _conversationId = Guid.NewGuid().ToString("n");
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private int _metricEventCount;
    private long _metricPayloadBytes;
    private int _stateVersion;

    public FlowRunController(AiServiceClient ai, FlowViewState view, ReplayRetentionOptions? retentionOptions = null)
    {
        _ai = ai;
        _view = view;
        _history = new ReplayHistory(retentionOptions ?? new());
        ActivePagesMetric.Add(1);
        Projections = new RunProjections(this);
        Replay = new RunReplay(this);
        Focus = new DiagramFocus(this);
        Status = new RunStatus(this);
        Catalogs = new WorkspaceCatalogs(ai, view, NotifyAsync);
    }

    /// <summary>Raised whenever the run state changes so the page can re-render (marshal onto the UI thread).</summary>
    public event Func<Task>? Changed;

    public RunProjections Projections { get; }
    public RunReplay Replay { get; }
    public DiagramFocus Focus { get; }
    public RunStatus Status { get; }
    public WorkspaceCatalogs Catalogs { get; }

    // Renders once per streamed event so each step of the run animates as it happens (the per-event
    // render is the whole point of the visualiser); the page bridges this onto the renderer's context.
    private Task NotifyAsync() => _disposed ? Task.CompletedTask : (Changed?.Invoke() ?? Task.CompletedTask);

    // Invalidates derived caches and releases references to captures after events/history change.
    private void BumpState()
    {
        _stateVersion++;
        UpdateMetrics();
        Projections.Invalidate();
        Replay.Invalidate();
    }

    private void UpdateMetrics()
    {
        var eventCount = _history.EventCount + _events.Count;
        var payloadBytes = _history.PayloadBytes + ReplayHistory.EstimatePayloadBytes(CurrentExchange());
        EventCountMetric.Add(eventCount - _metricEventCount);
        PayloadMetric.Add(payloadBytes - _metricPayloadBytes);
        _metricEventCount = eventCount;
        _metricPayloadBytes = payloadBytes;
    }

    // --- State read by the collaborators -----------------------------------

    internal FlowViewState View => _view;
    internal int StateVersion => _stateVersion;
    internal string RunExchangeId => _runExchangeId;
    internal string? RunAgent => _runAgent;
    internal IReadOnlyList<A2AChip> RunA2A => _runA2A;
    internal LiveHighlight Highlight => _highlight;
    internal BreakpointNotice? Breakpoint => _breakpoint;

    // The run in the live panels, shaped like an archived exchange so both read the same way.
    internal ConversationTurn CurrentExchange() =>
        new(_runExchangeId, _runMessage, _runAgent, _reply, _error, _events, _runVendor, _runWorkspace, _runA2A);

    // --- Exposed state -------------------------------------------------------

    public IReadOnlyList<FlowEvent> Events => _events;
    public IReadOnlyList<ConversationTurn> Turns => _history.Turns;

    /// <summary>The number of oldest exchanges removed from this page's local history.</summary>
    public int EvictedExchanges => _history.EvictedExchanges;

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

    public bool AwaitingStep => _awaitingStep;
    public bool AwaitingAnswer => _awaitingAnswer;
    public string? PendingQuestion => _pendingQuestion;

    /// <summary>The user's in-progress reply to a tool's question.</summary>
    public string AnswerText { get; set; } = string.Empty;

    // --- Run lifecycle -------------------------------------------------------

    /// <summary>Starts a run for the current message (no-op when already running or the message is blank).</summary>
    public Task SendAsync()
    {
        if (_running || string.IsNullOrWhiteSpace(_view.Message))
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
        AnswerText = string.Empty;
        _error = null;
        _reply = string.Empty;
        _highlight = LiveHighlight.None;
        _runMessage = _view.Message;
        _runAgent = _view.SelectedAgent;
        _runVendor = _view.VendorKey;
        _runWorkspace = _view.Workspace;
        _runA2A = _view.Agent.SupportsA2A ? Catalogs.KnownA2A.ToArray() : [];
        _runExchangeId = Guid.NewGuid().ToString("n");
        // A new message always takes the explorer back to the run that is happening now.
        _view.Cursor.FollowLive();
        // Remember the workspace this run used so it can be suggested again next time.
        _view.WorkspacePrefs.AddRecent(_view.Workspace);
        // Clear the composer so the sent message moves into the conversation log.
        _view.Message = string.Empty;
        _events.Clear();
        BumpState();
        Catalogs.ClearLoaded();
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
        _highlight = LiveHighlight.None;
        _events.Clear();
        _history.Clear();
        BumpState();
        Catalogs.ClearLoaded();
        _runMessage = string.Empty;
        _runAgent = null;
        _runVendor = null;
        _runWorkspace = null;
        _runA2A = [];
        _runExchangeId = string.Empty;
        _view.Cursor.FollowLive();

        try
        {
            await _ai.ResetConversationAsync(previous);
        }
        catch (Exception ex)
        {
            _error = $"Could not reset the conversation: {ex.Message}";
        }

        await NotifyAsync();
    }

    // Archives by exchange, not by event count: a run stopped before anything arrived is still a real
    // exchange and stays inspectable.
    private void ArchiveCurrentRun()
    {
        if (string.IsNullOrEmpty(_runExchangeId))
        {
            return;
        }

        _history.Add(CurrentExchange() with { Events = new List<FlowEvent>(_events) });
        BumpState();
    }

    private async Task ConsumeAsync(CancellationToken token)
    {
        var options = _view.Options;
        try
        {
            await foreach (var flowEvent in _ai.StreamFlowAsync(
                _runMessage, _view.SelectedAgent, _sessionId, _conversationId,
                options.Mode == FlowMode.Manual, options.StepDelayMs, _view.Workspace,
                options.DisabledToolsOrNull, options.DisabledSkillsOrNull, options.EnabledInstructionsOrNull, _view.VendorKey, token, options.Breakpoints))
            {
                if (flowEvent.Kind == "breakpoint")
                {
                    ApplyBreakpointNotice(flowEvent);
                    await NotifyAsync();
                    continue;
                }

                _events.Add(flowEvent);
                BumpState();
                _highlight = _highlight.Apply(flowEvent);
                _awaitingStep = false;
                Catalogs.Track(flowEvent);

                switch (flowEvent.Kind)
                {
                    case "ask-question":
                        // The agent paused to ask the user something; show the inline answer box.
                        _pendingQuestion = flowEvent.Detail ?? flowEvent.Data ?? "The agent asked a question.";
                        AnswerText = string.Empty;
                        _awaitingAnswer = true;
                        break;
                    case "final":
                        _reply = flowEvent.Detail ?? string.Empty;
                        break;
                    case "error":
                        _error = flowEvent.Detail ?? flowEvent.Label;
                        break;
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

    private void ApplyBreakpointNotice(FlowEvent flowEvent)
    {
        var notice = JsonSerializer.Deserialize<BreakpointNotice>(flowEvent.Data!);
        if (notice is null)
        {
            return;
        }

        _view.Options.Mode = notice.Manual ? FlowMode.Manual : FlowMode.Auto;
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
    }

    /// <summary>Advances one step in manual mode (or releases a held breakpoint into manual stepping).</summary>
    public async Task NextAsync()
    {
        if (BreakpointPaused)
        {
            await ReleaseBreakpointAsync(manual: true);
            return;
        }

        if (!_running || _view.Options.Mode != FlowMode.Manual || _awaitingStep)
        {
            return;
        }

        _awaitingStep = true;
        await NotifyAsync();
        await SafeControlAsync("next");
    }

    /// <summary>Pauses or resumes an auto-mode run (or continues past a held breakpoint).</summary>
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
        if (_running && _view.Options.Mode == FlowMode.Auto)
        {
            await SafeControlAsync(action: null, delayMs: _view.Options.StepDelayMs);
        }
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        await SafeControlAsync("stop");
    }

    /// <summary>Sends the user's reply to a tool's question back to the paused run, which then resumes.</summary>
    public async Task SubmitAnswerAsync()
    {
        if (!_awaitingAnswer || string.IsNullOrWhiteSpace(AnswerText))
        {
            return;
        }

        var answer = AnswerText;
        _awaitingAnswer = false;
        _pendingQuestion = null;
        AnswerText = string.Empty;
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
            await _ai.SendControlAsync(_sessionId, manual ? "next" : "resume", breakpointId: notice.Id);
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
        var options = _view.Options;
        var previous = options.IsBreakpointEnabled(kind);
        options.SetBreakpoint(kind, enabled);
        _breakpointError = null;
        if (!_running) return;
        _breakpointSettingsPending = true;
        await NotifyAsync();
        try
        {
            await _ai.SendControlAsync(_sessionId, breakpoints: options.Breakpoints);
        }
        catch (Exception ex)
        {
            options.SetBreakpoint(kind, previous);
            _breakpointError = $"Could not update breakpoints: {ex.Message}";
        }
        finally
        {
            _breakpointSettingsPending = false;
            await NotifyAsync();
        }
    }

    // Best-effort: control is advisory and the run may already have ended.
    private async Task SafeControlAsync(string? action, int? delayMs = null, string? answer = null)
    {
        if (string.IsNullOrEmpty(_sessionId))
        {
            return;
        }

        try
        {
            await _ai.SendControlAsync(_sessionId, action, delayMs: delayMs, answer: answer);
        }
        catch
        {
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
        _highlight = LiveHighlight.None;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        EventCountMetric.Add(-_metricEventCount);
        PayloadMetric.Add(-_metricPayloadBytes);
        ActivePagesMetric.Add(-1);
        _history.Dispose();
    }
}
