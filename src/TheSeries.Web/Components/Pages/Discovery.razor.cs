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

    /// <summary>Omits the standalone header when hosted inside the live Flow dialog.</summary>
    [Parameter] public bool Embedded { get; set; }

    /// <summary>Prevents reconnecting shared tool clients while the parent conversation is running.</summary>
    [Parameter] public bool AllowRediscovery { get; set; } = true;

    /// <summary>Whether this view started a discovery pass that may have changed the shared catalog.</summary>
    public bool HasRediscovered { get; private set; }

    private readonly CancellationTokenSource _lifetime = new();
    private TaskCompletionSource? _completion;
    private bool _disposed;
    private bool _closing;
    private string RediscoveryTitle => AllowRediscovery ? "Re-discover tools and agents" : "Wait for the conversation run to finish before re-discovering";

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
        DiscoverySnapshotResponse? snapshot;
        try
        {
            snapshot = await Ai.GetDiscoveryAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            if (_disposed || _closing) return;
            // The AI service being unreachable is a state to show, not a reason to fail the page.
            foreach (var source in Sources)
            {
                _state[source] = "Failed";
                _phase[source] = "error";
                _error[source] = $"Could not reach the AI service: {ex.Message}";
            }

            return;
        }

        if (snapshot is null || _disposed || _closing)
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
        if (_running || !AllowRediscovery || _disposed || _closing)
        {
            return;
        }

        _running = true;
        HasRediscovered = true;
        _paused = false;
        _runningSource = string.IsNullOrWhiteSpace(source) ? "all" : source;
        _sessionId = Guid.NewGuid().ToString("N");
        _cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _completion = completion;

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
                if (_disposed || _closing) break;
                Apply(evt);
                StateHasChanged();
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped by the user; leave the partial state as-is.
        }
        catch (Exception ex)
        {
            foreach (var affected in Sources.Where(candidate => _runningSource == "all" || _runningSource == candidate))
            {
                _state[affected] = "Failed";
                _error[affected] = ex.Message;
            }
        }
        finally
        {
            _paused = false;
            _runningSource = null;
            _cts?.Dispose();
            _cts = null;
            _lastRunUtc = DateTimeOffset.UtcNow;
            try
            {
                if (!_disposed && !_closing) await LoadSnapshotAsync();
            }
            finally
            {
                _running = false;
                completion.TrySetResult();
            }
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

    /// <summary>Cancels only this discovery stream and waits for its reader to finish before dismissing the dialog.</summary>
    public async Task CancelAsync()
    {
        _closing = true;
        _lifetime.Cancel();
        _cts?.Cancel();
        if (_completion is not null) await _completion.Task;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
