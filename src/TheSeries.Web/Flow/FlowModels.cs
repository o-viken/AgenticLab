namespace TheSeries.Web.Flow;

/// <summary>How the flow run is paced: automatically on a timer, or one step per user click.</summary>
internal enum FlowMode
{
    Auto,
    Manual,
}

/// <summary>
/// What the agent is doing right now, as announced in the conversation panel. Derived from the run
/// state (running/paused/awaiting/breakpoint/error), in the order the UI resolves it.
/// <list type="bullet">
/// <item><description><see cref="Idle"/>: no run has started (or the conversation was reset).</description></item>
/// <item><description><see cref="Thinking"/>: a turn is in flight and progressing on its own.</description></item>
/// <item><description><see cref="AwaitingStep"/>: manual stepping — the run is held until the user clicks Next.</description></item>
/// <item><description><see cref="Paused"/>: auto stepping was paused by the user.</description></item>
/// <item><description><see cref="AtBreakpoint"/>: held at an execution breakpoint (the boundary names it).</description></item>
/// <item><description><see cref="AwaitingAnswer"/>: a tool asked the user a question and is blocked on the reply.</description></item>
/// <item><description><see cref="Failed"/>: the run ended with an error.</description></item>
/// <item><description><see cref="Done"/>: the run finished and the answer is shown.</description></item>
/// </list>
/// </summary>
internal enum AgentActivity
{
    Idle,
    Thinking,
    AwaitingStep,
    Paused,
    AtBreakpoint,
    AwaitingAnswer,
    Failed,
    Done,
}

/// <summary>
/// Which tab is active in the left Controls panel, reorganised from two stacked cards into a tabbed view.
/// <list type="bullet">
/// <item><description><see cref="Chat"/>: the agent picker, message box, run/stepping buttons and conversation log (FlowChat).</description></item>
/// <item><description><see cref="Settings"/>: step delay, stepping mode, tool toggles, workspace input and the discovered skills/custom instructions (FlowControls).</description></item>
/// <item><description><see cref="Telemetry"/>: live run figures — turns, context size, persona/tools chars and event count.</description></item>
/// </list>
/// </summary>
internal enum ControlsTab
{
    Chat,
    Settings,
    Telemetry,
}

/// <summary>Which edge of the page a <c>SidePanel</c> docks to (and therefore which edge its splitter sits on).</summary>
public enum PanelSide
{
    Left,
    Right,
    Bottom,
}

/// <summary>
/// The level of detail shown in the diagram and Steps list.
/// <list type="bullet">
/// <item><description><see cref="Simple"/>: User ↔ Service only — the LLM is hidden inside the service (a black box).</description></item>
/// <item><description><see cref="NonTechnical"/>: User → Application → LLM (Client + AiService merged, no tools/loop).</description></item>
/// <item><description><see cref="Technical"/>: User → Client → AiService → LLM (no nested Tools box / resources).</description></item>
/// <item><description><see cref="Expert"/>: the complete harness view (Tools, resources, loop badge, every step).</description></item>
/// </list>
/// </summary>
internal enum Perspective
{
    Simple,
    NonTechnical,
    Technical,
    Expert,
}

/// <summary>
/// The vendor/brand whose look, agent roster, simulated model label and harness system prompt are
/// applied to the page. <see cref="Default"/> is the non-brand original look (no harness override).
/// </summary>
public enum Vendor
{
    Default,
    Copilot,
    ClaudeCode,
    Claude,
    ChatGpt,
    Gemini,
    Microsoft365,
}

/// <summary>
/// One exchange in the conversation: the user's message, the responding agent, its final reply (or
/// error) and the full ordered flow history for that turn. <paramref name="Id"/> is assigned when the
/// message is sent and survives archiving, so a selection in the Execution explorer stays put. The
/// vendor/workspace are recorded as they were at send time, so history never reports whatever the
/// controls happen to be set to now.
/// </summary>
internal sealed record ConversationTurn(
    string Id,
    string Message,
    string? Agent,
    string Reply,
    string? Error,
    IReadOnlyList<FlowEvent> Events,
    string? Vendor = null,
    string? Workspace = null,
    IReadOnlyList<A2AChip>? A2AAgents = null);

/// <summary>A skill discovered in the workspace and offered to the agent: its name and one-line description.</summary>
internal sealed record SkillChip(string Name, string Description);

/// <summary>
/// One suggestion shown in the workspace picker: the full folder <paramref name="Path"/> (used as the
/// value when picked), the <paramref name="Name"/> (the repo folder name, shown prominently) and whether
/// it is a <paramref name="IsRecent"/> previously-used workspace (vs a folder discovered under a base).
/// </summary>
internal sealed record WorkspaceSuggestion(string Path, string Name, bool IsRecent);

