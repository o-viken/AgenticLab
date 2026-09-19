using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using AgenticLab.AiService.Application.Conversations;
using Xunit;

namespace AgenticLab.AiService.Tests;

public sealed class ConversationStoreTests
{
    [Fact]
    public async Task AccessRefreshesTheSlidingExpirationWindow()
    {
        var time = new FakeTimeProvider();
        using var client = new FakeChatClient();
        var agent = new ChatClientAgent(client, instructions: "Test", name: "Test");
        using var store = CreateStore(time);

        var first = await store.GetOrCreateAsync("conversation", agent);
        time.Advance(TimeSpan.FromMinutes(9));
        var refreshed = await store.GetOrCreateAsync("conversation", agent);
        time.Advance(TimeSpan.FromMinutes(9));
        var stillActive = await store.GetOrCreateAsync("conversation", agent);

        Assert.Same(first, refreshed);
        Assert.Same(first, stillActive);
        Assert.Equal(1, store.RetainedCount);
    }

    [Fact]
    public async Task CleanupTimerRemovesInactiveConversations()
    {
        var time = new FakeTimeProvider();
        using var client = new FakeChatClient();
        var agent = new ChatClientAgent(client, instructions: "Test", name: "Test");
        using var store = CreateStore(time);

        var expired = await store.GetOrCreateAsync("conversation", agent);
        time.Advance(TimeSpan.FromMinutes(11));

        Assert.Equal(0, store.RetainedCount);
        Assert.NotSame(expired, await store.GetOrCreateAsync("conversation", agent));
    }

    [Fact]
    public async Task ResetImmediatelyRemovesAConversation()
    {
        var time = new FakeTimeProvider();
        using var client = new FakeChatClient();
        var agent = new ChatClientAgent(client, instructions: "Test", name: "Test");
        using var store = CreateStore(time);

        var reset = await store.GetOrCreateAsync("conversation", agent);
        store.Reset("conversation");

        Assert.Equal(0, store.RetainedCount);
        Assert.NotSame(reset, await store.GetOrCreateAsync("conversation", agent));
    }

    private static ConversationStore CreateStore(TimeProvider timeProvider) =>
        new(
            Options.Create(new ConversationStoreOptions
            {
                InactiveTtl = TimeSpan.FromMinutes(10),
                CleanupInterval = TimeSpan.FromMinutes(1),
            }),
            timeProvider);

    private sealed class FakeChatClient : IChatClient
    {
        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}