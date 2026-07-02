using Microsoft.Extensions.AI;
using TheSeries.AiService.Application;

namespace TheSeries.AiService.Demo.Agents;

/// <summary>
/// A simple assistant that answers questions about the current time using a tool discovered from a
/// remote Model Context Protocol (MCP) server, demonstrating real MCP tool discovery and use.
/// </summary>
public sealed class TimeKeeperAgent(McpToolProvider mcp) : AgentDefinitionBase
{
    /// <inheritdoc />
    public override string Name => "TimeKeeper";

    /// <inheritdoc />
    public override string Description => "Tells the current time using a tool discovered from an MCP server.";

    /// <inheritdoc />
    public override bool SupportsMcp => true;

    /// <inheritdoc />
    /// <remarks>Low risk: read-only time lookup over MCP with no side effects.</remarks>
    public override AgentRiskLevel RiskLevel => AgentRiskLevel.Low;

    /// <inheritdoc />
    public override IReadOnlyList<string> Guardrails =>
    [
        "MCP tools only — no file-system, command or write access",
        "Read-only: discovers and calls remote MCP tools without changing anything",
    ];

    /// <inheritdoc />
    protected override string Persona =>
        "You are TimeKeeper, a precise assistant for date and time questions. " +
        "When asked about the current time or date, call the GetCurrentTime tool discovered from the MCP " +
        "server rather than guessing, then state the result clearly. Keep replies short.";

    /// <inheritdoc />
    public override IList<AITool> Tools => mcp.GetTools();
}
