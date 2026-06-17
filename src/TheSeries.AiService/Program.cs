using Microsoft.Extensions.AI;
using TheSeries.AiService;
using TheSeries.AiService.Agents;
using TheSeries.AiService.Tools;

var builder = WebApplication.CreateBuilder(args);

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

app.Run();

internal sealed record ChatRequest(string Message, string? Agent = null);
internal sealed record ChatResponse(string Reply, string Agent);
internal sealed record AgentsResponse(IReadOnlyList<AgentInfo> Agents, string Default);
