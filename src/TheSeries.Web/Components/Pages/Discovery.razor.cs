using Microsoft.AspNetCore.Components;

namespace TheSeries.Web.Components.Pages;

/// <summary>
/// Visualizes the AI service's tool/agent discovery. Shows the last-known status of each discovery source
/// (MCP tools and A2A agents) from <c>GET /discovery</c>, and lets the user trigger a live re-discovery
/// (<c>POST /discovery/stream</c>) — for MCP and A2A independently or both together — whose steps animate as
/// they arrive over Server-Sent Events, drawn as a client↔server flow with directional arrows (mirroring
/// the agent flow view). The run is paced on the server by a stepping session so it can be run
/// step-by-step (manual) or auto-paced, and paused/resumed/stopped via <c>POST /chat/control</c>.
/// Re-discovery cleans up the existing connections, discovers again and refreshes the agents that use the
/// discovered tools. Whether discovery also runs automatically at startup is controlled by the
/// <c>Discovery:OnStartup</c> service configuration flag (surfaced read-only here).
/// </summary>
public sealed partial class Discovery : ComponentBase, IDisposable
{
    private static readonly string[] Sources = ["mcp", "a2a"];

    [Inject]
    private AiServiceClient Ai { get; set; } = default!;

    private bool _discoverOnStartup = true;
    private bool _running;
    private bool _paused;
    private bool _manual;
    private int _stepDelayMs = 700;
    private string? _runningSource;
    private string _sessionId = string.Empty;
    private DateTimeOffset? _lastRunUtc;
    private CancellationTokenSource? _cts;

    // Per-source state, keyed by source ("mcp"/"a2a").
    private readonly Dictionary<string, List<DiscoveryEventDto>> _log = new()
    {
        ["mcp"] = [],
        ["a2a"] = [],
    };
    private readonly Dictionary<string, List<DiscoveryItemDto>> _items = new()
    {
        ["mcp"] = [],
        ["a2a"] = [],
    };
    private readonly Dictionary<string, string> _state = new()
    {
        ["mcp"] = "NotRun",
        ["a2a"] = "NotRun",
    };
    private readonly Dictionary<string, string?> _endpoint = new()
    {
        ["mcp"] = null,
        ["a2a"] = null,
    };
    private readonly Dictionary<string, string?> _error = new()
    {
        ["mcp"] = null,
        ["a2a"] = null,
    };

    // The current animation phase per source, driving which nodes/arrows light up in the flow diagram.
    private readonly Dictionary<string, string> _phase = new()
    {
        ["mcp"] = "idle",
        ["a2a"] = "idle",
    };

    /// <inheritdoc />
    protected override async Task OnInitializedAsync() => await LoadSnapshotAsync();

    private async Task LoadSnapshotAsync()
    {
        var snapshot = await Ai.GetDiscoveryAsync();
        if (snapshot is null)
        {
            return;
        }

        _discoverOnStartup = snapshot.DiscoverOnStartup;
        foreach (var source in snapshot.Sources)
        {
            if (!_items.ContainsKey(source.Source))
            {
                continue;
            }

            _items[source.Source] = [.. source.Items];
            _state[source.Source] = source.State;
            _endpoint[source.Source] = source.Endpoint;
            _error[source.Source] = source.Error;
            _phase[source.Source] = source.State switch
            {
                "Connected" => "done",
                "Failed" => "error",
                "NoEndpoint" => "none",
                _ => "idle",
            };
            if (source.LastRunUtc is { } run && (_lastRunUtc is null || run > _lastRunUtc))
            {
                _lastRunUtc = run;
            }
        }

        StateHasChanged();
    }

    // Starts a run for a single source ("mcp"/"a2a") or both (null/"all").
    private async Task RediscoverAsync(string? source)
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _paused = false;
        _runningSource = string.IsNullOrWhiteSpace(source) ? "all" : source;
        _sessionId = Guid.NewGuid().ToString("N");
        _cts = new CancellationTokenSource();

        // Reset the sources this run covers so the flow animates from a clean slate.
        foreach (var s in Sources)
        {
            if (_runningSource is "all" || _runningSource == s)
            {
                _log[s].Clear();
                _items[s].Clear();
                _state[s] = "Connecting";
                _error[s] = null;
                _phase[s] = "idle";
            }
        }

        StateHasChanged();

        try
        {
            await foreach (var evt in Ai.StreamDiscoveryAsync(_sessionId, source, _manual, _stepDelayMs, _cts.Token))
            {
                Apply(evt);
                StateHasChanged();
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped by the user; leave the partial state as-is.
        }
        finally
        {
            _running = false;
            _paused = false;
            _runningSource = null;
            _cts?.Dispose();
            _cts = null;
            _lastRunUtc = DateTimeOffset.UtcNow;
            await LoadSnapshotAsync();
        }
    }

