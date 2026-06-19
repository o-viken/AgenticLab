using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using TheSeries.AiService;
using TheSeries.AiService.Agents;
using TheSeries.AiService.Tools;

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

// Workspace-scoped tools for the coding agent.
builder.Services.AddSingleton<FileSystemTool>();
builder.Services.AddSingleton<TerminalTool>();

// Workspace skills: discovered per run from the active workspace, their names/descriptions injected
// into the agent's context and their full instructions loaded on demand via SkillsTool.
builder.Services.AddSingleton<SkillLoader>();
builder.Services.AddSingleton<SkillMatcher>();
builder.Services.AddSingleton<SkillsTool>();

// Each agent declares its own persona and tool subset; the first registered is the default.
builder.Services.AddSingleton<IAgentDefinition, ChatBotAgent>();
builder.Services.AddSingleton<IAgentDefinition, WikiAssistantAgent>();
builder.Services.AddSingleton<IAgentDefinition, MathTutorAgent>();
// builder.Services.AddSingleton<IAgentDefinition, TriviaMasterAgent>();
builder.Services.AddSingleton<IAgentDefinition, CoderAgent>();

// The shared chat client and the catalog of agents are stateless and safe to share as singletons.
builder.Services.AddSingleton(sp =>
    AgentService.CreateChatClient(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton(sp =>
    new AgentCatalog(sp.GetRequiredService<IChatClient>(), sp.GetServices<IAgentDefinition>()));

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

app.MapPost("/chat", async (ChatRequest request, AgentCatalog catalog, ConversationStore conversations, SkillLoader skills, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest("Message must not be empty.");
    }

    if (!catalog.TryResolve(request.Agent, out var agent, out var resolvedName))
    {
        return Results.BadRequest($"Unknown agent '{request.Agent}'. Call GET /agents for the available names.");
    }

    if (catalog.RequiresWorkspace(resolvedName) && string.IsNullOrWhiteSpace(request.Workspace))
    {
        return Results.BadRequest($"Agent '{resolvedName}' requires a workspace. Include a 'workspace' path in the request.");
    }

    using var workspace = catalog.RequiresWorkspace(resolvedName) ? OpenWorkspace(request.Workspace) : null;
    if (catalog.RequiresWorkspace(resolvedName) && workspace is null)
    {
        return Results.BadRequest($"Workspace path '{request.Workspace}' is not an existing directory.");
    }

    // Continue the existing conversation (remembering prior turns) when an id is supplied; otherwise mint
    // a new one and return it so the client can keep the conversation going.
    var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
        ? Guid.NewGuid().ToString("n")
        : request.ConversationId.Trim();
    var session = await conversations.GetOrCreateAsync(conversationId, agent, cancellationToken);

    // Surface the workspace's skills to the agent for this run (names + descriptions only).
    var runOptions = BuildSkillRunOptions(catalog, resolvedName, skills);

    // Hide any tools the caller disabled for this run so the model is only offered the remaining subset.
    using var toolScope = request.DisabledTools is { Count: > 0 } disabled
        ? ToolFilterScope.Begin(disabled)
        : null;

    var response = await agent.RunAsync(request.Message, session, runOptions, cancellationToken);
    return Results.Ok(new ChatResponse(response.Text, resolvedName, conversationId));
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

// Builds run options that inject the active workspace's skill catalogue into the agent's instructions
// for a single run. Returns null when the agent does not use skills or the workspace declares none, so
// the agent runs with just its base instructions. Must be called while the workspace scope is active.
static AgentRunOptions? BuildSkillRunOptions(AgentCatalog catalog, string agentName, SkillLoader skills)
{
    if (!catalog.SupportsSkills(agentName))
    {
        return null;
    }

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
internal sealed record FlowChatRequest(string Message, string? Agent, string SessionId, string ConversationId, bool Manual = false, int StepDelayMs = 0, string? Workspace = null, IReadOnlyList<string>? DisabledTools = null);
internal sealed record FlowControlRequest(string SessionId, string? Action = null, bool? Manual = null, int? DelayMs = null);
internal sealed record ConversationResetRequest(string ConversationId);
