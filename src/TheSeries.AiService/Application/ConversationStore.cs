using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;

namespace TheSeries.AiService.Application;

/// <summary>
/// Keeps one <see cref="AgentSession"/> per conversation id so an agent run can continue an existing
/// conversation instead of starting fresh every time. The agents themselves are stateless singletons;
/// the per-conversation message history lives here, on the session, and is created lazily on first use.
/// </summary>
/// <remarks>
/// The session holds the conversation's chat messages and is interchangeable across agents, so a
/// conversation may switch agents mid-flight and still keep its history. Inactive sessions expire on a
/// sliding window and are intended for sequential use within a single conversation.
/// </remarks>
public sealed class ConversationStore : IDisposable
{
    /// <summary>The diagnostic meter emitted by the conversation store.</summary>
    public const string MeterName = "TheSeries.AiService.Conversations";

    private readonly ConcurrentDictionary<string, ConversationEntry> _sessions = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _inactiveTtl;
    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _expired;
    private readonly Counter<long> _reset;
    private readonly ITimer _cleanupTimer;

    internal int RetainedCount => _sessions.Count;

    /// <summary>Creates a store using the configured inactivity and cleanup intervals.</summary>
    public ConversationStore(IOptions<ConversationStoreOptions> options, TimeProvider timeProvider)
    {
        var configured = options.Value;
        if (configured.InactiveTtl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Conversation inactivity TTL must be greater than zero.");
        }
        if (configured.CleanupInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Conversation cleanup interval must be greater than zero.");
        }

        _timeProvider = timeProvider;
        _inactiveTtl = configured.InactiveTtl;
        _expired = _meter.CreateCounter<long>("conversations.expired", description: "Conversations removed after inactivity.");
        _reset = _meter.CreateCounter<long>("conversations.reset", description: "Conversations removed explicitly.");
        _meter.CreateObservableGauge("conversations.retained", () => _sessions.Count, description: "Conversations currently held in memory.");
        _cleanupTimer = timeProvider.CreateTimer(
            static state => ((ConversationStore)state!).RemoveExpired(),
            this,
            configured.CleanupInterval,
            configured.CleanupInterval);
    }

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
        while (_sessions.TryGetValue(conversationId, out var existing))
        {
            var now = _timeProvider.GetUtcNow();
            if (!IsExpired(existing, now))
            {
                if (_sessions.TryUpdate(conversationId, existing with { LastAccess = now }, existing))
                {
                    return existing.Session;
                }

                continue;
            }

            if (_sessions.TryRemove(new KeyValuePair<string, ConversationEntry>(conversationId, existing)))
            {
                _expired.Add(1);
            }
        }

        // Conversations are used sequentially, so a rare concurrent create just discards the loser.
        var created = await agent.CreateSessionAsync(cancellationToken);
        var createdEntry = new ConversationEntry(created, _timeProvider.GetUtcNow());
        return _sessions.GetOrAdd(conversationId, createdEntry).Session;
    }

    /// <summary>Forgets the conversation, discarding its history so the next message starts fresh.</summary>
    /// <param name="conversationId">The id of the conversation to clear.</param>
    public void Reset(string conversationId)
    {
        if (_sessions.TryRemove(conversationId, out _))
        {
            _reset.Add(1);
        }
    }

    private bool IsExpired(ConversationEntry entry, DateTimeOffset now) => now - entry.LastAccess >= _inactiveTtl;

    private void RemoveExpired()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var pair in _sessions)
        {
            if (IsExpired(pair.Value, now) && _sessions.TryRemove(pair))
            {
                _expired.Add(1);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cleanupTimer.Dispose();
        _meter.Dispose();
    }

    private sealed record ConversationEntry(AgentSession Session, DateTimeOffset LastAccess);
}

/// <summary>Configures how long inactive in-memory conversations are retained.</summary>
public sealed class ConversationStoreOptions
{
    /// <summary>The sliding inactivity window after which a conversation can be removed.</summary>
    public TimeSpan InactiveTtl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How frequently the store scans for conversations whose inactivity window elapsed.</summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);
}