    private void Apply(DiscoveryEventDto evt)
    {
        if (evt.Source is not ("mcp" or "a2a"))
        {
            return;
        }

        _log[evt.Source].Add(evt);

        if (evt is { Kind: "Item", Item: { } item })
        {
            _items[evt.Source].Add(item);
        }

        _phase[evt.Source] = evt.Kind switch
        {
            "Start" => "idle",
            "Cleanup" => "cleanup",
            "Endpoint" => "endpoint",
            "Connecting" => "connecting",
            "Listing" => "listing",
            "Item" => "receiving",
            "Done" => "done",
            "NoEndpoint" => "none",
            "Error" => "error",
            _ => _phase[evt.Source],
        };

        _state[evt.Source] = evt.Kind switch
        {
            "Done" => _state[evt.Source] == "Connecting" || _items[evt.Source].Count > 0 ? "Connected" : _state[evt.Source],
            "Error" => "Failed",
            "NoEndpoint" => "NoEndpoint",
            _ => _state[evt.Source],
        };

        if (evt.Kind == "Error")
        {
            _error[evt.Source] = evt.Message;
        }
    }

    // Stepping controls, delivered to the run's session via POST /chat/control.
    private Task NextAsync() => Control("next");

    private async Task TogglePauseAsync()
    {
        if (_paused)
        {
            _paused = false;
            await Control("resume");
        }
        else
        {
            _paused = true;
            await Control("pause");
        }
    }

    private Task StopAsync() => Control("stop");

    private async Task SetManualAsync(bool manual)
    {
        _manual = manual;
        if (_running)
        {
            await Ai.SendControlAsync(_sessionId, manual: manual);
        }
    }

    private async Task SetDelayAsync(ChangeEventArgs args)
    {
        if (int.TryParse(args.Value?.ToString(), out var delay))
        {
            _stepDelayMs = delay;
            if (_running && !_manual)
            {
                await Ai.SendControlAsync(_sessionId, delayMs: delay);
            }
        }
    }

    private async Task Control(string action)
    {
        if (!_running || string.IsNullOrEmpty(_sessionId))
        {
            return;
        }

        await Ai.SendControlAsync(_sessionId, action);
    }

    private bool IsRunningSource(string source) => _running && (_runningSource is "all" || _runningSource == source);

    private string SourceTitle(string source) => source switch
    {
        "mcp" => "MCP tools",
        "a2a" => "A2A agents",
        _ => source,
    };

    private string SourceSubtitle(string source) => source switch
    {
        "mcp" => "Model Context Protocol — tools discovered from a remote MCP server.",
        "a2a" => "Agent2Agent — agents this service can delegate to.",
        _ => string.Empty,
    };

    private string ServerLabel(string source) => source switch
    {
        "mcp" => "mcpserver",
        "a2a" => "a2aserver",
        _ => "server",
    };

    private string ServerIcon(string source) => source switch
    {
        "mcp" => "🧩",
        "a2a" => "🤝",
        _ => "🖥️",
    };

    private string StateClass(string source) => _state.GetValueOrDefault(source) switch
    {
        "Connected" => "state-connected",
        "Failed" => "state-failed",
        "Connecting" => "state-connecting",
        "NoEndpoint" => "state-none",
        _ => "state-notrun",
    };

    private static string StateLabel(string state) => state switch
    {
        "Connected" => "Connected",
        "Failed" => "Failed",
        "Connecting" => "Discovering…",
        "NoEndpoint" => "No endpoint",
        _ => "Not run",
    };

    // Flow-diagram helpers: which nodes/arrows are lit for a source's current phase.
    private string ClientNodeClass(string source) =>
        IsRunningSource(source) && _phase.GetValueOrDefault(source) is "cleanup" or "endpoint" or "connecting" or "listing" or "receiving"
            ? "active" : "";

    private string ServerNodeClass(string source) =>
        _phase.GetValueOrDefault(source) is "connecting" or "listing" or "receiving" or "done"
            ? "active" : "";

    // Arrows animate only while the source is actively running, so they stop when the run finishes.
    private string SendArrowClass(string source) =>
        IsRunningSource(source) && _phase.GetValueOrDefault(source) is "connecting" or "listing" ? "active" : "";

    private string RecvArrowClass(string source) =>
        IsRunningSource(source) && _phase.GetValueOrDefault(source) is "receiving" ? "active" : "";

    private static string StepClass(string kind) => kind switch
    {
        "Error" => "step-error",
        "Done" or "Complete" => "step-done",
        "Item" => "step-item",
        "NoEndpoint" => "step-warn",
        "Connecting" or "Listing" => "step-send",
        _ => "step-info",
    };

    // Expand/collapse state for step and item detail panels, keyed so each row toggles independently.
    private readonly HashSet<string> _expanded = [];

    private bool IsExpanded(string key) => _expanded.Contains(key);

    private void Toggle(string key)
    {
        if (!_expanded.Remove(key))
        {
            _expanded.Add(key);
        }
    }

    // The rich detail for a step: for a discovered tool/agent it's the definition (name + description);
    // otherwise the step message.
    private static bool HasStepDetail(DiscoveryEventDto evt) =>
        evt.Item is not null || !string.IsNullOrWhiteSpace(evt.Message);

    private static string StepDetailTitle(DiscoveryEventDto evt) =>
        evt.Item is { } item ? item.Name : evt.Kind;

    private static string StepDetailBody(DiscoveryEventDto evt) =>
        evt.Item is { Description: var d } && !string.IsNullOrWhiteSpace(d) ? d : evt.Message;

    /// <inheritdoc />
    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
