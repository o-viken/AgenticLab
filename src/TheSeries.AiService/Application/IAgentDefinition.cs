using Microsoft.Extensions.AI;

namespace TheSeries.AiService.Application;

/// <summary>
/// How much real-world impact an agent can have, used to communicate the risk of letting it run.
/// Higher levels mean the agent can cause side effects (write files, run commands) in the
/// environment it executes in, not just read or reason.
/// </summary>
public enum AgentRiskLevel
{
    /// <summary>No tools with side effects; the agent only reasons from its own knowledge.</summary>
    None,

    /// <summary>Read-only or computational tools (search, calculate, read files) with no side effects.</summary>
    Low,

    /// <summary>Can change state in a confined way (e.g. scoped writes) but not run arbitrary commands.</summary>
    Medium,

    /// <summary>Can write/delete files and execute commands on the host it runs on.</summary>
    High,
}

/// <summary>
/// Describes a selectable agent: the persona (system instructions) it runs with and the
/// subset of tools it may call. Implementations are registered in DI and composed into
/// concrete agents by <see cref="AgentCatalog"/>.
/// </summary>
public interface IAgentDefinition
{
    /// <summary>The unique name used to select this agent (case-insensitive), e.g. <c>WikiAssistant</c>.</summary>
    string Name { get; }

    /// <summary>A short, user-facing description of what this agent is good at.</summary>
    string Description { get; }

    /// <summary>The system instructions supplied to the agent on every run.</summary>
    string Instructions { get; }

    /// <summary>
    /// The default Azure OpenAI deployment this agent should run on, or <c>null</c> to use the global
    /// default. A <c>Agents:{Name}:Deployment</c> configuration value, when present, overrides this so an
    /// operator can pick a model per agent without changing code.
    /// </summary>
    string? ModelId { get; }

    /// <summary>
    /// Whether this agent requires the caller to supply a workspace path. When <c>true</c>, the chat
    /// endpoints reject a request that does not include a workspace, and a <see cref="WorkspaceScope"/>
    /// is opened for the run so the file-system and terminal tools have a root to operate against.
    /// </summary>
    bool RequiresWorkspace { get; }

    /// <summary>
    /// Whether this agent participates in workspace skills. When <c>true</c>, the chat endpoints discover
    /// the skills declared in the active workspace and inject their names and descriptions into the agent's
    /// instructions for the run, so the agent can load a skill's full content on demand. Implies a
    /// workspace is available.
    /// </summary>
    bool SupportsSkills { get; }

    /// <summary>
    /// How much real-world impact this agent can have. Surfaced to clients so a user can understand
    /// the risk of letting the agent run before they do.
    /// </summary>
    AgentRiskLevel RiskLevel { get; }

    /// <summary>
    /// Human-readable safety mechanisms that constrain this agent (e.g. command allowlist,
    /// workspace-confined paths, read-only tools). Surfaced to clients to make the guardrails
    /// that are already enforced in code visible to the user. Empty when the agent has none.
    /// </summary>
    IReadOnlyList<string> Guardrails { get; }

    /// <summary>The tools this agent is allowed to call.</summary>
    IList<AITool> Tools { get; }
}
