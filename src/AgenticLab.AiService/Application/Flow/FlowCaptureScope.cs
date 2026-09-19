using System.Collections.Concurrent;

namespace AgenticLab.AiService.Application.Flow;

/// <summary>
/// Records a single LLM round-trip captured during an agent run: the exact data sent to the model
/// and the response it returned.
/// </summary>
/// <param name="turnNumber">The 1-based index of this round-trip within the run.</param>
/// <param name="requestData">A human-readable rendering of the messages and tool definitions sent to the model.</param>
/// <param name="requestSummary">A short, single-line summary of what the harness sent the model this turn.</param>
public sealed class LlmTurn(int turnNumber, string requestData, string requestSummary)
{
    /// <summary>The 1-based index of this LLM round-trip within the run.</summary>
    public int TurnNumber { get; } = turnNumber;

    /// <summary>A human-readable rendering of the messages and tool definitions sent to the model.</summary>
    public string RequestData { get; } = requestData;

    /// <summary>A short, single-line summary of what the harness sent the model this turn (message counts, tools offered, newest message).</summary>
    public string RequestSummary { get; } = requestSummary;

    /// <summary>A human-readable rendering of the model's response (text and/or tool calls); null until completed.</summary>
    public string? ResponseData { get; internal set; }

    /// <summary>A short, single-line summary of what the model returned this turn (its answer, or the tool calls it requested); null until completed.</summary>
    public string? ResponseSummary { get; internal set; }
}

/// <summary>
/// An ambient, per-run sink that the <see cref="CapturingChatClient"/> writes each LLM round-trip into,
/// so the <see cref="FlowTracer"/> can surface the real request/response data in the flow UI.
/// The scope lives in an <see cref="AsyncLocal{T}"/> so concurrent runs stay isolated and the shared
/// singleton chat client only captures while a scope is active.
/// </summary>
public sealed class FlowCaptureScope : IDisposable
{
    private static readonly AsyncLocal<FlowCaptureScope?> CurrentScope = new();
    private readonly ConcurrentQueue<LlmTurn> _turns = new();
    private int _count;

    private FlowCaptureScope()
    {
    }

    /// <summary>The scope active on the current async context, or null when capturing is not enabled.</summary>
    public static FlowCaptureScope? Current => CurrentScope.Value;

    /// <summary>Starts a new capture scope on the current async context. Dispose to end it.</summary>
    /// <returns>The newly started scope.</returns>
    public static FlowCaptureScope Begin()
    {
        var scope = new FlowCaptureScope();
        CurrentScope.Value = scope;
        return scope;
    }

    /// <summary>
    /// Re-asserts this scope as the active one on the current async context. The value lives in an
    /// <see cref="AsyncLocal{T}"/>, which is reset whenever the owning async iterator resumes after a
    /// <c>yield return</c>; callers must re-activate before driving deeper async work (e.g. advancing the
    /// agent for its next LLM round-trip) so later turns are captured too.
    /// </summary>
    public void Activate() => CurrentScope.Value = this;

    /// <summary>A snapshot of the turns captured so far, in order.</summary>
    public IReadOnlyList<LlmTurn> Turns => _turns.ToArray();

    /// <summary>Records the start of a new LLM round-trip and returns the turn to complete later.</summary>
    /// <param name="requestData">A rendering of the request sent to the model.</param>
    /// <param name="requestSummary">A short, single-line summary of the request.</param>
    /// <returns>The newly created turn.</returns>
    internal LlmTurn AddTurn(string requestData, string requestSummary)
    {
        var turn = new LlmTurn(Interlocked.Increment(ref _count), requestData, requestSummary);
        _turns.Enqueue(turn);
        return turn;
    }

    /// <summary>Ends the scope, clearing it from the current async context.</summary>
    public void Dispose()
    {
        if (ReferenceEquals(CurrentScope.Value, this))
        {
            CurrentScope.Value = null;
        }
    }
}
