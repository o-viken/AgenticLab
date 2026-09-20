using AgenticLab.Extensibility.Runtime;

namespace AgenticLab.AiService.Application.Conversations;

/// <summary>Host-owned identity reactivated alongside the other ambient scopes after streaming yields.</summary>
public sealed class AgentRunScope : IDisposable
{
    private static readonly AsyncLocal<AgentRunIdentity?> Identity = new();
    private readonly AgentRunIdentity? _previous;
    private readonly AgentRunIdentity _identity;

    /// <summary>Opens an identity scope; tool arguments cannot replace this identity.</summary>
    public AgentRunScope(string conversationId, string agentName)
    {
        _previous = Identity.Value;
        _identity = new(conversationId, agentName);
        Activate();
    }

    /// <summary>The identity active on this async execution context.</summary>
    public static AgentRunIdentity? Current => Identity.Value;
    /// <summary>Restores this run after an async iterator resumes.</summary>
    public void Activate() => Identity.Value = _identity;
    /// <inheritdoc />
    public void Dispose()
    {
        if (ReferenceEquals(Identity.Value, _identity)) Identity.Value = _previous;
    }
}

/// <summary>The read-only identity adapter shared with example assemblies.</summary>
public sealed class AgentRunContext : IAgentRunContext
{
    /// <inheritdoc />
    public AgentRunIdentity? Current => AgentRunScope.Current;
}