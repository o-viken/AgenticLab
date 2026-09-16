using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using TheSeries.AiService;
using TheSeries.AiService.Application;
using TheSeries.AiService.Application.Tools;
using TheSeries.AiService.Demo.Agents;
using TheSeries.AiService.Demo.Tools;
using TheSeries.AiService.Demo.Vendors;

var builder = WebApplication.CreateBuilder(args);

// The /chat/stream SSE response can stay idle for a long time while the client pauses or
// single-steps the flow, so disable Kestrel's minimum response data rate to avoid aborting it.
builder.WebHost.ConfigureKestrel(options => options.Limits.MinResponseDataRate = null);

// OpenTelemetry, health checks, service discovery and resilience.
builder.AddServiceDefaults();

// HttpClient used by the Wikipedia tool. Wikipedia requires a descriptive User-Agent.
builder.Services.AddHttpClient("wikipedia", client =>
{
    client.BaseAddress = new Uri("https://en.wikipedia.org");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("TheSeries-WikiAssistant/1.0 (https://github.com/Equinor/the-series)");
});

// Tools shared by the agents.
builder.Services.AddSingleton(sp =>
    new WikiTool(sp.GetRequiredService<IHttpClientFactory>().CreateClient("wikipedia")));
builder.Services.AddSingleton<CalculatorTool>();

// Fake Microsoft 365 / Graph tool set (canned, in-memory) used by the Microsoft 365 Copilot agents.
builder.Services.AddSingleton<Microsoft365Tool>();

// Workspace-scoped tools for the coding agent.
builder.Services.AddSingleton<FileSystemTool>();
builder.Services.AddSingleton<TerminalTool>();

// HttpClient used by the web-fetch tool. A descriptive User-Agent and a bounded timeout keep a slow or
// hostile endpoint from hanging a run.
builder.Services.AddHttpClient("webfetch", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("TheSeries-WebFetch/1.0 (https://github.com/Equinor/the-series)");
});

// Lets an agent fetch a public http(s) URL (the backend counterpart of a VS Code agent's web/fetch tool).
builder.Services.AddSingleton(sp =>
    new WebFetchTool(sp.GetRequiredService<IHttpClientFactory>().CreateClient("webfetch")));

// Lets an agent pause a streaming run to ask the user a clarifying question.
builder.Services.AddSingleton<AskQuestionTool>();

// Workspace skills: discovered per run from the active workspace, their names/descriptions injected
// into the agent's context and their full instructions loaded on demand via SkillsTool.
builder.Services.AddSingleton<SkillLoader>();
builder.Services.AddSingleton<SkillMatcher>();
builder.Services.AddSingleton<SkillsTool>();

// Workspace custom instructions (GitHub Copilot style): discovered per run from the active workspace's
// instructions/ folder, their full content always injected into the agent's context when present.
builder.Services.AddSingleton<InstructionLoader>();

// Workspace agents: user-authored agents discovered per run from the active workspace's agents/ folder
// and built into runnable agents on the shared chat client.
builder.Services.AddSingleton<WorkspaceAgentLoader>();
builder.Services.AddSingleton<WorkspaceAgentResolver>();

// Connects to the remote MCP server (Aspire resource 'mcpserver') and exposes its discovered tools.
builder.Services.AddSingleton<McpToolProvider>();

// Connects to the remote A2A agent server (Aspire resource 'a2aserver') and exposes a delegation tool the
// orchestrator agent uses to call that agent over the Agent2Agent protocol.
builder.Services.AddSingleton<A2AAgentProvider>();

// Maps a brand/vendor key (sent when a brand theme is selected in the web flow) to a vendor-flavoured
// harness system prompt that replaces the shared harness for a single run. The catalog is infrastructure
// (Application); the representative prompt content for each vendor lives in Demo/Vendors and is injected.
builder.Services.AddSingleton<IVendorHarness, DefaultHarness>();
builder.Services.AddSingleton<IVendorHarness, CopilotHarness>();
builder.Services.AddSingleton<IVendorHarness, ClaudeCodeHarness>();
builder.Services.AddSingleton<IVendorHarness, ClaudeHarness>();
builder.Services.AddSingleton<IVendorHarness, ChatGptHarness>();
builder.Services.AddSingleton<IVendorHarness, GeminiHarness>();
builder.Services.AddSingleton<IVendorHarness, Microsoft365Harness>();
builder.Services.AddSingleton<VendorHarnessCatalog>();

