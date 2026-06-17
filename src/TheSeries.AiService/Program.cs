using Microsoft.Agents.AI;
using TheSeries.AiService;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry, health checks, service discovery and resilience.
builder.AddServiceDefaults();

// HttpClient used by the Wikipedia tool. Wikipedia requires a descriptive User-Agent.
builder.Services.AddHttpClient("wikipedia", client =>
{
    client.BaseAddress = new Uri("https://en.wikipedia.org");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("TheSeries-WikiAssistant/1.0 (https://github.com/Equinor/the-series)");
});

builder.Services.AddSingleton(sp =>
    new WikiTool(sp.GetRequiredService<IHttpClientFactory>().CreateClient("wikipedia")));

// The agent is stateless and safe to share as a singleton.
builder.Services.AddSingleton(sp =>
    AgentService.CreateWikiAgent(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<WikiTool>()));

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapPost("/chat", async (ChatRequest request, AIAgent agent, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest("Message must not be empty.");
    }

    var response = await agent.RunAsync(request.Message, cancellationToken: cancellationToken);
    return Results.Ok(new ChatResponse(response.Text));
});

app.Run();

internal sealed record ChatRequest(string Message);
internal sealed record ChatResponse(string Reply);
