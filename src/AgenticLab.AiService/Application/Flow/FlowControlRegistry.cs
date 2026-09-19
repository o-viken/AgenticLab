using System.Collections.Concurrent;

namespace AgenticLab.AiService.Application.Flow;

/// <summary>
/// Tracks the active <see cref="FlowSession"/>s by id so the <c>POST /chat/control</c> endpoint can drive
/// the matching in-flight <c>POST /chat/stream</c> run (pause, resume, step, stop).
/// </summary>
public sealed class FlowControlRegistry
{
    internal const string MeterName = "AgenticLab.AiService.Flow";
    private static readonly System.Diagnostics.Metrics.Meter Meter = new(MeterName);
    private readonly ConcurrentDictionary<string, FlowSession> _sessions = new();

    internal int ActiveCount => _sessions.Count;

    /// <summary>Creates a registry and exposes its active-session gauge.</summary>
    public FlowControlRegistry()
    {
        Meter.CreateObservableGauge("flow.active_sessions", () => _sessions.Count,
            description: "Backend flow sessions currently awaiting or executing.");
    }

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
