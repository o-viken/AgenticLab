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

// Each agent declares its own persona and tool subset; the first registered is the default.
builder.Services.AddSingleton<IAgentDefinition, WikiAssistantAgent>();
builder.Services.AddSingleton<IAgentDefinition, MathTutorAgent>();
builder.Services.AddSingleton<IAgentDefinition, TriviaMasterAgent>();

// The shared chat client and the catalog of agents are stateless and safe to share as singletons.
builder.Services.AddSingleton(sp =>
    AgentService.CreateChatClient(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton(sp =>
    new AgentCatalog(sp.GetRequiredService<IChatClient>(), sp.GetServices<IAgentDefinition>()));

// Projects a real agent run into an observable stream of flow events for the visualization UI.
builder.Services.AddSingleton<FlowControlRegistry>();
builder.Services.AddSingleton<FlowTracer>();

var app = builder.Build();

app.MapDefaultEndpoints();

// Lists the available agents and which one is used by default.
app.MapGet("/agents", (AgentCatalog catalog) =>
    Results.Ok(new AgentsResponse(catalog.Agents, catalog.DefaultName)));

app.MapPost("/chat", async (ChatRequest request, AgentCatalog catalog, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest("Message must not be empty.");
    }

    if (!catalog.TryResolve(request.Agent, out var agent, out var resolvedName))
    {
        return Results.BadRequest($"Unknown agent '{request.Agent}'. Call GET /agents for the available names.");
    }

    var response = await agent.RunAsync(request.Message, cancellationToken: cancellationToken);
    return Results.Ok(new ChatResponse(response.Text, resolvedName));
});

// Streams the steps of an agent run as Server-Sent Events so the web UI can animate the data flow live.
// The run is paced by a FlowSession so the client can step, pause, resume or stop the real execution.
app.MapPost("/chat/stream", (FlowChatRequest request, FlowTracer tracer, FlowControlRegistry registry, CancellationToken cancellationToken) =>
{
    var session = registry.Create(request.SessionId, request.Manual, request.StepDelayMs);
    return TypedResults.ServerSentEvents(
        tracer.StreamAsync(request.Message, request.Agent, session, cancellationToken),
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

app.Run();

internal sealed record ChatRequest(string Message, string? Agent = null);
internal sealed record ChatResponse(string Reply, string Agent);
internal sealed record AgentsResponse(IReadOnlyList<AgentInfo> Agents, string Default);
internal sealed record FlowChatRequest(string Message, string? Agent, string SessionId, bool Manual = false, int StepDelayMs = 0);
internal sealed record FlowControlRequest(string SessionId, string? Action = null, bool? Manual = null, int? DelayMs = null);
