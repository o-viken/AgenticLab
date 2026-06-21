namespace TheSeries.Web.Flow;

/// <summary>
/// Static catalogue of Web-side vendor/brand data: the CSS palette class for a vendor and its backend
/// vendor key, the risk-meter fill for a level, and the external resources the tools can reach. All the
/// per-vendor metadata (display name, simulated model label and the modes each vendor offers — including
/// the non-brand Default vendor) lives on the backend vendor definitions and is loaded at runtime via
/// <c>GET /vendors</c>; only the CSS palette and the enum→key mapping are kept here. Pure lookups with no
/// UI dependency, shared by the page's view state and the diagram components.
/// </summary>
internal static class VendorCatalog
{
    /// <summary>The CSS class applied to the root so the vendor's brand palette overrides take effect.</summary>
    public static string CssClass(Vendor vendor) => vendor switch
    {
        Vendor.Copilot => "theme-copilot",
        Vendor.ClaudeCode => "theme-claude-code",
        Vendor.Claude => "theme-claude",
        Vendor.ChatGpt => "theme-chatgpt",
        Vendor.Gemini => "theme-gemini",
        Vendor.Microsoft365 => "theme-m365",
        _ => string.Empty,
    };

    /// <summary>
    /// The vendor key sent to the backend so it swaps in this vendor's harness system prompt for the run
    /// (replacing the shared harness while keeping the agent's persona), and the key used to look up the
    /// vendor's loaded metadata. The non-brand <see cref="Vendor.Default"/> maps to <c>default</c>, whose
    /// backend harness is empty, so the agent keeps its own harness.
    /// </summary>
    public static string? HarnessKey(Vendor vendor) => vendor switch
    {
        Vendor.Copilot => "copilot",
        Vendor.ClaudeCode => "claude-code",
        Vendor.Claude => "claude",
        Vendor.ChatGpt => "chatgpt",
        Vendor.Gemini => "gemini",
        Vendor.Microsoft365 => "microsoft365",
        _ => "default",
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
