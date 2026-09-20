using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using AgenticLab.Extensibility.Runtime;

namespace AgenticLab.AiService.Application.Discovery;

/// <summary>
/// Connects to the remote Model Context Protocol (MCP) server over HTTP, discovers its tools, and
/// exposes them as <see cref="AITool"/>s the agents can call. The connection can be established once at
/// startup (<see cref="ConnectAsync"/>) or on demand (<see cref="RediscoverAsync"/>, which first tears
/// down any existing connection and clears the cached tools before discovering again). The discovered
/// tools are cached and shared as a singleton so every agent that opts into MCP reuses the same client.
/// When the server is unavailable the provider degrades gracefully to an empty tool list instead of
/// failing the service.
/// </summary>
public sealed class McpToolProvider(IConfiguration configuration, ILogger<McpToolProvider> logger) : IAsyncDisposable, IMcpToolSource
{
    private McpClient? _client;
    private IList<AITool> _tools = [];
    private IReadOnlyList<McpToolInfo> _toolInfos = [];
    private DiscoverySourceStatus _status = new("mcp", null, DiscoveryState.NotRun, null, null, []);

    /// <summary>The MCP server resource name, used for service discovery and display.</summary>
    public string ServerName => "mcpserver";

    /// <summary>Whether a connection was established and at least one tool was discovered.</summary>
    public bool IsConnected => _tools.Count > 0;

    /// <summary>The MCP-discovered tools, ready to be handed to an agent. Empty until discovery runs.</summary>
    public IList<AITool> GetTools() => _tools;

    /// <inheritdoc />
    public IList<AITool> GetTools(IReadOnlyCollection<string> names) =>
        _tools.Where(tool => names.Contains(tool.Name, StringComparer.Ordinal)).ToList();

    /// <summary>The discovered tools' names and descriptions, surfaced so clients can show what was discovered.</summary>
    public IReadOnlyList<McpToolInfo> ToolInfos => _toolInfos;

    /// <summary>The current discovery status (endpoint, state, last-run time and discovered tools).</summary>
    public DiscoverySourceStatus Status => _status;

    /// <summary>
    /// Connects to the MCP server (resolved via service discovery) and lists its tools. Safe to call once
    /// at startup; failures are logged and leave the tool list empty so the agents still load. This is a
    /// convenience wrapper that drains <see cref="RediscoverAsync"/>.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await foreach (var _ in RediscoverAsync(cancellationToken))
        {
            // Draining the stream performs the discovery; the events are only needed by streaming callers.
        }
    }

    /// <summary>
    /// Tears down any existing MCP connection and cached tools, then connects to the MCP server (resolved
    /// via service discovery, falling back to the <c>Mcp:Endpoint</c> configuration value for a standalone
    /// run) and lists its tools again, yielding a <see cref="DiscoveryEvent"/> for each step so the process
    /// can be visualized. Failures are logged and leave the tool list empty so the agents still load.
    /// </summary>
    public async IAsyncEnumerable<DiscoveryEvent> RediscoverAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var seq = 0;
        yield return new DiscoveryEvent("mcp", DiscoveryEventKind.Start, "Starting MCP tool discovery.", Sequence: seq++);

        // Clean up any previous connection and cached tools before rediscovering.
        await CleanupAsync();
        yield return new DiscoveryEvent("mcp", DiscoveryEventKind.Cleanup, "Cleared previous MCP connection and cached tools.", Sequence: seq++);

        var endpoint = configuration["services:mcpserver:http:0"]
            ?? configuration["services:mcpserver:https:0"]
            ?? configuration["Mcp:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            logger.LogWarning("No MCP server endpoint configured (services:mcpserver:* or Mcp:Endpoint); skipping MCP tool discovery.");
            _status = new DiscoverySourceStatus("mcp", null, DiscoveryState.NoEndpoint, null, DateTimeOffset.UtcNow, []);
            yield return new DiscoveryEvent("mcp", DiscoveryEventKind.NoEndpoint, "No MCP server endpoint configured; skipping discovery.", Sequence: seq++);
            yield return new DiscoveryEvent("mcp", DiscoveryEventKind.Done, "MCP discovery finished (no endpoint).", Sequence: seq++);
            yield break;
        }

        _status = _status with { Endpoint = endpoint, State = DiscoveryState.Connecting };
        yield return new DiscoveryEvent("mcp", DiscoveryEventKind.Endpoint, $"Resolved MCP endpoint {endpoint}.", Sequence: seq++);

        IList<McpClientTool>? mcpTools = null;
        string? error = null;
        try
        {
            var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(endpoint) });
            _client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
            mcpTools = await _client.ListToolsAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not connect to MCP server at {Endpoint}; agents will run without MCP tools.", endpoint);
            error = ex.Message;
        }

        if (error is not null || mcpTools is null)
        {
            _status = new DiscoverySourceStatus("mcp", endpoint, DiscoveryState.Failed, error, DateTimeOffset.UtcNow, []);
            yield return new DiscoveryEvent("mcp", DiscoveryEventKind.Error, $"Could not reach MCP server: {error}", Sequence: seq++);
            yield break;
        }

        yield return new DiscoveryEvent("mcp", DiscoveryEventKind.Connecting, $"Connected to MCP server '{ServerName}'.", Sequence: seq++);
        yield return new DiscoveryEvent("mcp", DiscoveryEventKind.Listing, "Listing MCP tools.", Sequence: seq++);

        _tools = mcpTools.Cast<AITool>().ToList();
        _toolInfos = mcpTools.Select(t => new McpToolInfo(t.Name, t.Description ?? string.Empty)).ToList();

        var items = new List<DiscoveryItem>();
        foreach (var info in _toolInfos)
        {
            var item = new DiscoveryItem(info.Name, info.Description);
            items.Add(item);
            yield return new DiscoveryEvent("mcp", DiscoveryEventKind.Item, $"Discovered tool '{info.Name}'.", item, seq++);
        }

        _status = new DiscoverySourceStatus("mcp", endpoint, DiscoveryState.Connected, null, DateTimeOffset.UtcNow, items);
        logger.LogInformation("Discovered {Count} tool(s) from MCP server '{Server}' at {Endpoint}.", _tools.Count, ServerName, endpoint);
        yield return new DiscoveryEvent("mcp", DiscoveryEventKind.Done, $"MCP discovery finished — {items.Count} tool(s).", Sequence: seq++);
    }

    private async Task CleanupAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
        }

        _tools = [];
        _toolInfos = [];
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
