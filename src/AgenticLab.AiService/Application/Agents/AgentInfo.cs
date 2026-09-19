namespace AgenticLab.AiService.Application.Agents;

/// <summary>
/// A user-facing summary of a selectable agent.
/// </summary>
/// <param name="Name">The unique name used to select the agent.</param>
/// <param name="Description">A short description of what the agent is good at.</param>
/// <param name="Tools">The names of the tools this agent may call.</param>
/// <param name="RequiresWorkspace">Whether selecting this agent requires the caller to supply a workspace path.</param>
/// <param name="SupportsSkills">Whether this agent uses workspace skills (its names/descriptions are injected each run).</param>
/// <param name="SupportsMcp">Whether this agent uses tools discovered from a remote MCP server.</param>
/// <param name="SupportsA2A">Whether this agent can delegate to another agent over the A2A protocol.</param>
/// <param name="RiskLevel">How much real-world impact the agent can have (<c>None</c>, <c>Low</c>, <c>Medium</c>, <c>High</c>).</param>
/// <param name="Guardrails">The safety mechanisms enforced for this agent, surfaced so the user understands the risk.</param>
/// <param name="ModelId">The Azure OpenAI deployment the agent runs on, surfaced so clients can show which model answers.</param>
/// <param name="ToolMappings">For workspace agents, each declared tool token and the backend tool it mapped to; empty for built-in agents.</param>
public sealed record AgentInfo(string Name, string Description, IReadOnlyList<string> Tools, bool RequiresWorkspace, bool SupportsSkills, bool SupportsMcp, bool SupportsA2A, string RiskLevel, IReadOnlyList<string> Guardrails, string ModelId, IReadOnlyList<ToolMapping> ToolMappings);
