using System.Runtime.CompilerServices;

namespace TheSeries.AiService.Application.Discovery;

/// <summary>
/// Orchestrates a discovery run across the requested discovery source(s) — MCP, A2A, or both — and
/// projects it into an ordered stream of <see cref="DiscoveryEvent"/>s for the visualization UI. A run
/// first tears down and re-runs each source's discovery (<see cref="McpToolProvider.RediscoverAsync"/> /
/// <see cref="A2AAgentProvider.RediscoverAsync"/>), then refreshes the built agents so the freshly
/// discovered tools take effect (<see cref="AgentCatalog.RefreshDiscoveryAgents"/>). Each step is gated on
/// a <see cref="FlowSession"/> so the client can pace, pause, single-step or stop the run — reusing the
/// same stepping mechanism as the agent flow. Concurrent runs are serialized so a re-discovery cannot race
/// another.
/// </summary>
/// <param name="mcp">The MCP tool discovery source.</param>
/// <param name="a2a">The A2A agent discovery source.</param>
/// <param name="catalog">The agent catalog whose discovery-backed agents are refreshed after a run.</param>
/// <param name="registry">The registry the run's <see cref="FlowSession"/> is removed from when it ends.</param>
/// <param name="configuration">Configuration, read for the <c>Discovery:OnStartup</c> flag in the snapshot.</param>
public sealed class DiscoveryTracer(
    McpToolProvider mcp,
    A2AAgentProvider a2a,
    AgentCatalog catalog,
    FlowControlRegistry registry,
    IConfiguration configuration)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Whether discovery runs automatically at startup (config <c>Discovery:OnStartup</c>, default true).</summary>
    public bool DiscoverOnStartup => configuration.GetValue("Discovery:OnStartup", true);

    /// <summary>
    /// Runs discovery for the requested <paramref name="source"/> (<c>"mcp"</c>, <c>"a2a"</c>, or
    /// <c>"all"</c>/null for both) and yields each step as it happens, pacing each on
    /// <paramref name="session"/> and finishing with a <see cref="DiscoveryEventKind.Complete"/> event once
    /// the agents have been refreshed. Runs are serialized: if one is already in progress, this waits for it
    /// to finish before starting.
    /// </summary>
    /// <param name="source">The source to discover: <c>"mcp"</c>, <c>"a2a"</c>, or <c>"all"</c>/null for both.</param>
    /// <param name="session">The control session that paces, pauses and stops the run.</param>
    /// <param name="cancellationToken">Cancels the discovery run.</param>
    public async IAsyncEnumerable<DiscoveryEvent> StreamAsync(
        string? source,
        FlowSession session,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.StopToken);
        var token = linked.Token;

        var runMcp = string.IsNullOrWhiteSpace(source) || source is "all" or "mcp";
        var runA2a = string.IsNullOrWhiteSpace(source) || source is "all" or "a2a";

        await _gate.WaitAsync(token);
        try
        {
            var seq = 0;

            if (runMcp)
            {
                await foreach (var evt in mcp.RediscoverAsync(token))
                {
                    await session.WaitForStepAsync(token);
                    yield return evt with { Sequence = seq++ };
                }
            }

            if (runA2a)
            {
                await foreach (var evt in a2a.RediscoverAsync(token))
                {
                    await session.WaitForStepAsync(token);
                    yield return evt with { Sequence = seq++ };
                }
            }

            // Rebuild the agents that use the discovered tools so the rediscovery takes effect on them.
            catalog.RefreshDiscoveryAgents();

            await session.WaitForStepAsync(token);
            yield return new DiscoveryEvent("all", DiscoveryEventKind.Complete, "Discovery complete; agents refreshed.", Sequence: seq);
        }
        finally
        {
            _gate.Release();
            registry.Remove(session.Id);
        }
    }

    /// <summary>
    /// Returns the current status of every discovery source without running discovery, so a client can
    /// render the last-known result. Also reports whether discovery runs at startup.
    /// </summary>
    public DiscoverySnapshot Snapshot() =>
        new(DiscoverOnStartup, [mcp.Status, a2a.Status]);
}
