namespace TheSeries.AiService.Application;

/// <summary>
/// A user-authored agent discovered in the active workspace's <c>agents/</c> folder (one
/// <c>&lt;name&gt;.agent.yaml</c> file per agent). Unlike the built-in <see cref="IAgentDefinition"/>s
/// these are not registered at start-up; they are loaded per request from the workspace and turned into
/// a runnable agent on demand by the <see cref="WorkspaceAgentResolver"/>.
/// </summary>
/// <param name="Name">The unique name used to select the agent (from the YAML <c>name</c>).</param>
/// <param name="Description">A short description of what the agent is good at.</param>
/// <param name="Persona">The agent's persona/system instructions, layered on top of the shared harness prompt.</param>
/// <param name="ToolNames">The names of the existing backend tool functions the agent may call.</param>
/// <param name="RiskLevel">How much real-world impact the agent can have.</param>
/// <param name="Guardrails">The safety mechanisms enforced for the agent, surfaced so the user understands the risk.</param>
/// <param name="SupportsSkills">Whether the agent participates in workspace skills (its catalogue is injected per run).</param>
/// <param name="RelativePath">The workspace-relative path of the YAML file the agent was loaded from.</param>
public sealed record WorkspaceAgentDefinition(
    string Name,
    string Description,
    string Persona,
    IReadOnlyList<string> ToolNames,
    AgentRiskLevel RiskLevel,
    IReadOnlyList<string> Guardrails,
    bool SupportsSkills,
    string RelativePath);
