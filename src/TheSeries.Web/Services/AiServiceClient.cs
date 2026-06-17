using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace TheSeries.Web;

/// <summary>
/// Typed client for the AI service. Discovers the available agents and consumes the
/// <c>POST /chat/stream</c> Server-Sent Events endpoint that drives the flow animation.
/// </summary>
/// <param name="http">The HttpClient configured to reach the <c>aiservice</c> resource.</param>
internal sealed class AiServiceClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Lists the available agents and which one is used by default.</summary>
    public async Task<AgentsResponse?> GetAgentsAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<AgentsResponse>("/agents", JsonOptions, cancellationToken);

    /// <summary>
    /// Sends a message and yields each <see cref="FlowEvent"/> as the agent run progresses.
    /// </summary>
    /// <param name="message">The user's message.</param>
    /// <param name="agent">The agent to use, or null/blank for the default.</param>
    /// <param name="stepDelayMs">An artificial server-side delay applied after each step, in milliseconds.</param>
    /// <param name="cancellationToken">A token to cancel the stream.</param>
    public async IAsyncEnumerable<FlowEvent> StreamFlowAsync(
        string message,
        string? agent,
        int stepDelayMs,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/chat/stream")
        {
            Content = JsonContent.Create(new FlowChatRequest(message, agent, stepDelayMs)),
        };
        request.Headers.Accept.ParseAdd("text/event-stream");

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var parser = SseParser.Create(stream, (_, data) =>
            JsonSerializer.Deserialize<FlowEvent>(data, JsonOptions));

        await foreach (var item in parser.EnumerateAsync(cancellationToken))
        {
            if (item.Data is { } flowEvent)
            {
                yield return flowEvent;
            }
        }
    }
}

internal sealed record AgentInfo(string Name, string Description);
internal sealed record AgentsResponse(IReadOnlyList<AgentInfo> Agents, string Default);
internal sealed record FlowChatRequest(string Message, string? Agent, int StepDelayMs);
internal sealed record FlowEvent(int Sequence, string Kind, string Label, string? Detail);
