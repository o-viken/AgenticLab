namespace TheSeries.Web.Flow;

/// <summary>
/// Static catalogue of theme-related data: which agents each theme offers, the CSS/display name for a
/// theme, the risk-meter fill for a level, and the external resources the tools can reach. Pure lookups
/// with no UI dependency, shared by the page's view state and the diagram components.
/// </summary>
internal static class ThemeCatalog
{
    /// <summary>
    /// Which agents each theme offers, in display order. The label is what the picker shows (the product's
    /// "mode" name); the name is the backend agent it maps to. Several themes may reuse the same agent.
    /// </summary>
    public static readonly IReadOnlyDictionary<Theme, AgentChoice[]> ThemeAgents = new Dictionary<Theme, AgentChoice[]>
    {
        [Theme.Default] = new[] { new AgentChoice("WikiAssistant", "wiki"), new AgentChoice("ChatBot", "chat") },
        [Theme.Copilot] = new[] { new AgentChoice("Ask", "ask"), new AgentChoice("Plan", "plan"), new AgentChoice("Coder", "agent") },
        [Theme.ChatGpt] = new[] { new AgentChoice("ChatBot", "chat") },
        [Theme.Gemini] = new[] { new AgentChoice("ChatBot", "chat") },
        [Theme.Claude] = new[] { new AgentChoice("ChatBot", "chat") },
        [Theme.ClaudeCode] = new[] { new AgentChoice("Plan", "plan"), new AgentChoice("Coder", "agent") },
        [Theme.Microsoft365] = new[] { new AgentChoice("M365Copilot", "chat"), new AgentChoice("M365Researcher", "researcher"), new AgentChoice("M365Analyst", "analyst") },
    };

    /// <summary>The CSS class applied to the root so the brand theme's variable overrides take effect.</summary>
    public static string ThemeClass(Theme theme) => theme switch
    {
        Theme.Copilot => "theme-copilot",
        Theme.ClaudeCode => "theme-claude-code",
        Theme.Claude => "theme-claude",
        Theme.ChatGpt => "theme-chatgpt",
        Theme.Gemini => "theme-gemini",
        Theme.Microsoft365 => "theme-m365",
        _ => string.Empty,
    };

    /// <summary>The friendly display name of the theme (shown in the page title and header).</summary>
    public static string ThemeName(Theme theme) => theme switch
    {
        Theme.Copilot => "GitHub Copilot",
        Theme.ClaudeCode => "Claude Code",
        Theme.Claude => "Claude",
        Theme.ChatGpt => "ChatGPT",
        Theme.Gemini => "Gemini",
        Theme.Microsoft365 => "Microsoft 365 Copilot",
        _ => "Default",
    };

    /// <summary>
    /// The simulated provider/model label shown on the LLM node for a brand theme, to mimic that the
    /// product runs on its own model (e.g. Claude on Anthropic, ChatGPT on GPT). This is presentation
    /// only — the real backend is always Azure OpenAI. Returns an empty string for the non-brand Default
    /// theme, where the real deployment is shown instead.
    /// </summary>
    public static string ThemeModelLabel(Theme theme) => theme switch
    {
        Theme.Copilot => "GPT-5 (GitHub Copilot)",
        Theme.ClaudeCode => "Claude Sonnet 4.5 (Anthropic)",
        Theme.Claude => "Claude Sonnet 4.5 (Anthropic)",
        Theme.ChatGpt => "GPT-5 (OpenAI)",
        Theme.Gemini => "Gemini 2.5 Pro (Google)",
        Theme.Microsoft365 => "GPT-4o (Microsoft)",
        _ => string.Empty,
    };

    /// <summary>How many of the three risk-meter segments are filled for a given risk level.</summary>
    public static int RiskFilledSegments(string riskLevel) => riskLevel switch
    {
        "High" => 3,
        "Medium" => 2,
        "Low" => 1,
        _ => 0,
    };

    /// <summary>The external resources the tools can reach. Each maps one or more tool names to a transport.</summary>
    public static readonly ResourceInfo[] KnownResources =
    {
        new("wikipedia", "🌐", "Wikipedia", "HTTPS · en.wikipedia.org", new[] { "SearchWiki", "GetWikiPage" }),
        new("compute", "🧮", "Local compute", "In-process · CPU", new[] { "Calculate" }),
        new("workspace", "📁", "Workspace", "Local file system", new[] { "ReadFile", "ListFiles", "WriteFile", "DeleteFile" }),
        new("shell", "⌨️", "Terminal", "Local process · allowlisted", new[] { "RunCommand" }),
        new("skills", "📚", "Skills", "Local · workspace SKILL.md", new[] { "ReadSkill" }),
        new("microsoft365", "🗂️", "Microsoft 365", "Microsoft Graph · sample data", new[] { "SearchEmail", "SearchFiles", "SearchChats", "GetCalendar", "FindPeople", "SummarizeDocument", "SendMail" }),
    };

    /// <summary>
    /// The resource backing the given active tool, or null when no tool is active or none matches.
    /// Resources are only shown while their tool runs — never before or after.
    /// </summary>
    public static ResourceInfo? ActiveResource(string? activeTool) =>
        activeTool is null ? null : KnownResources.FirstOrDefault(r => r.ToolNames.Contains(activeTool));
}
