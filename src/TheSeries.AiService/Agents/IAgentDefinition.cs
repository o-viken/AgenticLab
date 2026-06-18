using Microsoft.Extensions.AI;

namespace TheSeries.AiService.Agents;

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

    /// <summary>The tools this agent is allowed to call.</summary>
    IList<AITool> Tools { get; }
}