// Each agent declares its own persona and tool subset; the first registered is the default.
builder.Services.AddSingleton<IAgentDefinition, ChatAgent>();
builder.Services.AddSingleton<IAgentDefinition, WikiAssistantAgent>();
builder.Services.AddSingleton<IAgentDefinition, MathTutorAgent>();
// builder.Services.AddSingleton<IAgentDefinition, TriviaMasterAgent>();
builder.Services.AddSingleton<IAgentDefinition, AskAgent>();
builder.Services.AddSingleton<IAgentDefinition, PlanAgent>();
builder.Services.AddSingleton<IAgentDefinition, CoderAgent>();
builder.Services.AddSingleton<IAgentDefinition, Microsoft365Agent>();
builder.Services.AddSingleton<IAgentDefinition, M365ResearcherAgent>();
builder.Services.AddSingleton<IAgentDefinition, M365AnalystAgent>();
builder.Services.AddSingleton<IAgentDefinition, TimeKeeperAgent>();
builder.Services.AddSingleton<IAgentDefinition, OrchestratorAgent>();

// Builds (and caches) one chat client per Azure OpenAI deployment so agents can run on different models.
builder.Services.AddSingleton<ChatClientProvider>();
// The default chat client (default deployment), for components that are not tied to a specific agent.
builder.Services.AddSingleton(sp => sp.GetRequiredService<ChatClientProvider>().Get(null));
// The catalog of agents is stateless and safe to share as a singleton; each agent runs on its own deployment.
builder.Services.AddSingleton(sp =>
    new AgentCatalog(sp.GetRequiredService<ChatClientProvider>(), sp.GetServices<IAgentDefinition>()));

// Holds one conversation thread per conversation id so agent runs can continue an existing chat.
builder.Services.AddSingleton<ConversationStore>();

// Projects a real agent run into an observable stream of flow events for the visualization UI.
builder.Services.AddSingleton<FlowControlRegistry>();
builder.Services.AddSingleton<FlowTracer>();

// Orchestrates MCP + A2A discovery and projects it into a stream of discovery events for the visualization UI.
builder.Services.AddSingleton<DiscoveryTracer>();

var app = builder.Build();

// Discover the MCP server's tools and connect to the A2A server at startup so discovery-using agents pick
// them up, unless disabled via Discovery:OnStartup. Either way discovery can be (re)run on demand from the
// discovery page. Degrades gracefully when a server is unavailable.
if (app.Services.GetRequiredService<DiscoveryTracer>().DiscoverOnStartup)
{
    await app.Services.GetRequiredService<McpToolProvider>().ConnectAsync();
    await app.Services.GetRequiredService<A2AAgentProvider>().ConnectAsync();
}

app.MapDefaultEndpoints();

// Lists the available agents and which one is used by default.
app.MapGet("/agents", (AgentCatalog catalog) =>
    Results.Ok(new AgentsResponse(catalog.Agents, catalog.DefaultName)));

// Lists the skills discovered in a given workspace (names + descriptions) so a client can show the
// skill catalogue before a run starts. Returns an empty list when the path is missing/invalid or the
// workspace declares no skills.
app.MapPost("/skills", (SkillsRequest request, SkillLoader skills) =>
{
    using var workspace = OpenWorkspace(request.Workspace);
    if (workspace is null)
    {
        return Results.Ok(new SkillsResponse(Array.Empty<SkillInfo>()));
    }

    var discovered = skills.Load()
        .Select(s => new SkillInfo(s.Name, s.Description))
        .ToList();
    return Results.Ok(new SkillsResponse(discovered));
});

// Lists the custom instructions discovered in a given workspace's instructions/ folder (names +
// descriptions) so a client can show them before a run. Their full content is always injected into the
// agent's context when present. Returns an empty list when the path is missing/invalid or there are none.
app.MapPost("/instructions", (InstructionsRequest request, InstructionLoader instructions) =>
{
    using var workspace = OpenWorkspace(request.Workspace);
    if (workspace is null)
    {
        return Results.Ok(new InstructionsResponse(Array.Empty<InstructionInfo>()));
    }

    var discovered = instructions.Load()
        .Select(i => new InstructionInfo(i.Name, i.Description))
        .ToList();
    return Results.Ok(new InstructionsResponse(discovered));
});

