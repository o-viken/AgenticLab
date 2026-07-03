using System.ComponentModel;
using System.Net.Http.Json;
using A2A;
using Microsoft.Extensions.AI;

namespace TheSeries.AiService.Application;

/// <summary>
/// Connects to a remote server that hosts one or more agents over the Agent2Agent (A2A) protocol and
/// exposes a single delegation tool the orchestrator agent can call. Where <see cref="McpToolProvider"/>
/// discovers external <em>tools</em> over MCP, this provider reaches external <em>agents</em> over A2A: it
/// discovers the server's agents (their names, paths and descriptions), builds an <see cref="A2AClient"/>
/// per agent, and exposes one <c>DelegateToAgent(agentName, question)</c> tool that routes a question to
/// the named agent and returns its answer. Because the roster is discovered at startup, adding an agent to
/// the A2A server surfaces it here with no code change. When the server is unavailable the provider
/// degrades gracefully to an empty tool list instead of failing the service.
/// </summary>
public sealed class A2AAgentProvider(IConfiguration configuration, ILogger<A2AAgentProvider> logger) : IDisposable
{
    private readonly HttpClient _http = new();
    private readonly Dictionary<string, A2AClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private IList<AITool> _tools = [];
    private IReadOnlyList<A2AAgentInfo> _agentInfos = [];

    /// <summary>Whether at least one remote A2A agent was discovered.</summary>
    public bool IsConnected => _clients.Count > 0;

    /// <summary>The A2A delegation tools, ready to be handed to an agent. Empty until <see cref="ConnectAsync"/> runs.</summary>
    public IList<AITool> GetTools() => _tools;

    /// <summary>The discovered A2A agents' names and descriptions, surfaced so clients can show what was discovered.</summary>
    public IReadOnlyList<A2AAgentInfo> AgentInfos => _agentInfos;

    /// <summary>
    /// Resolves the remote A2A server's endpoint (via service discovery, falling back to the <c>A2A:Endpoint</c>
    /// configuration value for a standalone run), discovers the agents it hosts and builds an
    /// <see cref="A2AClient"/> for each. Safe to call once at startup; failures are logged and leave the tool
    /// list empty so the agents still load.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        var baseEndpoint = configuration["services:a2aserver:http:0"]
            ?? configuration["services:a2aserver:https:0"]
            ?? configuration["A2A:Endpoint"];
        if (string.IsNullOrWhiteSpace(baseEndpoint))
        {
            logger.LogWarning("No A2A server endpoint configured (services:a2aserver:* or A2A:Endpoint); skipping A2A agent discovery.");
            return;
        }

        try
        {
            var baseUri = new Uri(baseEndpoint);

            // Discover the agents this server hosts so the roster is not hard-coded here.
            var discovery = await _http.GetFromJsonAsync<A2ADiscoveryResponse>(
                new Uri(baseUri, "/agents"), cancellationToken);
            var agents = discovery?.Agents ?? [];

            foreach (var agent in agents)
            {
                if (string.IsNullOrWhiteSpace(agent.Name) || string.IsNullOrWhiteSpace(agent.Path))
                {
                    continue;
                }

                _clients[agent.Name] = new A2AClient(new Uri(baseUri, agent.Path), _http);
            }

            if (_clients.Count == 0)
            {
                logger.LogWarning("A2A server at {Endpoint} reported no agents; the orchestrator will run without A2A delegation.", baseUri);
                return;
            }

            _agentInfos = agents
                .Where(a => _clients.ContainsKey(a.Name))
                .Select(a => new A2AAgentInfo(a.Name, a.Description))
                .ToList();

            // One generic delegation tool routes to any discovered agent by name. Its description lists the
            // available agents so the model knows who it can delegate to.
            var roster = string.Join("; ", _agentInfos.Select(a => $"{a.Name} — {a.Description}"));
            _tools =
            [
                AIFunctionFactory.Create(DelegateToAgentAsync, new AIFunctionFactoryOptions
                {
                    Name = "DelegateToAgent",
                    Description = "Delegate a question to a named specialist agent over the A2A protocol and " +
                        $"return its answer. Available agents: {roster}.",
                }),
            ];

            logger.LogInformation("Discovered {Count} A2A agent(s) at {Endpoint}: {Agents}.",
                _clients.Count, baseUri, string.Join(", ", _clients.Keys));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not connect to the A2A server at {Endpoint}; the orchestrator will run without A2A delegation.", baseEndpoint);
        }
    }

    /// <summary>
    /// Forwards a question to a discovered agent (by name) over A2A and returns its answer. Exposed to the
    /// orchestrator agent as the <c>DelegateToAgent</c> tool.
    /// </summary>
    /// <param name="agentName">The name of the specialist agent to delegate to (see the tool description for the roster).</param>
    /// <param name="question">The question or request to send to the agent.</param>
    /// <param name="cancellationToken">Cancels the delegation call.</param>
    /// <returns>The agent's answer, or a message describing why it could not be reached.</returns>
    [Description("Delegate a question to a named specialist agent over the A2A protocol and return its answer.")]
    private async Task<string> DelegateToAgentAsync(
        [Description("The name of the specialist agent to delegate to.")] string agentName,
        [Description("The question or request to send to the agent.")] string question,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agentName) || !_clients.TryGetValue(agentName.Trim(), out var client))
        {
            var available = _clients.Count == 0 ? "none" : string.Join(", ", _clients.Keys);
            return $"Unknown agent '{agentName}'. Available agents: {available}.";
        }

        if (string.IsNullOrWhiteSpace(question))
        {
            return "No question was provided to delegate.";
        }

        try
        {
            var response = await client.SendMessageAsync(question, Role.User, cancellationToken: cancellationToken);
            if (response.PayloadCase == SendMessageResponseCase.Message && response.Message is { } message)
            {
                var text = string.Concat(message.Parts
                    .Where(p => p.ContentCase == PartContentCase.Text)
                    .Select(p => p.Text));
                return string.IsNullOrWhiteSpace(text) ? "The agent returned an empty reply." : text;
            }

            return "The agent did not return a direct answer.";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "A2A delegation call to agent '{Agent}' failed.", agentName);
            return $"Could not reach agent '{agentName}': {ex.Message}";
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();
}

/// <summary>A reachable A2A agent's name and description.</summary>
public sealed record A2AAgentInfo(string Name, string Description);

/// <summary>The A2A server's discovery response listing the agents it hosts.</summary>
internal sealed record A2ADiscoveryResponse(IReadOnlyList<A2ADiscoveredAgent> Agents);

/// <summary>A discovered A2A agent's name, path and description.</summary>
internal sealed record A2ADiscoveredAgent(string Name, string Path, string Description);
