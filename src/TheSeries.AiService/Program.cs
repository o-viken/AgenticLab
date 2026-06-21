using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using TheSeries.AiService;
using TheSeries.AiService.Application;
using TheSeries.AiService.Application.Tools;
using TheSeries.AiService.Demo.Agents;
using TheSeries.AiService.Demo.Tools;

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

// Lets an agent pause a streaming run to ask the user a clarifying question.
builder.Services.AddSingleton<AskQuestionTool>();

// Workspace skills: discovered per run from the active workspace, their names/descriptions injected
// into the agent's context and their full instructions loaded on demand via SkillsTool.
builder.Services.AddSingleton<SkillLoader>();
builder.Services.AddSingleton<SkillMatcher>();
builder.Services.AddSingleton<SkillsTool>();

// Workspace agents: user-authored agents discovered per run from the active workspace's agents/ folder
// and built into runnable agents on the shared chat client.
builder.Services.AddSingleton<WorkspaceAgentLoader>();
builder.Services.AddSingleton<WorkspaceAgentResolver>();

// Each agent declares its own persona and tool subset; the first registered is the default.
builder.Services.AddSingleton<IAgentDefinition, ChatBotAgent>();
builder.Services.AddSingleton<IAgentDefinition, WikiAssistantAgent>();
builder.Services.AddSingleton<IAgentDefinition, MathTutorAgent>();
// builder.Services.AddSingleton<IAgentDefinition, TriviaMasterAgent>();
builder.Services.AddSingleton<IAgentDefinition, AskAgent>();
builder.Services.AddSingleton<IAgentDefinition, PlanAgent>();
builder.Services.AddSingleton<IAgentDefinition, CoderAgent>();
builder.Services.AddSingleton<IAgentDefinition, Microsoft365Agent>();
builder.Services.AddSingleton<IAgentDefinition, M365ResearcherAgent>();
builder.Services.AddSingleton<IAgentDefinition, M365AnalystAgent>();

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

var app = builder.Build();

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

app.MapPost("/chat", async (ChatRequest request, AgentCatalog catalog, WorkspaceAgentResolver workspaceAgents, ConversationStore conversations, SkillLoader skills, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest("Message must not be empty.");
    }

    // A built-in agent: resolve it and open a workspace only when it requires one.
    if (catalog.TryResolve(request.Agent, out var agent, out var resolvedName))
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

        return await RunChat(agent, resolvedName, catalog.SupportsSkills(resolvedName), request, conversations, skills, cancellationToken);
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

    if (!workspaceAgents.TryResolve(request.Agent, out var workspaceAgent, out var definition))
    {
        return Results.BadRequest($"Unknown agent '{request.Agent}'. Call GET /agents for the available names.");
    }

    return await RunChat(workspaceAgent, definition.Name, definition.SupportsSkills, request, conversations, skills, cancellationToken);
});

// Streams the steps of an agent run as Server-Sent Events so the web UI can animate the data flow live.
// The run is paced by a FlowSession so the client can step, pause, resume or stop the real execution.
app.MapPost("/chat/stream", (FlowChatRequest request, FlowTracer tracer, FlowControlRegistry registry, CancellationToken cancellationToken) =>
{
    var session = registry.Create(request.SessionId, request.Manual, request.StepDelayMs);
    return TypedResults.ServerSentEvents(
        tracer.StreamAsync(request.Message, request.Agent, request.ConversationId, request.Workspace, request.DisabledTools, session, cancellationToken),
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

// Runs a (built-in or workspace-defined) agent for one /chat turn, continuing the supplied conversation,
// injecting the workspace skill catalogue when the agent uses skills, and hiding any tools the caller
// disabled for this run. Any required workspace scope must already be active on the calling context.
static async Task<IResult> RunChat(AIAgent agent, string resolvedName, bool supportsSkills, ChatRequest request, ConversationStore conversations, SkillLoader skills, CancellationToken cancellationToken)
{
    // Continue the existing conversation (remembering prior turns) when an id is supplied; otherwise mint
    // a new one and return it so the client can keep the conversation going.
    var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
        ? Guid.NewGuid().ToString("n")
        : request.ConversationId.Trim();
    var session = await conversations.GetOrCreateAsync(conversationId, agent, cancellationToken);

    // Surface the workspace's skills to the agent for this run (names + descriptions only).
    var runOptions = supportsSkills ? BuildSkillRunOptions(skills) : null;

    // Hide any tools the caller disabled for this run so the model is only offered the remaining subset.
    using var toolScope = request.DisabledTools is { Count: > 0 } disabled
        ? ToolFilterScope.Begin(disabled)
        : null;

    var response = await agent.RunAsync(request.Message, session, runOptions, cancellationToken);
    return Results.Ok(new ChatResponse(response.Text, resolvedName, conversationId));
}

// Builds run options that inject the active workspace's skill catalogue into the agent's instructions
// for a single run. Returns null when the workspace declares no skills, so the agent runs with just its
// base instructions. Must be called while the workspace scope is active.
static AgentRunOptions? BuildSkillRunOptions(SkillLoader skills)
{
    var block = skills.BuildContextBlock();
    return string.IsNullOrEmpty(block)
        ? null
        : new ChatClientAgentRunOptions(new ChatOptions { Instructions = block });
}

internal sealed record ChatRequest(string Message, string? Agent = null, string? ConversationId = null, string? Workspace = null, IReadOnlyList<string>? DisabledTools = null);
internal sealed record ChatResponse(string Reply, string Agent, string ConversationId);
internal sealed record AgentsResponse(IReadOnlyList<AgentInfo> Agents, string Default);
internal sealed record SkillsRequest(string? Workspace);
internal sealed record SkillsResponse(IReadOnlyList<SkillInfo> Skills);
internal sealed record SkillInfo(string Name, string Description);
internal sealed record WorkspaceAgentsRequest(string? Workspace);
internal sealed record FlowChatRequest(string Message, string? Agent, string SessionId, string ConversationId, bool Manual = false, int StepDelayMs = 0, string? Workspace = null, IReadOnlyList<string>? DisabledTools = null);
internal sealed record FlowControlRequest(string SessionId, string? Action = null, bool? Manual = null, int? DelayMs = null, string? Answer = null);
internal sealed record ConversationResetRequest(string ConversationId);
