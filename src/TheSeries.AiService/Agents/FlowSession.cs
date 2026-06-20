using System.Collections.Concurrent;

namespace TheSeries.AiService.Agents;

/// <summary>
/// Holds the live stepping state for one in-flight flow run so the UI can drive the pace of the
/// <em>actual</em> agent execution. The <see cref="FlowTracer"/> awaits <see cref="WaitForStepAsync"/>
/// before producing each step, which suspends consumption of the agent stream — and therefore the real
/// tool calls and model round-trips — until the client allows the next step. This keeps the animation in
/// sync with the backend (and its telemetry).
/// </summary>
public sealed class FlowSession : IDisposable
{
    private readonly SemaphoreSlim _advance = new(0);
    private readonly CancellationTokenSource _stop = new();

    /// <param name="id">The unique id shared by the stream and the control calls.</param>
    /// <param name="manual">When <c>true</c>, each step waits for an explicit <see cref="Advance"/>.</param>
    /// <param name="delayMs">In auto mode, the pause applied before each step, in milliseconds.</param>
    public FlowSession(string id, bool manual, int delayMs)
    {
        Id = id;
        Manual = manual;
        DelayMs = delayMs;
    }

    /// <summary>The unique id shared by the stream request and the control calls.</summary>
    public string Id { get; }

    /// <summary>When <c>true</c>, steps wait for an explicit advance signal; otherwise they auto-advance.</summary>
    public volatile bool Manual;

    /// <summary>When <c>true</c> (auto mode only), stepping is held until resumed.</summary>
    public volatile bool Paused;

    /// <summary>The auto-mode delay applied before each step, in milliseconds.</summary>
    public volatile int DelayMs;

    /// <summary>
    /// The run's interactive user-input scope, set by the <see cref="FlowTracer"/>, so the
    /// <c>POST /chat/control</c> endpoint can deliver an answer to a tool that asked the user a
    /// question. Null until the run opens one.
    /// </summary>
    public UserInputScope? UserInput { get; set; }

    /// <summary>A token that is cancelled when the run is stopped by the client.</summary>
    public CancellationToken StopToken => _stop.Token;

    /// <summary>Releases one manual step.</summary>
    public void Advance() => _advance.Release();

    /// <summary>Stops the run, cancelling any pending wait and the agent execution.</summary>
    public void Stop()
    {
        if (!_stop.IsCancellationRequested)
        {
            _stop.Cancel();
        }
    }

    /// <summary>
    /// Suspends until it is time to produce the next step: in manual mode this waits for an
    /// <see cref="Advance"/>; in auto mode it honours <see cref="Paused"/> and then waits <see cref="DelayMs"/>.
    /// Re-evaluates the mode periodically so switching mode or resuming mid-wait takes effect.
    /// </summary>
    /// <param name="cancellationToken">A token to abort the wait.</param>
    public async Task WaitForStepAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Manual)
            {
                // Poll so a switch to auto / resume / stop is observed promptly.
                if (await _advance.WaitAsync(150, cancellationToken))
                {
                    return;
                }

                continue;
            }

            if (Paused)
            {
                await Task.Delay(100, cancellationToken);
                continue;
            }

            if (DelayMs > 0)
            {
                await Task.Delay(DelayMs, cancellationToken);
            }

            return;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _advance.Dispose();
        _stop.Dispose();
    }
}

/// <summary>
/// Tracks the active <see cref="FlowSession"/>s by id so the <c>POST /chat/control</c> endpoint can drive
/// the matching in-flight <c>POST /chat/stream</c> run (pause, resume, step, stop).
/// </summary>
public sealed class FlowControlRegistry
{
    private readonly ConcurrentDictionary<string, FlowSession> _sessions = new();

    /// <summary>Creates and registers a session for the given id, replacing any existing one.</summary>
    /// <param name="id">The unique session id.</param>
    /// <param name="manual">Whether the run starts in manual stepping mode.</param>
    /// <param name="delayMs">The initial auto-mode step delay, in milliseconds.</param>
    public FlowSession Create(string id, bool manual, int delayMs)
    {
        var session = new FlowSession(id, manual, delayMs);
        if (_sessions.TryRemove(id, out var existing))
        {
            existing.Dispose();
        }

        _sessions[id] = session;
        return session;
    }

    /// <summary>Looks up a session by id.</summary>
    public bool TryGet(string id, out FlowSession session) => _sessions.TryGetValue(id, out session!);

    /// <summary>Removes and disposes the session for the given id.</summary>
    public void Remove(string id)
    {
        if (_sessions.TryRemove(id, out var session))
        {
            session.Dispose();
        }
    }
}
