using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.Http.Json;

var builder = Host.CreateApplicationBuilder(args);

// OpenTelemetry + service discovery (resolves the "aiservice" name from Aspire).
builder.AddServiceDefaults();

// Under Aspire orchestration the service is reached by name via service discovery.
// When run standalone, pass the AI service URL, e.g. --AiService:Url https://localhost:7123
var serviceUrl = builder.Configuration["AiService:Url"] ?? "https+http://aiservice";
builder.Services.AddHttpClient("aiservice", client =>
    client.BaseAddress = new Uri(serviceUrl));

using var host = builder.Build();
await host.StartAsync();

var http = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("aiservice");

// Discover the available agents and start on the server's default.
var agents = new List<AgentInfo>();
string? currentAgent = null;
try
{
    var listing = await http.GetFromJsonAsync<AgentsResponse>("/agents");
    if (listing is not null)
    {
        agents.AddRange(listing.Agents);
        currentAgent = listing.Default;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Could not load agents from the AI service: {ex.Message}");
}

PrintAgents(agents, currentAgent);
Console.WriteLine();
Console.WriteLine("Ask a question, switch with '/agent <name>', list with '/agents', or press Enter on an empty line to quit.");

while (true)
{
    Console.Write($"{currentAgent ?? "?"}> ");
    var message = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(message))
    {
        break;
    }

    if (message.Trim().Equals("/agents", StringComparison.OrdinalIgnoreCase))
    {
        PrintAgents(agents, currentAgent);
        Console.WriteLine();
        continue;
    }

    if (message.TrimStart().StartsWith("/agent ", StringComparison.OrdinalIgnoreCase))
    {
        var name = message.Trim()["/agent ".Length..].Trim();
        var match = agents.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            Console.WriteLine($"Unknown agent '{name}'. Available: {string.Join(", ", agents.Select(a => a.Name))}");
        }
        else
        {
            currentAgent = match.Name;
            Console.WriteLine($"Switched to {match.Name}.");
        }

        Console.WriteLine();
        continue;
    }

    try
    {
        using var response = await http.PostAsJsonAsync("/chat", new { message, agent = currentAgent });
        response.EnsureSuccessStatusCode();
        var reply = await response.Content.ReadFromJsonAsync<ChatReply>();
        Console.WriteLine(reply?.Reply ?? "(no reply)");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error talking to the AI service: {ex.Message}");
    }

    Console.WriteLine();
}

await host.StopAsync();

static void PrintAgents(IReadOnlyList<AgentInfo> agents, string? currentAgent)
{
    if (agents.Count == 0)
    {
        Console.WriteLine("No agents available.");
        return;
    }

    Console.WriteLine("Available agents:");
    foreach (var agent in agents)
    {
        var marker = agent.Name.Equals(currentAgent, StringComparison.OrdinalIgnoreCase) ? "*" : " ";
        Console.WriteLine($" {marker} {agent.Name} - {agent.Description}");
    }
}

internal sealed record ChatReply(string Reply);
internal sealed record AgentInfo(string Name, string Description);
internal sealed record AgentsResponse(IReadOnlyList<AgentInfo> Agents, string Default);

