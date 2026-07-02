using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace TheSeries.AiService.Application;

/// <summary>
/// Connects to the remote Model Context Protocol (MCP) server over HTTP, discovers its tools, and
/// exposes them as <see cref="AITool"/>s the agents can call. The connection is established once at
/// startup (<see cref="ConnectAsync"/>); the discovered tools are cached and shared as a singleton so
/// every agent that opts into MCP reuses the same client. When the server is unavailable the provider
/// degrades gracefully to an empty tool list instead of failing the service.
/// </summary>
public sealed class McpToolProvider(IConfiguration configuration, ILogger<McpToolProvider> logger) : IAsyncDisposable
{
    private McpClient? _client;
    private IList<AITool> _tools = [];
    private IReadOnlyList<McpToolInfo> _toolInfos = [];

    /// <summary>The MCP server resource name, used for service discovery and display.</summary>
    public string ServerName => "mcpserver";

    /// <summary>Whether a connection was established and at least one tool was discovered.</summary>
    public bool IsConnected => _tools.Count > 0;

    /// <summary>The MCP-discovered tools, ready to be handed to an agent. Empty until <see cref="ConnectAsync"/> runs.</summary>
    public IList<AITool> GetTools() => _tools;

    /// <summary>The discovered tools' names and descriptions, surfaced so clients can show what was discovered.</summary>
    public IReadOnlyList<McpToolInfo> ToolInfos => _toolInfos;

    /// <summary>
    /// Connects to the MCP server (resolved via service discovery) and lists its tools. Safe to call once
    /// at startup; failures are logged and leave the tool list empty so the agents still load.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        var endpoint = configuration["services:mcpserver:http:0"]
            ?? configuration["services:mcpserver:https:0"]
            ?? configuration["Mcp:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            logger.LogWarning("No MCP server endpoint configured (services:mcpserver:* or Mcp:Endpoint); skipping MCP tool discovery.");
            return;
        }

        try
        {
            var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(endpoint) });
            _client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
            var mcpTools = await _client.ListToolsAsync(cancellationToken: cancellationToken);
            _tools = mcpTools.Cast<AITool>().ToList();
            _toolInfos = mcpTools.Select(t => new McpToolInfo(t.Name, t.Description ?? string.Empty)).ToList();
            logger.LogInformation("Discovered {Count} tool(s) from MCP server '{Server}' at {Endpoint}.", _tools.Count, ServerName, endpoint);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not connect to MCP server at {Endpoint}; agents will run without MCP tools.", endpoint);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
    }
}

/// <summary>A discovered MCP tool's name and description.</summary>
public sealed record McpToolInfo(string Name, string Description);
