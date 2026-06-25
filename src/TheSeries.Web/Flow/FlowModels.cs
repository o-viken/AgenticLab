namespace TheSeries.Web.Flow;

/// <summary>How the flow run is paced: automatically on a timer, or one step per user click.</summary>
internal enum FlowMode
{
    Auto,
    Manual,
}

/// <summary>
/// Which tab is active in the left Controls panel, reorganised from two stacked cards into a tabbed view.
/// <list type="bullet">
/// <item><description><see cref="Chat"/>: the agent picker, message box, run/stepping buttons and conversation log (FlowChat).</description></item>
/// <item><description><see cref="Settings"/>: step delay, stepping mode, tool toggles and workspace input (FlowControls).</description></item>
/// <item><description><see cref="Workspace"/>: the workspace's discovered skills and custom instructions.</description></item>
/// <item><description><see cref="Telemetry"/>: live run figures — turns, context size, persona/tools chars and event count.</description></item>
/// </list>
/// </summary>
internal enum ControlsTab
{
    Chat,
    Settings,
    Workspace,
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
/// One completed exchange in the conversation transcript: the user's message, the responding agent,
/// its final reply (or error) and the full ordered flow history for that turn.
/// </summary>
internal sealed record ConversationTurn(
    string Message,
    string? Agent,
    string Reply,
    string? Error,
    IReadOnlyList<FlowEvent> Events);

/// <summary>A skill discovered in the workspace and offered to the agent: its name and one-line description.</summary>
internal sealed record SkillChip(string Name, string Description);

/// <summary>A custom instruction discovered in the workspace and always injected: its name and one-line description.</summary>
internal sealed record InstructionChip(string Name, string Description);

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
/// A vendor-scoped agent choice: the backend agent name plus the product-flavoured label shown in the picker.
/// </summary>
internal sealed record AgentChoice(string Name, string Label);
