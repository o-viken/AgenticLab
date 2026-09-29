using System.ClientModel.Primitives;
using System.Net.ServerSentEvents;
using System.Text.Json.Nodes;

namespace AgenticLab.ModelProviders;

/// <summary>Normalizes null chat deltas before the OpenAI adapter probes them for reasoning content.</summary>
internal sealed class NullDeltaStreamingPolicy : PipelinePolicy
{
    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        ProcessNext(message, pipeline, currentIndex);
        NormalizeResponse(message);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
        NormalizeResponse(message);
    }

    private static void NormalizeResponse(PipelineMessage message)
    {
        if (message.Response is { IsError: false, ContentStream: { } stream } response &&
            message.Request.Uri is { } uri && uri.AbsolutePath.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) &&
            response.Headers.TryGetValue("Content-Type", out var contentType) &&
            contentType?.StartsWith("text/event-stream", StringComparison.OrdinalIgnoreCase) is true)
        {
            response.ContentStream = new NormalizingStream(stream, message.CancellationToken);
        }
    }

    private static string NormalizeData(string data)
    {
        if (data.AsSpan().Trim().SequenceEqual("[DONE]")) return data;
        if (JsonNode.Parse(data) is not JsonObject root || root["choices"] is not JsonArray choices) return data;

        var changed = false;
        foreach (var choice in choices)
        {
            if (choice is JsonObject item && item.TryGetPropertyValue("delta", out var delta) && delta is null)
            {
                item["delta"] = new JsonObject();
                changed = true;
            }
        }
        return changed ? root.ToJsonString() : data;
    }

    private sealed class NormalizingStream(Stream source, CancellationToken cancellationToken) : Stream
    {
        private readonly IAsyncEnumerator<SseItem<string>> _events =
            SseParser.Create(source).EnumerateAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
        private readonly MemoryStream _buffer = new();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.IsEmpty) return 0;
            cancellationToken.ThrowIfCancellationRequested();
            if (_buffer.Position == _buffer.Length)
            {
                if (!await _events.MoveNextAsync().ConfigureAwait(false)) return 0;
                var item = _events.Current;
                var normalized = new SseItem<string>(NormalizeData(item.Data), item.EventType)
                {
                    EventId = item.EventId,
                    ReconnectionInterval = item.ReconnectionInterval,
                };
                _buffer.SetLength(0);
                await SseFormatter.WriteAsync(SingleEvent(normalized), _buffer, cancellationToken).ConfigureAwait(false);
                _buffer.Position = 0;
            }
            return await _buffer.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        private static async IAsyncEnumerable<SseItem<string>> SingleEvent(SseItem<string> item)
        {
            yield return item;
            await Task.CompletedTask;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _events.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                finally { _buffer.Dispose(); source.Dispose(); }
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            try { await _events.DisposeAsync().ConfigureAwait(false); }
            finally { _buffer.Dispose(); await source.DisposeAsync().ConfigureAwait(false); }
            GC.SuppressFinalize(this);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}