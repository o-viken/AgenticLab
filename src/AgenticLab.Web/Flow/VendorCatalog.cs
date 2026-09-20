namespace AgenticLab.Web.Flow;

/// <summary>
/// Static catalogue of Web-side vendor/brand data: the backend vendor key, the risk-meter fill for a
/// level, and the external resources the tools can reach. All the
/// per-vendor metadata (display name, simulated model label and the modes each vendor offers — including
/// the non-brand Default vendor) lives on the backend vendor definitions and is loaded at runtime via
/// <c>GET /vendors</c>; all vendors share the default palette. Pure lookups with no
/// UI dependency, shared by the page's view state and the diagram components.
/// </summary>
internal static class VendorCatalog
{
    /// <summary>Converts old enum-name preferences while preserving arbitrary registered host keys.</summary>
    public static string NormalizeKey(string value) => Enum.TryParse<Vendor>(value, true, out var vendor)
        && Enum.IsDefined(vendor) ? HarnessKey(vendor)! : value;

    /// <summary>Existing branding for built-in hosts; extensions use the neutral host icon.</summary>
    public static Vendor BuiltIn(string key) => Enum.GetValues<Vendor>()
        .FirstOrDefault(vendor => HarnessKey(vendor) == key);

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

    /// <summary>
    /// Preferred ordering for known branding keys, applied only to hosts returned by the backend.
    /// Includes legacy branding for optional examples without making disabled hosts available.
    /// </summary>
    public static readonly Vendor[] DisplayOrder =
    {
        Vendor.Default,
        Vendor.ChatGpt,
        Vendor.Gemini,
        Vendor.Copilot,
        Vendor.ClaudeCode,
        Vendor.Claude,
        Vendor.Microsoft365,
    };

    /// <summary>
    /// The product concept id for a vendor (for the ⓘ in the rail's hover tooltip), or null for the
    /// non-product Default vendor (which has no brand product concept).
    /// </summary>
    public static string? ProductConceptId(Vendor vendor) => vendor switch
    {
        Vendor.ChatGpt => "chatgpt",
        Vendor.Gemini => "gemini",
        Vendor.Copilot => "github-copilot",
        Vendor.ClaudeCode => "claude-code",
        Vendor.Claude => "claude",
        Vendor.Microsoft365 => "microsoft-365-copilot",
        _ => null,
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
        new("mcp", "🧩", "MCP server", "MCP · HTTP · mcpserver", new[] { "GetCurrentTime" }),
        new("a2a", "🤝", "Sub-agent (A2A)", "A2A · JSON-RPC · a2aserver", new[] { "DelegateToAgent" }),
    };

    /// <summary>
    /// The resource backing the given active tool, or null when no tool is active or none matches.
    /// Resources are only shown while their tool runs — never before or after.
    /// </summary>
    public static ResourceInfo? ActiveResource(string? activeTool) =>
        activeTool is null ? null : KnownResources.FirstOrDefault(r => r.ToolNames.Contains(activeTool));
}
