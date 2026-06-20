namespace TheSeries.Web.Flow;

/// <summary>How the flow run is paced: automatically on a timer, or one step per user click.</summary>
internal enum FlowMode
{
    Auto,
    Manual,
}

/// <summary>
/// How the page arranges the controls relative to the flow diagram.
/// <list type="bullet">
/// <item><description><see cref="Stacked"/>: controls bar above the flow (the original layout).</description></item>
/// <item><description><see cref="Split"/>: controls in a left column, the flow in a right column.</description></item>
/// </list>
/// </summary>
internal enum FlowLayout
{
    Stacked,
    Split,
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

/// <summary>The visual theme applied to the page (a brand palette, or the original "Default" look).</summary>
public enum Theme
{
    Default,
    Copilot,
    ClaudeCode,
    Claude,
    ChatGpt,
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
/// A theme-scoped agent choice: the backend agent name plus the product-flavoured label shown in the picker.
/// </summary>
internal sealed record AgentChoice(string Name, string Label);
