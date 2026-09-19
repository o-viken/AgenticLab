using System.Threading.Channels;

namespace AgenticLab.AiService.Application.Flow;

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
    private readonly object _breakpointLock = new();
    private HashSet<string> _breakpoints = new(StringComparer.Ordinal);
    private TaskCompletionSource? _breakpointRelease;
    private BreakpointNotice? _breakpoint;

    internal Channel<BreakpointNotice> BreakpointEvents { get; } = Channel.CreateUnbounded<BreakpointNotice>();

    /// <summary>The supported execution breakpoint names accepted by the chat API.</summary>
    public static IReadOnlySet<string> BreakpointKinds { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "before-model", "after-model", "before-tool", "after-tool",
    };

    /// <summary>Replaces the enabled breakpoints for future execution boundaries.</summary>
    public void SetBreakpoints(IEnumerable<string> breakpoints)
    {
        var selected = new HashSet<string>(breakpoints, StringComparer.Ordinal);
        if (selected.Any(kind => !BreakpointKinds.Contains(kind)))
        {
            throw new ArgumentException("Unknown breakpoint kind.", nameof(breakpoints));
        }

        lock (_breakpointLock)
        {
            _breakpoints = selected;
        }
    }

    /// <summary>Pauses at an enabled execution boundary until explicitly released or cancelled.</summary>
    public async Task WaitForBreakpointAsync(string kind, string? tool, CancellationToken cancellationToken)
    {
        Task release;
        lock (_breakpointLock)
        {
            if (!_breakpoints.Contains(kind))
            {
                return;
            }

            _breakpoint = new BreakpointNotice(Guid.NewGuid().ToString("n"), kind, tool, true, Manual);
            _breakpointRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            release = _breakpointRelease.Task;
            while (_advance.Wait(0)) { }
            BreakpointEvents.Writer.TryWrite(_breakpoint);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, StopToken);
        await release.WaitAsync(linked.Token);
    }

    /// <summary>Releases the identified breakpoint into auto or manual mode; stale controls are rejected.</summary>
    public bool ReleaseBreakpoint(string id, bool manual)
    {
        lock (_breakpointLock)
        {
            if (_breakpoint?.Id != id || _breakpointRelease is null)
            {
                return false;
            }

            Manual = manual;
            Paused = false;
            BreakpointEvents.Writer.TryWrite(_breakpoint with { Paused = false, Manual = manual });
            var release = _breakpointRelease;
            _breakpointRelease = null;
            _breakpoint = null;
            if (manual)
            {
                _advance.Release();
            }
            release.TrySetResult();
            return true;
        }
    }

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
    public void Advance()
    {
        lock (_breakpointLock)
        {
            if (_breakpoint is null && _advance.CurrentCount == 0)
            {
                _advance.Release();
            }
        }
    }

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
        Stop();
        BreakpointEvents.Writer.TryComplete();
        _advance.Dispose();
        _stop.Dispose();
    }
}