// Lists the user-authored agents declared in a given workspace's agents/ folder so a client can offer
// them alongside the built-in agents. Returns an empty list when the path is missing/invalid or the
// workspace declares no agents.
app.MapPost("/agents/workspace", (WorkspaceAgentsRequest request, WorkspaceAgentResolver workspaceAgents) =>
{
    using var workspace = OpenWorkspace(request.Workspace);
    return workspace is null
        ? Results.Ok(new AgentsResponse(Array.Empty<AgentInfo>(), string.Empty))
        : Results.Ok(new AgentsResponse(workspaceAgents.ListAgents(), string.Empty));
});

// Lists the immediate sub-folders of one or more base folders so a client can suggest workspace paths
// (e.g. the repo folders under a "GitHub" directory the user pointed at). A read-only directory listing
// that skips hidden/system folders and caps the result; returns an empty list when no base exists.
app.MapPost("/workspaces", (WorkspaceBrowseRequest request) =>
    Results.Ok(new WorkspaceBrowseResponse(BrowseWorkspaces(request.Bases))));

// Lists the MCP servers connected to the AI service and the tools discovered from them, so a client can
// show MCP discovery before/after a run. Backed by the live MCP client connection established at startup.
app.MapGet("/mcp", (McpToolProvider mcp) =>
    Results.Ok(new McpResponse([new McpServerInfo(mcp.ServerName, mcp.ToolInfos
        .Select(t => new McpToolDescriptor(t.Name, t.Description)).ToList())])));

// Lists the agents reachable over the Agent2Agent (A2A) protocol, so a client can show which sub-agents
// the orchestrator can delegate to. Backed by the A2A client connection established at startup.
app.MapGet("/a2a", (A2AAgentProvider a2a) =>
    Results.Ok(new A2AResponse(a2a.AgentInfos
        .Select(a => new A2AAgentDescriptor(a.Name, a.Description)).ToList())));

// Returns a snapshot of every discovery source's status (endpoint, state, last-run time and discovered
// tools/agents) plus whether discovery runs at startup, so a client can render the last-known result
// without running discovery again.
app.MapGet("/discovery", (DiscoveryTracer discovery) =>
    Results.Ok(discovery.Snapshot()));

