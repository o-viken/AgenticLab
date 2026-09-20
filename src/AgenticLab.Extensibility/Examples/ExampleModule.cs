using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticLab.Extensibility.Examples;

/// <summary>The executable role loading an explicitly registered, trusted example assembly.</summary>
public enum ExampleHost
{
    /// <summary>The coordinator, operational API and mutable process services.</summary>
    AiService,
    /// <summary>Remote tools and their bounded service clients.</summary>
    Mcp,
    /// <summary>Remote persona-only specialist definitions.</summary>
    A2A,
    /// <summary>Example panels and their operational API clients.</summary>
    Web,
}

/// <summary>Public example metadata; contains no component types, credentials or remote prompts.</summary>
public sealed record ExampleManifest(string Id, string DisplayName, IReadOnlyList<string> HostKeys,
    IReadOnlyList<string> AgentNames, bool RequiresUi = false)
{
    /// <summary>Tool-backed resources shown by the flow diagram.</summary>
    public IReadOnlyList<ExampleResource> Resources { get; init; } = [];
    /// <summary>Globally unique MCP names contributed by this module.</summary>
    public IReadOnlyList<string> McpToolNames { get; init; } = [];
    /// <summary>Globally unique remote specialist names contributed by this module.</summary>
    public IReadOnlyList<string> RemoteAgentNames { get; init; } = [];
    /// <summary>Module-owned descriptions of side effects for otherwise unknown tool names.</summary>
    public IReadOnlyDictionary<string, ExampleToolRisk> ToolRisks { get; init; } = new Dictionary<string, ExampleToolRisk>();
}

/// <summary>A presentation risk level (Low, Medium or High) and the actual enforced side-effect boundary.</summary>
public sealed record ExampleToolRisk(string Level, string Description);

/// <summary>A resource reached by a module's tools; presentation does not grant access.</summary>
public sealed record ExampleResource(string Key, string Title, string Transport, string[] ToolNames);

/// <summary>The identity of an opt-in example, shared by its independently hosted contributions.</summary>
public interface IExampleModule
{
    /// <summary>Stable public identity and the names this module owns.</summary>
    ExampleManifest Manifest { get; }
}

/// <summary>Backend services and routes owned by an example, without a separate executable.</summary>
public interface IAiServiceExample : IExampleModule
{
    /// <summary>Registers this role's state, tools and selectable agents.</summary>
    void AddAiServices(IServiceCollection services, IConfiguration configuration);
    /// <summary>Maps routes beneath the module's supplied /examples/{id}/api prefix.</summary>
    void MapApi(RouteGroupBuilder group);
}

/// <summary>Remote read capabilities registered using the standard MCP SDK.</summary>
public interface IMcpExample : IExampleModule
{
    /// <summary>Registers protocol clients, never the backend's mutable process store.</summary>
    void AddMcpServices(IServiceCollection services, IConfiguration configuration);
    /// <summary>Registers tool metadata without performing network calls during discovery.</summary>
    void AddMcpTools(IMcpServerBuilder builder);
}

/// <summary>A remote persona-only agent; instructions remain on its host.</summary>
public sealed record RemoteAgentDefinition(string Name, string Description, string Instructions);

/// <summary>Specialists hosted on the existing A2A server and its configured model.</summary>
public interface IA2AExample : IExampleModule
{
    /// <summary>The remote agents to register when this role is enabled.</summary>
    IReadOnlyList<RemoteAgentDefinition> Specialists { get; }
}

/// <summary>A locally compiled panel and its HTTP clients, independent of Web's Flow state types.</summary>
public interface IWebExample : IExampleModule
{
    /// <summary>A local IComponent type accepting an ExamplePanelContext parameter named Context.</summary>
    Type PanelType { get; }
    /// <summary>Registers this role's clients; mutable backend state must not be registered here.</summary>
    void AddWebServices(IServiceCollection services, IConfiguration configuration);
}

/// <summary>A snapshot of the host page plus narrow commands available to an example panel.</summary>
public sealed record ExamplePanelContext(string ConversationId, string HostKey, string? AgentName,
    bool Running, int RunVersion, Action<string> SetDraft, Func<Task<string>> NewConversation);

/// <summary>Optional awaited cleanup before the host starts a fresh conversation.</summary>
public interface IExamplePanel : IComponent
{
    /// <summary>Archives or releases example state; a failure prevents the reset.</summary>
    Task BeforeResetAsync(CancellationToken cancellationToken = default);
}