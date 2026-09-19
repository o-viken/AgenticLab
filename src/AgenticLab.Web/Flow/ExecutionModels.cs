namespace AgenticLab.Web.Flow;

/// <summary>The visible Context entries and captured prompt size at a live or replay position.</summary>
/// <param name="History">Compact entries from exchanges before the displayed exchange.</param>
/// <param name="Current">Content available through the displayed stage, in playback order.</param>
/// <param name="Chars">Captured prompt size, or null before a request is available at this position.</param>
internal sealed record ContextSnapshot(
    IReadOnlyList<ContextEntry> History,
    IReadOnlyList<ContextEntry> Current,
    int? Chars)
{
    /// <summary>An empty position without captured context.</summary>
    public static ContextSnapshot Empty { get; } = new([], [], null);

    /// <summary>The number of visible content entries.</summary>
    public int Count => History.Count + Current.Count;

    /// <summary>The prompt-size bar width, using the same scale as live Context.</summary>
    public int BarWidth => Math.Min(100, (Chars ?? 0) / 80);
}

/// <summary>How an exchange ended, so a partial run is never shown as a completed one.</summary>
internal enum ExchangeStatus
{
    /// <summary>Still executing.</summary>
    Running,

    /// <summary>The harness delivered an answer.</summary>
    Completed,

    /// <summary>Execution ended before an answer was delivered (the user stopped it, or the stream broke).</summary>
    Stopped,

    /// <summary>The run reported an error.</summary>
    Failed,
}

/// <summary>
/// One LLM round-trip inside an exchange, with the stages captured for it in causal order: the request
/// the harness sent, the model's response, then the tool calls that response asked for and their results.
/// </summary>
/// <param name="Number">The 1-based round-trip number within the exchange.</param>
/// <param name="Stages">The captured stages, ordered for reading.</param>
internal sealed record ExecutionTurn(int Number, IReadOnlyList<FlowEvent> Stages);

/// <summary>
/// One exchange in the conversation — everything that happened between the user sending a message and
/// the harness delivering an answer (or failing). Groups the captured events into the model round-trips
/// they belong to, and records the settings the run actually used so history never reports the values
/// currently selected in the controls.
/// </summary>
/// <param name="Id">A stable identifier, assigned when the message is sent and kept when the run is archived.</param>
/// <param name="Number">The 1-based position in the conversation.</param>
/// <param name="Message">The user's message.</param>
/// <param name="Agent">The agent that ran, as selected at send time.</param>
/// <param name="Vendor">The vendor harness key used for the run.</param>
/// <param name="Workspace">The workspace path the run used, when it needed one.</param>
/// <param name="Reply">The delivered answer, empty until one arrives.</param>
/// <param name="Error">The failure message, or null.</param>
/// <param name="Status">How the exchange ended.</param>
/// <param name="Intake">Stages before the first round-trip (the message arriving, or an early error).</param>
/// <param name="Turns">The model round-trips.</param>
/// <param name="Outcome">The delivery stages (the final answer and/or the error).</param>
/// <param name="Stages">Every stage flattened in display order, for stepping.</param>
internal sealed record ExecutionExchange(
    string Id,
    int Number,
    string Message,
    string? Agent,
    string? Vendor,
    string? Workspace,
    string Reply,
    string? Error,
    ExchangeStatus Status,
    IReadOnlyList<FlowEvent> Intake,
    IReadOnlyList<ExecutionTurn> Turns,
    IReadOnlyList<FlowEvent> Outcome,
    IReadOnlyList<FlowEvent> Stages,
    IReadOnlyList<A2AChip>? A2AAgents = null)
{
    /// <summary>A one-line summary of the size of the capture, shown on the exchange row.</summary>
    public string Meta =>
        $"{Turns.Count} turn{(Turns.Count == 1 ? "" : "s")} · {Stages.Count} stage{(Stages.Count == 1 ? "" : "s")}";
}
