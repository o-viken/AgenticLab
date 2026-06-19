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
    /// Lists the skills discovered in the given workspace (names + descriptions) so the catalogue can be
    /// shown in the harness before a run. Returns an empty list when the path is missing or declares none.
    /// </summary>
    /// <param name="workspace">The workspace path to scan for skills; null/blank yields an empty list.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    public async Task<SkillsResponse?> GetSkillsAsync(string? workspace, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("/skills", new SkillsRequest(workspace), JsonOptions, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<SkillsResponse>(JsonOptions, cancellationToken)
            : null;
    }

    /// <summary>
    /// Sends a message and yields each <see cref="FlowEvent"/> as the agent run progresses. The run is
    /// paced on the server by the matching <see cref="SendControlAsync"/> calls (keyed by session id).
    /// </summary>
    /// <param name="message">The user's message.</param>
    /// <param name="agent">The agent to use, or null/blank for the default.</param>
    /// <param name="sessionId">The unique id shared with the control calls.</param>
    /// <param name="conversationId">The conversation to continue, so the run remembers prior turns.</param>
    /// <param name="manual">When <c>true</c>, the run starts in manual stepping mode.</param>
    /// <param name="stepDelayMs">The auto-mode server-side delay applied before each step, in milliseconds.</param>
    /// <param name="workspace">The workspace path for agents that require one; null/blank otherwise.</param>
    /// <param name="disabledTools">The names of the agent's tools to hide from the model for this run; null/empty to offer them all.</param>
    /// <param name="cancellationToken">A token to cancel the stream.</param>
    public async IAsyncEnumerable<FlowEvent> StreamFlowAsync(
        string message,
        string? agent,
        string sessionId,
        string conversationId,
        bool manual,
        int stepDelayMs,
        string? workspace,
        IReadOnlyList<string>? disabledTools,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/chat/stream")
        {
            Content = JsonContent.Create(new FlowChatRequest(message, agent, sessionId, conversationId, manual, stepDelayMs, workspace, disabledTools)),
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

    /// <summary>
    /// Drives an in-flight flow run on the server: <c>next</c>, <c>pause</c>, <c>resume</c> or
    /// <c>stop</c>, and optionally switches mode or changes the auto delay.
    /// </summary>
    /// <param name="sessionId">The id of the run to control.</param>
    /// <param name="action">The action: <c>next</c>, <c>pause</c>, <c>resume</c> or <c>stop</c>.</param>
    /// <param name="manual">Optionally switch the stepping mode.</param>
    /// <param name="delayMs">Optionally change the auto-mode step delay.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    public async Task SendControlAsync(
        string sessionId,
        string? action = null,
        bool? manual = null,
        int? delayMs = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(
            "/chat/control",
            new FlowControlRequest(sessionId, action, manual, delayMs),
            JsonOptions,
            cancellationToken);
    }

    /// <summary>Clears a conversation's remembered history so the next message starts fresh.</summary>
    /// <param name="conversationId">The id of the conversation to reset.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    public async Task ResetConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(
            "/chat/reset",
            new ConversationResetRequest(conversationId),
            JsonOptions,
            cancellationToken);
    }
}

internal sealed record AgentInfo(string Name, string Description, IReadOnlyList<string> Tools, bool RequiresWorkspace = false, bool SupportsSkills = false);
internal sealed record AgentsResponse(IReadOnlyList<AgentInfo> Agents, string Default);
internal sealed record SkillsRequest(string? Workspace);
internal sealed record SkillsResponse(IReadOnlyList<SkillInfo> Skills);
internal sealed record SkillInfo(string Name, string Description);
internal sealed record FlowChatRequest(string Message, string? Agent, string SessionId, string ConversationId, bool Manual, int StepDelayMs, string? Workspace = null, IReadOnlyList<string>? DisabledTools = null);
internal sealed record FlowControlRequest(string SessionId, string? Action, bool? Manual, int? DelayMs);
internal sealed record ConversationResetRequest(string ConversationId);
internal sealed record FlowEvent(int Sequence, string Kind, string Label, string? Detail, int Turn = 0, string? Data = null);
