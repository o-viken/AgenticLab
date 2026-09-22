namespace AgenticLab.Web.Flow;

/// <summary>
/// Shared risk-meter and core tool-resource lookups. Host names and modes come from the backend
/// catalogue; optional branding, ordering and product links belong to example manifests.
/// </summary>
internal static class VendorCatalog
{
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