/// <summary>
/// One line of the workspace agent's tool-name mapping display: the declared token(s) from the agent
/// file (each rendered on its own line) and the backend tool they resolved to. <paramref name="Mapped"/>
/// is <c>null</c> and <paramref name="Dropped"/> is <c>true</c> when the token had no matching backend tool.
/// </summary>
internal sealed record ToolMappingLine(IReadOnlyList<string> Declared, string? Mapped, bool Dropped);

/// <summary>A custom instruction discovered in the workspace and always injected: its name and one-line description.</summary>
internal sealed record InstructionChip(string Name, string Description);

internal sealed record McpChip(string Name, string Description);

/// <summary>An agent reachable over the A2A protocol that the selected agent can delegate to: its name and description.</summary>
internal sealed record A2AChip(string Name, string Description);

/// <summary>A remote agent's captured delegation, without inferred internal execution.</summary>
internal sealed record A2AAgentView(A2AChip Agent, string Status, string? Question, string? Result);

/// <summary>Remote topology and the currently observed A2A boundary.</summary>
internal sealed record A2AFlowView(IReadOnlyList<A2AAgentView> Agents, string? ActiveAgent, string? Direction)
{
    /// <summary>No discovered remote agents or captured boundary.</summary>
    public static A2AFlowView Empty { get; } = new([], null, null);
}

/// <summary>
/// One chip in the anatomy Context stack: a piece of content carried into the model's context.
/// History entries come from earlier conversation turns (rendered dimmed); the rest come from the
/// current run. <paramref name="Source"/> ("user"/"agent"/"app") selects the contributor colour;
/// <paramref name="Turn"/> is the LLM round-trip badge (null for compact history chips).
/// </summary>
internal sealed record ContextEntry(string Label, string Source, string Preview, int? Turn = null, bool History = false);

/// <summary>Describes an external resource a tool reaches out to, shown as a node below the harness.</summary>
internal sealed record ResourceInfo(string Key, string Icon, string Title, string Transport, string[] ToolNames);

/// <summary>
/// One slice of a prompt-signature bar: a category of content sent to the model (system prompt, user
/// message, assistant message or tool result) and how many characters it contributed. The static tool
/// catalogue is excluded so the signature reflects only the conversation content.
/// </summary>
internal sealed record PromptSignatureCategory(string Key, string Label, int Chars);

/// <summary>
/// One conversation exchange (a single user message → final answer) in the delta ("growth") view: its
/// 1-based position, a <paramref name="Label"/> (the user's message), its per-category breakdown and total
/// char count, plus how that total splits into <paramref name="ReusedChars"/> (the prompt carried over from
/// the previous exchange — i.e. the previous exchange's total, re-sent as this one's prefix) and
/// <paramref name="AddedChars"/> (what this exchange newly appended, = total − reused). Stacking these
/// oldest→newest shows each conversation adding onto the previous one, and the totals chain exactly
/// (previous total + added = this total).
/// </summary>
internal sealed record PromptSignatureRequest(
    int Index,
    string Label,
    IReadOnlyList<PromptSignatureCategory> Categories,
    int TotalChars,
    int ReusedChars,
    int AddedChars);

/// <summary>
/// A breakdown of the most recent conversation exchange's request by message category, compared against the
/// previous exchange: the per-category char counts for both, their totals, and a <paramref name="MatchPercent"/>
/// stability score (the share of the current exchange's prompt byte-identical to the previous one — i.e. how
/// much of the prefix is reused, the way prompt caching measures it). <paramref name="HasPrevious"/> is false
/// on the first exchange (no earlier one to compare with). <paramref name="Requests"/> holds every exchange of
/// the conversation (oldest→newest) for the delta/growth view.
/// </summary>
internal sealed record PromptSignatureView(
    IReadOnlyList<PromptSignatureCategory> Previous,
    IReadOnlyList<PromptSignatureCategory> Current,
    int PreviousChars,
    int CurrentChars,
    int MatchPercent,
    bool HasPrevious,
    bool HasCurrent,
    IReadOnlyList<PromptSignatureRequest> Requests)
{
    /// <summary>An empty signature shown before the first LLM request of a run.</summary>
    public static readonly PromptSignatureView Empty = new(
        Array.Empty<PromptSignatureCategory>(), Array.Empty<PromptSignatureCategory>(), 0, 0, 0, false, false,
        Array.Empty<PromptSignatureRequest>());

    /// <summary>The largest single exchange total across the conversation, used to scale the delta bars.</summary>
    public int MaxRequestChars => Requests.Count == 0 ? 0 : Requests.Max(r => r.TotalChars);
}

