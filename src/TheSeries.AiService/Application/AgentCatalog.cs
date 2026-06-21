using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace TheSeries.AiService.Application;

/// <summary>
/// A user-facing summary of a selectable agent.
/// </summary>
/// <param name="Name">The unique name used to select the agent.</param>
/// <param name="Description">A short description of what the agent is good at.</param>
/// <param name="Tools">The names of the tools this agent may call.</param>
/// <param name="RequiresWorkspace">Whether selecting this agent requires the caller to supply a workspace path.</param>
/// <param name="SupportsSkills">Whether this agent uses workspace skills (its names/descriptions are injected each run).</param>
/// <param name="RiskLevel">How much real-world impact the agent can have (<c>None</c>, <c>Low</c>, <c>Medium</c>, <c>High</c>).</param>
/// <param name="Guardrails">The safety mechanisms enforced for this agent, surfaced so the user understands the risk.</param>
/// <param name="ModelId">The Azure OpenAI deployment the agent runs on, surfaced so clients can show which model answers.</param>
public sealed record AgentInfo(string Name, string Description, IReadOnlyList<string> Tools, bool RequiresWorkspace, bool SupportsSkills, string RiskLevel, IReadOnlyList<string> Guardrails, string ModelId);

/// <summary>
/// Builds and resolves the set of selectable agents from their <see cref="IAgentDefinition"/>s, all sharing
/// the same Azure OpenAI chat client. Agents are stateless and built once at construction.
/// </summary>
public sealed class AgentCatalog
{
    private readonly Dictionary<string, AIAgent> _agents;
    private readonly Dictionary<string, bool> _requiresWorkspace;
    private readonly Dictionary<string, bool> _supportsSkills;

    /// <summary>
    /// Composes one <see cref="ChatClientAgent"/> per definition, keyed by name (case-insensitive).
    /// The first definition is treated as the default. Each agent's <see cref="AgentInfo.ModelId"/> is its
    /// declared deployment (see <see cref="ChatClientProvider.ResolveDeployment"/>) for display, but it
    /// runs on the chat client for its <see cref="ChatClientProvider.ExecutionDeployment"/> — the same
    /// deployment when per-agent routing is on, or the default when <c>AzureOpenAI:ForceDefaultModel</c>
    /// makes the declared model display-only.
    /// </summary>
    /// <param name="clients">Provides the Azure OpenAI chat client for each agent's deployment.</param>
    /// <param name="definitions">The agent definitions to expose.</param>
    /// <exception cref="ArgumentException">Thrown when no definitions are supplied.</exception>
    public AgentCatalog(ChatClientProvider clients, IEnumerable<IAgentDefinition> definitions)
    {
        var list = definitions.ToList();
        if (list.Count == 0)
        {
            throw new ArgumentException("At least one agent definition is required.", nameof(definitions));
        }

        var deployments = list.ToDictionary(d => d.Name, clients.ResolveDeployment, StringComparer.OrdinalIgnoreCase);
        _agents = new Dictionary<string, AIAgent>(StringComparer.OrdinalIgnoreCase);
        _requiresWorkspace = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        _supportsSkills = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in list)
        {
            _agents[definition.Name] = new ChatClientAgent(
                clients.Get(clients.ExecutionDeployment(deployments[definition.Name])),
                instructions: definition.Instructions,
                name: definition.Name,
                tools: definition.Tools);
            _requiresWorkspace[definition.Name] = definition.RequiresWorkspace;
            _supportsSkills[definition.Name] = definition.SupportsSkills;
        }

        DefaultName = list[0].Name;
        Agents = list.Select(d => new AgentInfo(
            d.Name,
            d.Description,
            d.Tools.OfType<AIFunction>().Select(f => f.Name).ToList(),
            d.RequiresWorkspace,
            d.SupportsSkills,
            d.RiskLevel.ToString(),
            d.Guardrails,
            deployments[d.Name])).ToList();
    }

    /// <summary>The name of the agent used when a request does not specify one.</summary>
    public string DefaultName { get; }

    /// <summary>The available agents, in registration order.</summary>
    public IReadOnlyList<AgentInfo> Agents { get; }

    /// <summary>Resolves the agent by name (case-insensitive), or the default when <paramref name="name"/> is null/blank.</summary>
    /// <param name="name">The requested agent name, or null/blank for the default.</param>
    /// <param name="agent">The resolved agent when found.</param>
    /// <param name="resolvedName">The name of the resolved agent when found.</param>
    /// <returns><c>true</c> when an agent was resolved; otherwise <c>false</c>.</returns>
    public bool TryResolve(string? name, out AIAgent agent, out string resolvedName)
    {
        resolvedName = string.IsNullOrWhiteSpace(name) ? DefaultName : name.Trim();
        return _agents.TryGetValue(resolvedName, out agent!);
    }

    /// <summary>Whether the named agent requires a workspace path. Unknown names return <c>false</c>.</summary>
    /// <param name="name">The resolved agent name.</param>
    /// <returns><c>true</c> when the agent requires a workspace; otherwise <c>false</c>.</returns>
    public bool RequiresWorkspace(string name) =>
        _requiresWorkspace.TryGetValue(name, out var requires) && requires;

    /// <summary>Whether the named agent uses workspace skills. Unknown names return <c>false</c>.</summary>
    /// <param name="name">The resolved agent name.</param>
    /// <returns><c>true</c> when the agent participates in workspace skills; otherwise <c>false</c>.</returns>
    public bool SupportsSkills(string name) =>
        _supportsSkills.TryGetValue(name, out var supports) && supports;
}
