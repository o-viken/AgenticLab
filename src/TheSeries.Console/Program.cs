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
Console.WriteLine("Ask a question, switch with '/agent <name>', list with '/agents', toggle tools with '/tools [name]', set a folder with '/workspace <path>', start over with '/new', or press Enter on an empty line to quit.");

// One conversation for this session so the agent remembers prior turns; '/new' starts a fresh one.
var conversationId = Guid.NewGuid().ToString("n");

// The workspace folder used by agents that require one (e.g. Coder); set with '/workspace <path>'.
string? workspace = null;

// Tools the user has switched off for the current agent; cleared when switching agents.
var disabledTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

    if (message.TrimStart().StartsWith("/workspace", StringComparison.OrdinalIgnoreCase))
    {
        var path = message.Trim().Length > "/workspace".Length
            ? message.Trim()["/workspace".Length..].Trim().Trim('"')
            : string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            Console.WriteLine(workspace is null ? "No workspace set." : $"Workspace: {workspace}");
        }
        else if (!Directory.Exists(path))
        {
            Console.WriteLine($"Directory does not exist: {path}");
        }
        else
        {
            workspace = Path.GetFullPath(path);
            Console.WriteLine($"Workspace set to {workspace}.");
        }

        Console.WriteLine();
        continue;
    }

    if (message.Trim().Equals("/new", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            using var reset = await http.PostAsJsonAsync("/chat/reset", new { conversationId });
            reset.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not reset the conversation: {ex.Message}");
        }

        conversationId = Guid.NewGuid().ToString("n");
        Console.WriteLine("Started a new conversation.");
        Console.WriteLine();
        continue;
    }

    if (message.TrimStart().StartsWith("/tools", StringComparison.OrdinalIgnoreCase))
    {
        var selectedAgent = agents.FirstOrDefault(a => a.Name.Equals(currentAgent, StringComparison.OrdinalIgnoreCase));
        var tools = selectedAgent?.Tools ?? Array.Empty<string>();
        var arg = message.Trim().Length > "/tools".Length
            ? message.Trim()["/tools".Length..].Trim()
            : string.Empty;

        if (tools.Count == 0)
        {
            Console.WriteLine($"Agent '{currentAgent}' has no tools to toggle.");
        }
        else if (string.IsNullOrWhiteSpace(arg))
        {
            Console.WriteLine($"Tools for {currentAgent}:");
            foreach (var tool in tools)
            {
                Console.WriteLine($"   [{(disabledTools.Contains(tool) ? " " : "x")}] {tool}");
            }
        }
        else
        {
            var match = tools.FirstOrDefault(t => t.Equals(arg, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                Console.WriteLine($"Unknown tool '{arg}'. Available: {string.Join(", ", tools)}");
            }
            else if (disabledTools.Remove(match))
            {
                Console.WriteLine($"Enabled {match}.");
            }
            else
            {
                disabledTools.Add(match);
                Console.WriteLine($"Disabled {match}.");
            }
        }

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
            disabledTools.Clear();
            Console.WriteLine($"Switched to {match.Name}.");
        }

        Console.WriteLine();
        continue;
    }

    try
    {
        var selected = agents.FirstOrDefault(a => a.Name.Equals(currentAgent, StringComparison.OrdinalIgnoreCase));
        if (selected?.RequiresWorkspace == true && string.IsNullOrWhiteSpace(workspace))
        {
            Console.WriteLine($"Agent '{currentAgent}' needs a workspace. Set one with '/workspace <path>' first.");
            Console.WriteLine();
            continue;
        }

        using var response = await http.PostAsJsonAsync("/chat", new { message, agent = currentAgent, conversationId, workspace, disabledTools = disabledTools.Count > 0 ? disabledTools.ToArray() : null });
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
internal sealed record AgentInfo(string Name, string Description, IReadOnlyList<string> Tools, bool RequiresWorkspace = false);
internal sealed record AgentsResponse(IReadOnlyList<AgentInfo> Agents, string Default);

