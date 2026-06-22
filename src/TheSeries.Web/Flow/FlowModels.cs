namespace TheSeries.Web.Flow;

/// <summary>How the flow run is paced: automatically on a timer, or one step per user click.</summary>
internal enum FlowMode
{
    Auto,
    Manual,
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
/// <item><description><see cref="NonTechnical"/>: User → Application → LLM (Client + AiService merged, no tools/loop).</description></item>
/// <item><description><see cref="Technical"/>: User → Client → AiService → LLM (no nested Tools box / resources).</description></item>
/// <item><description><see cref="Expert"/>: the complete harness view (Tools, resources, loop badge, every step).</description></item>
/// </list>
/// </summary>
internal enum Perspective
{
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
/// message, assistant message, tool result or the tool catalogue) and how many characters it contributed.
/// </summary>
internal sealed record PromptSignatureCategory(string Key, string Label, int Chars);

/// <summary>
/// A breakdown of the most recent LLM request by message category, compared against the previous request:
/// the per-category char counts for both, their totals, and a <paramref name="MatchPercent"/> stability
/// score (the share of the current request that is byte-identical to the previous one — i.e. how much of
/// the prompt prefix is reused, the way prompt caching measures it). <paramref name="HasPrevious"/> is
/// false on the first round-trip (no earlier request to compare with).
/// </summary>
internal sealed record PromptSignatureView(
    IReadOnlyList<PromptSignatureCategory> Previous,
    IReadOnlyList<PromptSignatureCategory> Current,
    int PreviousChars,
    int CurrentChars,
    int MatchPercent,
    bool HasPrevious,
    bool HasCurrent)
{
    /// <summary>An empty signature shown before the first LLM request of a run.</summary>
    public static readonly PromptSignatureView Empty = new(
        Array.Empty<PromptSignatureCategory>(), Array.Empty<PromptSignatureCategory>(), 0, 0, 0, false, false);
}

/// <summary>
/// A vendor-scoped agent choice: the backend agent name plus the product-flavoured label shown in the picker.
/// </summary>
internal sealed record AgentChoice(string Name, string Label);
