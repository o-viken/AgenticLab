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

    /// <summary>The tools this agent is allowed to call.</summary>
    IList<AITool> Tools { get; }
}
