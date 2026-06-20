namespace TheSeries.AiService.Agents;

/// <summary>
/// An ambient, per-run scope that lets a tool pause the agent loop to ask the user a question and
/// resume once the answer arrives. The <see cref="Tools.AskQuestionTool"/> awaits
/// <see cref="WaitForAnswerAsync"/> (suspending the agent stream, exactly like
/// <see cref="FlowSession.WaitForStepAsync"/> does for stepping) and the <c>POST /chat/control</c>
/// endpoint releases it via <see cref="ProvideAnswer"/> when the user submits their reply.
/// </summary>
/// <remarks>
/// Like <see cref="WorkspaceScope"/>, <see cref="ToolFilterScope"/> and <see cref="FlowCaptureScope"/>
/// it lives in an <see cref="AsyncLocal{T}"/> so concurrent runs stay isolated. Only the streaming
/// (<c>/chat/stream</c>) path opens a scope; under the blocking <c>/chat</c> endpoint
/// <see cref="Current"/> is null and the tool returns a non-interactive fallback instead of blocking.
/// </remarks>
public sealed class UserInputScope : IDisposable
{
    private static readonly AsyncLocal<UserInputScope?> CurrentScope = new();
    private readonly object _gate = new();
    private TaskCompletionSource<string> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private UserInputScope()
    {
    }

    /// <summary>The scope active on the current async context, or null when no run is interactive.</summary>
    public static UserInputScope? Current => CurrentScope.Value;

    /// <summary>Starts a new user-input scope on the current async context. Dispose to end it.</summary>
    /// <returns>The newly started scope.</returns>
    public static UserInputScope Begin()
    {
        var scope = new UserInputScope();
        CurrentScope.Value = scope;
        return scope;
    }

    /// <summary>
    /// Re-asserts this scope as the active one on the current async context. The value lives in an
    /// <see cref="AsyncLocal{T}"/>, which is reset whenever an owning async iterator resumes after a
    /// <c>yield return</c>; callers driving the agent through such an iterator must re-activate before
    /// each advance so the tool can find the scope on every round-trip.
    /// </summary>
    public void Activate() => CurrentScope.Value = this;

    /// <summary>
    /// Suspends until the user answers the current question (or the run is cancelled). A fresh wait is
    /// armed on each call, so an agent may ask several questions within a single run.
    /// </summary>
    /// <param name="cancellationToken">A token that abandons the wait (e.g. when the run is stopped).</param>
    /// <returns>The answer the user supplied.</returns>
    public Task<string> WaitForAnswerAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<string> tcs;
        lock (_gate)
        {
            _answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            tcs = _answer;
        }

        cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        return tcs.Task;
    }

    /// <summary>Supplies the user's answer, releasing the tool waiting in <see cref="WaitForAnswerAsync"/>.</summary>
    /// <param name="answer">The text the user entered.</param>
    public void ProvideAnswer(string answer)
    {
        TaskCompletionSource<string> tcs;
        lock (_gate)
        {
            tcs = _answer;
        }

        tcs.TrySetResult(answer);
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
