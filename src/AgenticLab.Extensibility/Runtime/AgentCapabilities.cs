using Microsoft.Extensions.AI;

namespace AgenticLab.Extensibility.Runtime;

/// <summary>Read-only identity supplied by the host, never chosen through tool arguments.</summary>
public sealed record AgentRunIdentity(string ConversationId, string AgentName);

/// <summary>Access to the current async run without exposing the host's session implementation.</summary>
public interface IAgentRunContext
{
    /// <summary>The active run, or null outside agent execution.</summary>
    AgentRunIdentity? Current { get; }
}

/// <summary>Access to explicitly published local host tools without depending on an executable host.</summary>
public interface IHostToolSource
{
    /// <summary>Returns tools in requested order using exact names; throws if any name is not published.</summary>
    IList<AITool> GetTools(IReadOnlyCollection<string> names);
}

/// <summary>Access to existing discovered MCP tools through an exact, per-agent allowlist.</summary>
public interface IMcpToolSource
{
    /// <summary>Returns only currently discovered tools whose names appear in the allowlist.</summary>
    IList<AITool> GetTools(IReadOnlyCollection<string> names);
}

/// <summary>A remote invocation outcome, separating transport failure from advisory answer text.</summary>
public sealed record AgentDelegationResult(bool Success, string Text);

/// <summary>A protocol gateway; callers remain responsible for their specialist allowlist.</summary>
public interface IAgentDelegation
{
    /// <summary>Calls an already discovered specialist, propagating cancellation.</summary>
    Task<AgentDelegationResult> InvokeAsync(string agentName, string question, CancellationToken cancellationToken = default);
}