/// <summary>
/// The two harness-anatomy sizes surfaced as separate numbers: the <paramref name="PersonaChars"/> (the
/// <em>agent prompt</em> — the persona text inside the instructions' <c>&lt;agentMode&gt;</c> tags) and the
/// <paramref name="ToolsChars"/> (the <em>tools available</em> — the tool catalogue's name + description +
/// parameters, which the Prompt signature deliberately excludes from its conversation totals).
/// </summary>
internal sealed record AnatomySizes(int PersonaChars, int ToolsChars)
{
    /// <summary>No request captured yet (both sizes zero).</summary>
    public static readonly AnatomySizes Empty = new(0, 0);
}

/// <summary>
/// One alternative the (simulated) model could have emitted at a generation step: the candidate token
/// <paramref name="Text"/> and its fabricated <paramref name="Probability"/> (0–1). Shown as the hover
/// popover on a generated token to illustrate next-token sampling — these numbers are invented, not real.
/// </summary>
internal sealed record TokenCandidate(string Text, double Probability)
{
    /// <summary>The probability as a whole-number percentage for display (e.g. 71).</summary>
    public int Percent => (int)Math.Round(Probability * 100);
}

/// <summary>
/// One simulated token in the inference view: its <paramref name="Text"/>, the (fabricated)
/// <paramref name="Probability"/> the model "chose" it with, and the <paramref name="Candidates"/> it was
/// chosen from (empty for prompt tokens, which are not generated). Purely illustrative — no real tokenizer
/// or model logits are involved.
/// </summary>
internal sealed record SimToken(string Text, double Probability, IReadOnlyList<TokenCandidate> Candidates)
{
    /// <summary>The chosen token's probability as a whole-number percentage for display.</summary>
    public int Percent => (int)Math.Round(Probability * 100);

    /// <summary>Whether this token carries a candidate list to reveal on hover.</summary>
    public bool HasCandidates => Candidates.Count > 0;
}

/// <summary>
/// The simulated "inside the LLM" view (Expert · Inference toggle): the latest user message split into
/// <paramref name="PromptTokens"/> (word-piece chips), the final answer replayed as
/// <paramref name="ResponseTokens"/> (autoregressively generated chips), and a whole-prompt
/// <paramref name="PromptTokenEstimate"/> (≈ chars / 4 across the whole request). All of it is a
/// client-side fabrication for teaching — the backend does not expose tokens.
/// </summary>
internal sealed record InferenceView(
    IReadOnlyList<SimToken> PromptTokens,
    IReadOnlyList<SimToken> ResponseTokens,
    int PromptTokenEstimate,
    bool HasPrompt,
    bool HasResponse)
{
    /// <summary>An empty inference view shown before the first request/answer of a run.</summary>
    public static readonly InferenceView Empty = new(
        Array.Empty<SimToken>(), Array.Empty<SimToken>(), 0, false, false);

    /// <summary>Number of tokens shown for the (latest) user message.</summary>
    public int PromptTokenCount => PromptTokens.Count;

    /// <summary>Number of generated tokens shown for the answer.</summary>
    public int ResponseTokenCount => ResponseTokens.Count;
}

/// <summary>
/// One token's fabricated embedding in the Embeddings view: its <paramref name="Text"/>, a small fixed-length
/// <paramref name="Vector"/> (each component in −1…1, rendered as a diverging heatmap strip) and a
/// <paramref name="X"/>/<paramref name="Y"/> position in 0…1 (the vector projected onto two fixed axes) used
/// for the 2-D "meaning map" scatter. Identical tokens get identical vectors, so they land on the same spot.
/// The numbers are invented client-side (deterministic) — not a real embedding model.
/// </summary>
internal sealed record EmbeddingToken(string Text, IReadOnlyList<double> Vector, double X, double Y);

/// <summary>
/// The simulated embeddings/neural-network view (Expert · Embeddings &amp; network toggle): the latest
/// prompt's meaningful tokens turned into fake <paramref name="Tokens"/> (vector + 2-D position), the vector
/// <paramref name="Dimensions"/> shown, and the optional <paramref name="PredictedToken"/> the symbolic
/// forward-pass diagram resolves to (the run's first generated token, when an answer exists). Purely
/// illustrative — the backend exposes no embeddings or network internals.
/// </summary>
internal sealed record EmbeddingsView(
    IReadOnlyList<EmbeddingToken> Tokens,
    int Dimensions,
    string? PredictedToken)
{
    /// <summary>An empty view shown before the first request of a run.</summary>
    public static readonly EmbeddingsView Empty = new(Array.Empty<EmbeddingToken>(), 0, null);

    /// <summary>Whether any token embeddings are available to show.</summary>
    public bool HasTokens => Tokens.Count > 0;
}

/// <summary>
/// A vendor-scoped agent choice: the backend agent name plus the product-flavoured label shown in the picker.
/// </summary>
internal sealed record AgentChoice(string Name, string Label);
