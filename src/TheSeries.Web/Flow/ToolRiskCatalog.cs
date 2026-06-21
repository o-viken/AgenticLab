namespace TheSeries.Web.Flow;

/// <summary>
/// The risk tier of an individual tool, used to colour-code tools in the harness so that an agent's
/// capabilities are not shown as a flat list. Unlike an agent's overall <c>RiskLevel</c> (which has a
/// <c>None</c> tier for a tool-less chatbot), every tool <em>does</em> something, so the lowest tier here
/// is <see cref="Low"/>.
/// </summary>
internal enum ToolRisk
{
    /// <summary>Read-only or pure-compute: looks something up or calculates, with no side effects.</summary>
    Low,

    /// <summary>Acts in the world in a confined or reversible way (e.g. sends a message on the user's behalf).</summary>
    Medium,

    /// <summary>Writes/deletes files or runs commands on the host — hard-to-reverse, high-impact actions.</summary>
    High,
}

/// <summary>
/// Classifies a tool by name into a <see cref="ToolRisk"/> tier and explains why, so the UI can
/// distinguish an agent's read-only tools from the ones that change the world. The mapping mirrors the
/// backend's own capability split (the write/exec tools that drive an agent's <c>High</c> risk, and the
/// side-effecting <c>SendMail</c> that makes the M365 agent <c>Medium</c>); it is a deterministic,
/// presentational lookup, so it lives Web-side rather than reshaping the <c>GET /agents</c> contract.
/// Unknown tool names default to <see cref="ToolRisk.Low"/>.
/// </summary>
internal static class ToolRiskCatalog
{
    // Writes/deletes files or runs commands on the host.
    private static readonly HashSet<string> High = new(StringComparer.OrdinalIgnoreCase)
    {
        "WriteFile", "DeleteFile", "RunCommand",
    };

    // Acts in the world but in a confined/reversible way.
    private static readonly HashSet<string> Medium = new(StringComparer.OrdinalIgnoreCase)
    {
        "SendMail",
    };

    /// <summary>The risk tier of the tool with the given name (case-insensitive); unknown → <see cref="ToolRisk.Low"/>.</summary>
    public static ToolRisk Classify(string tool)
    {
        if (High.Contains(tool))
        {
            return ToolRisk.High;
        }

        return Medium.Contains(tool) ? ToolRisk.Medium : ToolRisk.Low;
    }

    /// <summary>The CSS class for a tool's risk tier (<c>risk-low</c>/<c>risk-medium</c>/<c>risk-high</c>),
    /// reusing the themed risk palette so it follows the active vendor.</summary>
    public static string CssClass(string tool) => Classify(tool) switch
    {
        ToolRisk.High => "risk-high",
        ToolRisk.Medium => "risk-medium",
        _ => "risk-low",
    };

    /// <summary>A short, human-readable explanation of why the tool has its risk tier, for a tooltip.</summary>
    public static string Reason(string tool) => Classify(tool) switch
    {
        ToolRisk.High => "High risk — writes/deletes files or runs commands on the host.",
        ToolRisk.Medium => "Medium risk — acts on your behalf (a side effect that's hard to undo).",
        _ => "Low risk — read-only or pure computation, no side effects.",
    };

    /// <summary>The tier's display label (<c>Low</c>/<c>Medium</c>/<c>High</c>).</summary>
    public static string Label(string tool) => Classify(tool).ToString();
}
