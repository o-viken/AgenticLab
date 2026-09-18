using System.Diagnostics.Metrics;
using System.Text.Json;

namespace TheSeries.Web.Flow;

/// <summary>Limits archived replay data per Flow page; the current exchange is not archived.</summary>
internal sealed class ReplayRetentionOptions
{
    /// <summary>Maximum number of complete archived exchanges retained per page.</summary>
    public int MaxArchivedExchanges { get; set; } = 20;

    /// <summary>Maximum estimated UTF-16 text payload bytes across archived exchanges.</summary>
    public long MaxArchivedPayloadBytes { get; set; } = 16 * 1024 * 1024;
}

/// <summary>Retains whole recent exchanges and accounts for their payload without storing telemetry content.</summary>
internal sealed class ReplayHistory : IDisposable
{
    internal const string MeterName = "TheSeries.Web.Replay";
    private static readonly Meter Meter = new(MeterName);
    private static readonly UpDownCounter<long> Exchanges = Meter.CreateUpDownCounter<long>("replay.archived.exchanges");
    private static readonly UpDownCounter<long> Events = Meter.CreateUpDownCounter<long>("replay.archived.events");
    private static readonly UpDownCounter<long> Payload = Meter.CreateUpDownCounter<long>("replay.archived.payload_bytes", "By");
    private static readonly Counter<long> Evictions = Meter.CreateCounter<long>("replay.evicted.exchanges");
    private readonly List<ConversationTurn> _turns = [];
    private readonly Queue<long> _sizes = new();
    private readonly int _maxExchanges;
    private readonly long _maxBytes;
    private bool _disposed;

    internal ReplayHistory(ReplayRetentionOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(options.MaxArchivedExchanges);
        ArgumentOutOfRangeException.ThrowIfNegative(options.MaxArchivedPayloadBytes);
        _maxExchanges = options.MaxArchivedExchanges;
        _maxBytes = options.MaxArchivedPayloadBytes;
    }

    internal IReadOnlyList<ConversationTurn> Turns => _turns;
    internal long PayloadBytes { get; private set; }
    internal int EventCount { get; private set; }
    internal int EvictedExchanges { get; private set; }

    internal void Add(ConversationTurn turn)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var bytes = EstimatePayloadBytes(turn);
        _turns.Add(turn);
        _sizes.Enqueue(bytes);
        PayloadBytes += bytes;
        EventCount += turn.Events.Count;
        Exchanges.Add(1);
        Events.Add(turn.Events.Count);
        Payload.Add(bytes);

        while (_turns.Count > _maxExchanges || PayloadBytes > _maxBytes)
        {
            var removed = _turns[0];
            var removedBytes = _sizes.Dequeue();
            _turns.RemoveAt(0);
            PayloadBytes -= removedBytes;
            EventCount -= removed.Events.Count;
            EvictedExchanges++;
            Exchanges.Add(-1);
            Events.Add(-removed.Events.Count);
            Payload.Add(-removedBytes);
            Evictions.Add(1);
        }
    }

    internal void Clear()
    {
        Exchanges.Add(-_turns.Count);
        Events.Add(-EventCount);
        Payload.Add(-PayloadBytes);
        _turns.Clear();
        _sizes.Clear();
        PayloadBytes = 0;
        EventCount = 0;
        EvictedExchanges = 0;
    }

    internal static long EstimatePayloadBytes(ConversationTurn turn)
    {
        var bytes = TextBytes(turn.Id) + TextBytes(turn.Message) + TextBytes(turn.Agent)
            + TextBytes(turn.Reply) + TextBytes(turn.Error) + TextBytes(turn.Vendor) + TextBytes(turn.Workspace);
        foreach (var flowEvent in turn.Events)
        {
            bytes += TextBytes(flowEvent.Kind) + TextBytes(flowEvent.Label) + TextBytes(flowEvent.Detail)
                + TextBytes(flowEvent.Data) + TextBytes(flowEvent.CallId);
            if (flowEvent.ToolCall is { } call)
            {
                bytes += TextBytes(call.Name);
                if (call.Arguments.ValueKind != JsonValueKind.Undefined)
                {
                    bytes += TextBytes(call.Arguments.GetRawText());
                }
            }
        }
        foreach (var agent in turn.A2AAgents ?? [])
        {
            bytes += TextBytes(agent.Name) + TextBytes(agent.Description);
        }
        return bytes;
    }

    private static long TextBytes(string? text) => (long)(text?.Length ?? 0) * sizeof(char);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        Clear();
        _disposed = true;
    }
}