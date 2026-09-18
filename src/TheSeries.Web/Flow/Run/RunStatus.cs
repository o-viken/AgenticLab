namespace TheSeries.Web.Flow;

/// <summary>
/// The human-readable status of the run for the Conversation tab: what the agent is doing (resolved in
/// announcement order), the chip label and one-line note beside its entry, the live turn meta, the
/// composer hint and the prompt shown in the anatomy's "Set by user" layer.
/// </summary>
internal sealed class RunStatus(FlowRunController owner)
{
    /// <summary>What the agent is doing right now, resolved from the run state in announcement order.</summary>
    public AgentActivity Activity =>
        !string.IsNullOrWhiteSpace(owner.Error) ? AgentActivity.Failed
        : owner.AwaitingAnswer ? AgentActivity.AwaitingAnswer
        : owner.BreakpointPaused ? AgentActivity.AtBreakpoint
        : owner.Paused ? AgentActivity.Paused
        // AwaitingStep means a Next request is already in flight, so the run is moving again.
        : owner.Running && owner.View.Options.Mode == FlowMode.Manual && !owner.AwaitingStep ? AgentActivity.AwaitingStep
        : owner.Running ? AgentActivity.Thinking
        : !string.IsNullOrWhiteSpace(owner.Reply) ? AgentActivity.Done
        : AgentActivity.Idle;

    /// <summary>The short status shown beside the agent's name while a turn is active, or null.</summary>
    public string? AgentStatusLabel => Activity switch
    {
        AgentActivity.Failed => "Error",
        AgentActivity.AwaitingAnswer => "Waiting for you",
        AgentActivity.Paused => "Paused",
        AgentActivity.Thinking or AgentActivity.AwaitingStep or AgentActivity.AtBreakpoint => "In progress",
        _ => null,
    };

    /// <summary>Where the run has got to, named after the same execution boundaries as the breakpoints.</summary>
    public string? ProgressLabel => owner.Events.Count == 0 ? null : FlowEventMapping.ProgressLabelFor(owner.Events[^1]);

    /// <summary>The human-readable execution boundary currently holding the run, or null.</summary>
    public string? BreakpointReason => owner.Breakpoint is { } notice
        ? $"{RunOptions.BreakpointLabel(notice.Kind)}{(notice.Tool is null ? "" : $": {notice.Tool}")}"
        : null;

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
    public string? TurnMeta
    {
        get
        {
            var projections = owner.Projections;
            return owner.Running ? (projections.CurrentTurn == 0 ? null : $"turn {projections.CurrentTurn}")
                : projections.TotalTurns == 0 ? null
                : $"{projections.TotalTurns} turn{(projections.TotalTurns == 1 ? "" : "s")}";
        }
    }

    /// <summary>A note under the composer explaining why it cannot send right now, or null when it can.</summary>
    public string? ComposerHint =>
        owner.Running ? "A turn is active. Continue or stop it before sending another message."
        : owner.View.Agent.RequiresWorkspace && string.IsNullOrWhiteSpace(owner.View.Workspace)
            ? "This agent needs a workspace folder — set one under Settings."
            : null;

    /// <summary>The user's prompt for the current/next run, shown as the "Set by user" anatomy layer.</summary>
    public string CurrentUserPrompt =>
        !string.IsNullOrWhiteSpace(owner.RunMessage) ? owner.RunMessage
        : !string.IsNullOrWhiteSpace(owner.View.Message) ? owner.View.Message
        : "—";
}
