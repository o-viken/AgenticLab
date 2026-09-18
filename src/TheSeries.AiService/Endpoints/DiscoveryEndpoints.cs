namespace TheSeries.AiService.Endpoints;

/// <summary>Endpoints exposing MCP tool discovery and A2A agent discovery, plus the re-runnable discovery stream.</summary>
internal static class DiscoveryEndpoints
{
    public static IEndpointRouteBuilder MapDiscoveryEndpoints(this IEndpointRouteBuilder app)
    {
        // Lists the MCP servers connected to the AI service and the tools discovered from them.
        app.MapGet("/mcp", (McpToolProvider mcp) =>
            Results.Ok(new McpResponse([new McpServerInfo(mcp.ServerName, mcp.ToolInfos
                .Select(t => new McpToolDescriptor(t.Name, t.Description)).ToList())])));

        // Lists the agents reachable over the Agent2Agent (A2A) protocol that the orchestrator can delegate to.
        app.MapGet("/a2a", (A2AAgentProvider a2a) =>
            Results.Ok(new A2AResponse(a2a.AgentInfos
                .Select(a => new A2AAgentDescriptor(a.Name, a.Description)).ToList())));

        // Returns a snapshot of every discovery source's status plus whether discovery runs at startup, so a
        // client can render the last-known result without running discovery again.
        app.MapGet("/discovery", (DiscoveryTracer discovery) =>
            Results.Ok(discovery.Snapshot()));

        // Runs a discovery pass for the requested source (clean up, re-discover, refresh the discovery-using
        // agents) and streams each step as a Server-Sent Event. Paced by a FlowSession so the client can step,
        // pause, resume or stop it via POST /chat/control. Has side effects, hence POST.
        app.MapPost("/discovery/stream", (DiscoveryStreamRequest request, DiscoveryTracer discovery, FlowControlRegistry registry, CancellationToken cancellationToken) =>
        {
            var session = registry.Create(request.SessionId, request.Manual, request.StepDelayMs);
            return TypedResults.ServerSentEvents(discovery.StreamAsync(request.Source, session, cancellationToken));
        });

        return app;
    }
}

internal sealed record McpResponse(IReadOnlyList<McpServerInfo> Servers);
internal sealed record McpServerInfo(string Name, IReadOnlyList<McpToolDescriptor> Tools);
internal sealed record McpToolDescriptor(string Name, string Description);
internal sealed record A2AResponse(IReadOnlyList<A2AAgentDescriptor> Agents);
internal sealed record A2AAgentDescriptor(string Name, string Description);
internal sealed record DiscoveryStreamRequest(string SessionId, string? Source = null, bool Manual = false, int StepDelayMs = 0);
