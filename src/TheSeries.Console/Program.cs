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

Console.WriteLine("WikiAssistant ready. Ask a question, or press Enter on an empty line to quit.");
while (true)
{
    Console.Write("> ");
    var message = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(message))
    {
        break;
    }

    try
    {
        using var response = await http.PostAsJsonAsync("/chat", new { message });
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

internal sealed record ChatReply(string Reply);

