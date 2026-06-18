using System.Collections.Concurrent;
using Microsoft.Agents.AI;

namespace TheSeries.AiService.Agents;

/// <summary>
/// Keeps one <see cref="AgentSession"/> per conversation id so an agent run can continue an existing
/// conversation instead of starting fresh every time. The agents themselves are stateless singletons;
/// the per-conversation message history lives here, on the session, and is created lazily on first use.
/// </summary>
/// <remarks>
/// The session holds the conversation's chat messages and is interchangeable across agents, so a
/// conversation may switch agents mid-flight and still keep its history. Sessions are held in memory for
/// the lifetime of the process and are intended for sequential use within a single conversation.
/// </remarks>
public sealed class ConversationStore
{
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();

    /// <summary>
    /// Returns the existing session for <paramref name="conversationId"/>, or creates and stores a new one
    /// from <paramref name="agent"/> when the conversation has not been seen before.
    /// </summary>
    /// <param name="conversationId">The unique id of the conversation.</param>
    /// <param name="agent">The agent used to create a new session when one does not yet exist.</param>
    /// <param name="cancellationToken">A token to cancel session creation.</param>
    /// <returns>The session carrying the conversation's history.</returns>
    public async ValueTask<AgentSession> GetOrCreateAsync(string conversationId, AIAgent agent, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(conversationId, out var existing))
        {
            return existing;
        }

        // Conversations are used sequentially, so a rare concurrent create just discards the loser.
        var created = await agent.CreateSessionAsync(cancellationToken);
        return _sessions.GetOrAdd(conversationId, created);
    }

    /// <summary>Forgets the conversation, discarding its history so the next message starts fresh.</summary>
    /// <param name="conversationId">The id of the conversation to clear.</param>
    public void Reset(string conversationId) => _sessions.TryRemove(conversationId, out _);
}
