namespace TheSeries.AiService.Application;

/// <summary>
/// The kind of a <see cref="DiscoveryEvent"/> — the stage of a discovery run it represents. Emitted in
/// roughly this order per source: <see cref="Start"/>, <see cref="Cleanup"/>, <see cref="Endpoint"/>,
/// <see cref="Connecting"/>, <see cref="Listing"/>, one <see cref="Item"/> per discovered tool/agent, then
/// <see cref="Done"/> (or <see cref="Error"/> / <see cref="NoEndpoint"/> when it could not complete).
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum DiscoveryEventKind
{
    /// <summary>Discovery for a source is starting.</summary>
    Start,

    /// <summary>Existing connections/caches for the source are being torn down before rediscovery.</summary>
    Cleanup,

    /// <summary>The source's endpoint has been resolved.</summary>
    Endpoint,

    /// <summary>No endpoint is configured for the source, so discovery is skipped.</summary>
    NoEndpoint,

    /// <summary>A connection to the source is being established.</summary>
    Connecting,

    /// <summary>The source's tools/agents are being listed.</summary>
    Listing,

    /// <summary>A single tool/agent was discovered (carried in <see cref="DiscoveryEvent.Item"/>).</summary>
    Item,

    /// <summary>Discovery for the source finished successfully.</summary>
    Done,

    /// <summary>Discovery for the source failed (the reason is in <see cref="DiscoveryEvent.Message"/>).</summary>
    Error,

    /// <summary>The whole discovery run (all sources) has completed.</summary>
    Complete,
}

/// <summary>The outcome state of a discovery source, used for the status snapshot.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum DiscoveryState
{
    /// <summary>Discovery has not been run yet for this source.</summary>
    NotRun,

    /// <summary>No endpoint is configured, so the source cannot be discovered.</summary>
    NoEndpoint,

    /// <summary>A discovery run is in progress.</summary>
    Connecting,

    /// <summary>The source was reached and its tools/agents were discovered.</summary>
    Connected,

    /// <summary>The last discovery attempt failed.</summary>
    Failed,
}

/// <summary>
/// A single step of a live discovery run, streamed to the client so the discovery process (connect →
/// clean up → resolve endpoint → list → items → done) can be visualized as it happens. Mirrors the
/// flow visualizer's per-step event model, but for MCP/A2A discovery rather than an agent run.
/// </summary>
/// <param name="Source">The source being discovered — <c>"mcp"</c> or <c>"a2a"</c>.</param>
/// <param name="Kind">The stage this event represents.</param>
/// <param name="Message">A human-readable description of the step.</param>
/// <param name="Item">The discovered tool/agent for an <see cref="DiscoveryEventKind.Item"/> event; otherwise null.</param>
/// <param name="Sequence">A monotonically increasing index across the whole run, for ordering.</param>
public sealed record DiscoveryEvent(
    string Source,
    DiscoveryEventKind Kind,
    string Message,
    DiscoveryItem? Item = null,
    int Sequence = 0);

/// <summary>A discovered tool (MCP) or agent (A2A): its name and description.</summary>
/// <param name="Name">The tool/agent name.</param>
/// <param name="Description">A short description of what it does.</param>
public sealed record DiscoveryItem(string Name, string Description);

/// <summary>
/// The current status of a discovery source, returned by the discovery snapshot so a client can render
/// the last-known result without running discovery again.
/// </summary>
/// <param name="Source">The source — <c>"mcp"</c> or <c>"a2a"</c>.</param>
/// <param name="Endpoint">The resolved endpoint, or null when none is configured/known.</param>
/// <param name="State">The outcome state of the last discovery attempt.</param>
/// <param name="Error">The failure message when <see cref="State"/> is <see cref="DiscoveryState.Failed"/>.</param>
/// <param name="LastRunUtc">When discovery last ran for this source, or null if it never has.</param>
/// <param name="Items">The tools/agents discovered on the last successful run.</param>
public sealed record DiscoverySourceStatus(
    string Source,
    string? Endpoint,
    DiscoveryState State,
    string? Error,
    DateTimeOffset? LastRunUtc,
    IReadOnlyList<DiscoveryItem> Items);