// Runs a discovery pass for the requested source (clean up, then re-discover MCP tools and/or A2A
// agents, then refresh the discovery-using agents) and streams each step as a Server-Sent Event so the
// process can be visualized live. The run is paced by a FlowSession (reusing the agent-flow stepping
// mechanism) so the client can step, pause, resume or stop it via POST /chat/control. Has side effects
// (tears down and rebuilds the discovery connections), hence POST.
app.MapPost("/discovery/stream", (DiscoveryStreamRequest request, DiscoveryTracer discovery, FlowControlRegistry registry, CancellationToken cancellationToken) =>
{
    var session = registry.Create(request.SessionId, request.Manual, request.StepDelayMs);
    return TypedResults.ServerSentEvents(discovery.StreamAsync(request.Source, session, cancellationToken));
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
// each offers) so a client can build the vendor picker without hard-coding the data. The non-brand
// Default vendor is not a registered vendor harness and is the client's own baseline.
app.MapGet("/vendors", (VendorHarnessCatalog vendors) =>
    Results.Ok(new VendorsResponse(vendors.Vendors
        .Select(v => new VendorInfo(
            v.Key,
            v.DisplayName,
            v.ModelLabel,
            v.Modes.Select(m => new VendorModeInfo(m.Agent, m.Label)).ToList()))
        .ToList())));

app.MapPost("/chat", async (ChatRequest request, AgentCatalog catalog, WorkspaceAgentResolver workspaceAgents, ConversationStore conversations, SkillLoader skills, InstructionLoader instructions, VendorHarnessCatalog vendors, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest("Message must not be empty.");
    }

    // When a brand/vendor is selected, its harness replaces the shared harness for this run.
    var harness = vendors.Resolve(request.Vendor);

    // A built-in agent: resolve it and open a workspace only when it requires one.
    if (catalog.TryResolve(request.Agent, harness, out var agent, out var resolvedName))
    {
        if (catalog.RequiresWorkspace(resolvedName) && string.IsNullOrWhiteSpace(request.Workspace))
        {
            return Results.BadRequest($"Agent '{resolvedName}' requires a workspace. Include a 'workspace' path in the request.");
        }

        using var workspace = catalog.RequiresWorkspace(resolvedName) ? OpenWorkspace(request.Workspace) : null;
        if (catalog.RequiresWorkspace(resolvedName) && workspace is null)
        {
            return Results.BadRequest($"Workspace path '{request.Workspace}' is not an existing directory.");
        }

        return await RunChat(agent, resolvedName, catalog.SupportsSkills(resolvedName), request, conversations, skills, instructions, cancellationToken);
    }

    // Otherwise it may be a user-authored agent declared in the workspace's agents/ folder, which can
    // only be discovered once a (valid) workspace is open.
    if (string.IsNullOrWhiteSpace(request.Workspace))
    {
        return Results.BadRequest($"Unknown agent '{request.Agent}'. Call GET /agents for the available names.");
    }

    using var agentWorkspace = OpenWorkspace(request.Workspace);
    if (agentWorkspace is null)
    {
        return Results.BadRequest($"Workspace path '{request.Workspace}' is not an existing directory.");
    }

    if (!workspaceAgents.TryResolve(request.Agent, out var workspaceAgent, out var definition, harness))
    {
        return Results.BadRequest($"Unknown agent '{request.Agent}'. Call GET /agents for the available names.");
    }

    // Workspace-defined agents always support workspace skills (see WorkspaceDefinedAgent), so the skill
    // catalogue is injected for their runs regardless of the agent file's `skills` field.
    return await RunChat(workspaceAgent, definition.Name, supportsSkills: true, request, conversations, skills, instructions, cancellationToken);
});

// Streams the steps of an agent run as Server-Sent Events so the web UI can animate the data flow live.
// The run is paced by a FlowSession so the client can step, pause, resume or stop the real execution.
app.MapPost("/chat/stream", IResult (FlowChatRequest request, FlowTracer tracer, FlowControlRegistry registry, CancellationToken cancellationToken) =>
{
    if (request.Breakpoints?.Any(kind => !FlowSession.BreakpointKinds.Contains(kind)) == true)
    {
        return Results.BadRequest("Unknown breakpoint kind.");
    }

    var session = registry.Create(request.SessionId, request.Manual, request.StepDelayMs);
    session.SetBreakpoints(request.Breakpoints ?? []);
    return TypedResults.ServerSentEvents(
        tracer.StreamAsync(request.Message, request.Agent, request.ConversationId, request.Workspace, request.DisabledTools, request.DisabledSkills, request.EnabledInstructions, request.Vendor, session, cancellationToken),
        eventType: "flow");
});

// Drives an in-flight /chat/stream run: single-step (next), pause, resume, switch mode, change the
// delay or stop. Matches the run by its session id.
app.MapPost("/chat/control", (FlowControlRequest request, FlowControlRegistry registry) =>
{
    if (!registry.TryGet(request.SessionId, out var session))
    {
        return Results.NotFound();
    }

    if (request.Breakpoints is { } breakpoints)
    {
        if (breakpoints.Any(kind => !FlowSession.BreakpointKinds.Contains(kind)))
        {
            return Results.BadRequest("Unknown breakpoint kind.");
        }
        session.SetBreakpoints(breakpoints);
    }

    if (request.BreakpointId is { } breakpointId)
    {
        var action = request.Action?.ToLowerInvariant();
        if (action is not ("next" or "resume"))
        {
            return Results.BadRequest("A breakpoint requires next or resume.");
        }
        return session.ReleaseBreakpoint(breakpointId, manual: action == "next")
            ? Results.NoContent()
            : Results.Conflict("This breakpoint is no longer paused.");
    }

    if (request.Manual is { } manual)
    {
        session.Manual = manual;
    }

    if (request.DelayMs is { } delay)
    {
        session.DelayMs = delay;
    }

    switch (request.Action?.ToLowerInvariant())
    {
        case "next":
            session.Advance();
            break;
        case "pause":
            session.Paused = true;
            break;
        case "resume":
            session.Paused = false;
            break;
        case "stop":
            session.Stop();
            break;
        case "answer":
            session.UserInput?.ProvideAnswer(request.Answer ?? string.Empty);
            break;
    }

    return Results.NoContent();
});

// Clears a conversation's remembered history so the next message starts fresh.
app.MapPost("/chat/reset", (ConversationResetRequest request, ConversationStore conversations) =>
{
    if (string.IsNullOrWhiteSpace(request.ConversationId))
    {
        return Results.BadRequest("ConversationId must not be empty.");
    }

    conversations.Reset(request.ConversationId.Trim());
    return Results.NoContent();
});

app.Run();

// Opens a workspace scope for a path, returning null when the path is missing or not a directory.
static WorkspaceScope? OpenWorkspace(string? path)
{
    try
    {
        return WorkspaceScope.Begin(path);
    }
    catch (Exception ex) when (ex is ArgumentException or DirectoryNotFoundException)
    {
        return null;
    }
}

// Lists the immediate sub-directories of each supplied base folder (the candidate repo folders), used to
// suggest workspace paths in the UI. Skips blank/non-existent bases and hidden/system sub-folders, dedups
// by full path, and caps the result so a huge base folder can't flood the client. Purely read-only.
static IReadOnlyList<WorkspaceEntry> BrowseWorkspaces(IReadOnlyList<string>? bases)
{
    if (bases is not { Count: > 0 })
    {
        return Array.Empty<WorkspaceEntry>();
    }

    const int maxEntries = 300;
    var entries = new List<WorkspaceEntry>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var raw in bases)
    {
        if (entries.Count >= maxEntries || string.IsNullOrWhiteSpace(raw))
        {
            continue;
        }

        string full;
        try
        {
            full = Path.GetFullPath(raw.Trim());
        }
        catch
        {
            continue;
        }

        if (!Directory.Exists(full))
        {
            continue;
        }

        IEnumerable<string> subdirs;
        try
        {
            subdirs = Directory.EnumerateDirectories(full).OrderBy(d => d, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // A base we can't read (permissions) simply contributes nothing.
            continue;
        }

        foreach (var dir in subdirs)
        {
            if (entries.Count >= maxEntries)
            {
                break;
            }

            try
            {
                var attrs = File.GetAttributes(dir);
                if (attrs.HasFlag(FileAttributes.Hidden) || attrs.HasFlag(FileAttributes.System))
                {
                    continue;
                }
            }
            catch
            {
                continue;
            }

            if (seen.Add(dir))
            {
                entries.Add(new WorkspaceEntry(dir, Path.GetFileName(dir), full));
            }
        }
    }

    return entries;
}

// Runs a (built-in or workspace-defined) agent for one /chat turn, continuing the supplied conversation,
// injecting the workspace's custom instructions (always, when present) and its skill catalogue (when the
// agent uses skills), and hiding any tools the caller disabled for this run. Any required workspace scope
// must already be active on the calling context.
static async Task<IResult> RunChat(AIAgent agent, string resolvedName, bool supportsSkills, ChatRequest request, ConversationStore conversations, SkillLoader skills, InstructionLoader instructions, CancellationToken cancellationToken)
{
    // Continue the existing conversation (remembering prior turns) when an id is supplied; otherwise mint
    // a new one and return it so the client can keep the conversation going.
    var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
        ? Guid.NewGuid().ToString("n")
        : request.ConversationId.Trim();
    var session = await conversations.GetOrCreateAsync(conversationId, agent, cancellationToken);

    // Custom instructions are opt-in (default off): only the ones the caller enabled are injected. Skills
    // default on: the caller can disable a subset. Both scopes stay active for the whole (single) run.
    using var instructionScope = InstructionFilterScope.Begin(request.EnabledInstructions ?? Array.Empty<string>());
    using var skillScope = request.DisabledSkills is { Count: > 0 } disabledSkills
        ? SkillFilterScope.Begin(disabledSkills)
        : null;

    // Surface the workspace's custom instructions (opted in) and its skills (names + descriptions, when
    // the agent uses them) to the agent for this run only.
    var runOptions = BuildRunOptions(supportsSkills, skills, instructions);

    // Hide any tools the caller disabled for this run so the model is only offered the remaining subset.
    using var toolScope = request.DisabledTools is { Count: > 0 } disabled
        ? ToolFilterScope.Begin(disabled)
        : null;

    var response = await agent.RunAsync(request.Message, session, runOptions, cancellationToken);
    return Results.Ok(new ChatResponse(response.Text, resolvedName, conversationId));
}

// Builds run options that inject the active workspace's custom instructions (always, when present) and
// its skill catalogue (only when the agent supports skills) into the agent's instructions for a single
// run. Returns null when neither is present, so the agent runs with just its base instructions. Must be
// called while the workspace scope is active.
static AgentRunOptions? BuildRunOptions(bool supportsSkills, SkillLoader skills, InstructionLoader instructions)
{
    var instructionBlock = instructions.BuildContextBlock();
    var skillBlock = supportsSkills ? skills.BuildContextBlock() : null;
    var combined = string.Join("\n", new[] { instructionBlock, skillBlock }.Where(b => !string.IsNullOrEmpty(b)));
    return string.IsNullOrEmpty(combined)
        ? null
        : new ChatClientAgentRunOptions(new ChatOptions { Instructions = combined });
}

internal sealed record ChatRequest(string Message, string? Agent = null, string? ConversationId = null, string? Workspace = null, IReadOnlyList<string>? DisabledTools = null, IReadOnlyList<string>? DisabledSkills = null, IReadOnlyList<string>? EnabledInstructions = null, string? Vendor = null);
internal sealed record ChatResponse(string Reply, string Agent, string ConversationId);
internal sealed record AgentsResponse(IReadOnlyList<AgentInfo> Agents, string Default);
internal sealed record SkillsRequest(string? Workspace);
internal sealed record SkillsResponse(IReadOnlyList<SkillInfo> Skills);
internal sealed record SkillInfo(string Name, string Description);
internal sealed record InstructionsRequest(string? Workspace);
internal sealed record InstructionsResponse(IReadOnlyList<InstructionInfo> Instructions);
internal sealed record InstructionInfo(string Name, string Description);
internal sealed record WorkspaceAgentsRequest(string? Workspace);
internal sealed record WorkspaceBrowseRequest(IReadOnlyList<string>? Bases);
internal sealed record WorkspaceBrowseResponse(IReadOnlyList<WorkspaceEntry> Directories);
internal sealed record WorkspaceEntry(string Path, string Name, string Base);
internal sealed record McpResponse(IReadOnlyList<McpServerInfo> Servers);
internal sealed record McpServerInfo(string Name, IReadOnlyList<McpToolDescriptor> Tools);
internal sealed record McpToolDescriptor(string Name, string Description);
internal sealed record A2AResponse(IReadOnlyList<A2AAgentDescriptor> Agents);
internal sealed record A2AAgentDescriptor(string Name, string Description);
internal sealed record HarnessRequest(string? Agent, string? Vendor);
internal sealed record HarnessResponse(string Prompt);
internal sealed record VendorsResponse(IReadOnlyList<VendorInfo> Vendors);
internal sealed record VendorInfo(string Key, string DisplayName, string ModelLabel, IReadOnlyList<VendorModeInfo> Modes);
internal sealed record VendorModeInfo(string Agent, string Label);
internal sealed record FlowChatRequest(string Message, string? Agent, string SessionId, string ConversationId, bool Manual = false, int StepDelayMs = 0, string? Workspace = null, IReadOnlyList<string>? DisabledTools = null, IReadOnlyList<string>? DisabledSkills = null, IReadOnlyList<string>? EnabledInstructions = null, string? Vendor = null, IReadOnlyList<string>? Breakpoints = null);
internal sealed record FlowControlRequest(string SessionId, string? Action = null, bool? Manual = null, int? DelayMs = null, string? Answer = null, IReadOnlyList<string>? Breakpoints = null, string? BreakpointId = null);
internal sealed record ConversationResetRequest(string ConversationId);
internal sealed record DiscoveryStreamRequest(string SessionId, string? Source = null, bool Manual = false, int StepDelayMs = 0);
