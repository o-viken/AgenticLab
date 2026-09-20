using AgenticLab.Extensibility.Examples;

namespace AgenticLab.AiService.Endpoints;

/// <summary>Read-only endpoints describing the agents, vendors and the effective harness prompt.</summary>
internal static class AgentEndpoints
{
    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        // Lists the available agents and which one is used by default.
        app.MapGet("/agents", (AgentCatalog catalog, ExampleCatalog examples) =>
            Results.Ok(new AgentsResponse(catalog.Agents.Select(agent => agent with
            {
                ExampleId = examples.ForAgent(agent.Name)?.Id,
                RequiresExampleUi = examples.ForAgent(agent.Name)?.RequiresUi ?? false,
            }).ToArray(), catalog.DefaultName)));

        // Lists the user-authored agents declared in a given workspace's agents/ folder so a client can offer
        // them alongside the built-in agents. Returns an empty list when the path is missing/invalid or the
        // workspace declares no agents.
        app.MapPost("/agents/workspace", (WorkspaceAgentsRequest request, WorkspaceAgentResolver workspaceAgents) =>
        {
            using var workspace = WorkspaceScope.TryBegin(request.Workspace);
            return workspace is null
                ? Results.Ok(new AgentsResponse(Array.Empty<AgentInfo>(), string.Empty))
                : Results.Ok(new AgentsResponse(workspaceAgents.ListAgents(), string.Empty));
        });

        // Returns the effective harness (system) prompt for a given agent + vendor so a client can show the
        // active system prompt before a run. A selected vendor's harness replaces the agent's own; otherwise the
        // agent's harness is returned (falling back to the default agent's when the name is unknown).
        app.MapPost("/harness", (HarnessRequest request, AgentCatalog catalog, VendorHarnessCatalog vendors) =>
        {
            var prompt = vendors.Resolve(request.Vendor)
                ?? catalog.HarnessFor(request.Agent)
                ?? catalog.HarnessFor(null)
                ?? string.Empty;
            return Results.Ok(new HarnessResponse(prompt));
        });

        // Lists the brand vendors with their full metadata (display name, simulated model label and the modes
        // each offers) so a client can build the vendor picker without hard-coding the data.
        app.MapGet("/vendors", (VendorHarnessCatalog vendors, ExampleCatalog examples) =>
            Results.Ok(new VendorsResponse(vendors.Vendors
                .Select(v => new VendorInfo(
                    v.Key,
                    v.DisplayName,
                    v.ModelLabel,
                    v.Modes.Select(m => new VendorModeInfo(m.Agent, m.Label)).ToList(),
                    examples.Modules.FirstOrDefault(module => module.Manifest.HostKeys.Contains(v.Key))?.Manifest.Id,
                    examples.Modules.FirstOrDefault(module => module.Manifest.HostKeys.Contains(v.Key))?.Manifest.RequiresUi ?? false))
                .ToList())));

        return app;
    }
}

internal sealed record AgentsResponse(IReadOnlyList<AgentInfo> Agents, string Default);
internal sealed record WorkspaceAgentsRequest(string? Workspace);
internal sealed record HarnessRequest(string? Agent, string? Vendor);
internal sealed record HarnessResponse(string Prompt);
internal sealed record VendorsResponse(IReadOnlyList<VendorInfo> Vendors);
internal sealed record VendorInfo(string Key, string DisplayName, string ModelLabel, IReadOnlyList<VendorModeInfo> Modes, string? ExampleId = null, bool RequiresExampleUi = false);
internal sealed record VendorModeInfo(string Agent, string Label);
