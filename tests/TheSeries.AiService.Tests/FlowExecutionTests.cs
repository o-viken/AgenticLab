using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using TheSeries.AiService.Application;
using Xunit;

namespace TheSeries.AiService.Tests;

public sealed class FlowExecutionTests
{
    [Fact]
    public async Task EveryBoundaryStopsRealExecutionAndNotifiesWhileAdvanceIsBlocked()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var session = new FlowSession("test", false, 0);
        session.SetBreakpoints(FlowSession.BreakpointKinds);
        using var model = new FakeModel();
        var toolCalls = 0;
        var tool = AIFunctionFactory.Create(() => Interlocked.Increment(ref toolCalls), "Count");
        using var pipeline = model.AsBuilder()
            .UseFunctionInvocation(configure: client => client.FunctionInvoker = FlowExecutionScope.InvokeFunctionAsync)
            .Use(inner => new CapturingChatClient(inner))
            .Build();
        var notices = Channel.CreateUnbounded<BreakpointNotice>();
        var run = ConsumeAsync(pipeline, tool, session, notices.Writer, timeout.Token);

        async Task ReleaseAsync(string kind, int expectedRequests, int expectedTools, string? expectedTool = null)
        {
            var notice = await notices.Reader.ReadAsync(timeout.Token);
            Assert.True(notice.Paused);
            Assert.Equal(kind, notice.Kind);
            Assert.Equal(expectedTool, notice.Tool);
            Assert.Equal(expectedRequests, model.Requests);
            Assert.Equal(expectedTools, Volatile.Read(ref toolCalls));
            Assert.False(run.IsCompleted);
            Assert.True(session.ReleaseBreakpoint(notice.Id, false));
            Assert.False((await notices.Reader.ReadAsync(timeout.Token)).Paused);
        }

        await ReleaseAsync("before-model", 0, 0);
        await ReleaseAsync("after-model", 1, 0);
        await ReleaseAsync("before-tool", 1, 0, "Count");
        await ReleaseAsync("after-tool", 1, 1, "Count");
        await ReleaseAsync("before-tool", 1, 1, "Count");
        await ReleaseAsync("after-tool", 1, 2, "Count");
        await ReleaseAsync("before-model", 1, 2);
        await ReleaseAsync("after-model", 2, 2);
        var updates = await run.WaitAsync(timeout.Token);
        Assert.Contains(updates.SelectMany(update => update.Contents), content => content is TextContent { Text: "Done" });
        Assert.Equal(2, model.Requests);
        Assert.Equal(2, toolCalls);
    }

    [Theory]
    [InlineData("before-model")]
    [InlineData("after-model")]
    [InlineData("before-tool")]
    [InlineData("after-tool")]
    public async Task StoppingAtBreakpointCompletesThePendingAdvance(string kind)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var session = new FlowSession("test", false, 0);
        session.SetBreakpoints([kind]);
        using var model = new FakeModel();
        using var pipeline = model.AsBuilder()
            .UseFunctionInvocation(configure: client => client.FunctionInvoker = FlowExecutionScope.InvokeFunctionAsync)
            .Use(inner => new CapturingChatClient(inner)).Build();
        var notices = Channel.CreateUnbounded<BreakpointNotice>();
        var run = ConsumeAsync(pipeline, AIFunctionFactory.Create(() => 1, "Count"), session, notices.Writer, timeout.Token);
        await notices.Reader.ReadAsync(timeout.Token);
        session.Stop();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(timeout.Token));
        Assert.Equal(kind == "before-model" ? 0 : 1, model.Requests);
    }

    [Fact]
    public async Task RunsWithNoBreakpointsDoNotProduceControlEvents()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var session = new FlowSession("test", false, 0);
        using var model = new FakeModel();
        using var pipeline = model.AsBuilder()
            .UseFunctionInvocation(configure: client => client.FunctionInvoker = FlowExecutionScope.InvokeFunctionAsync)
            .Use(inner => new CapturingChatClient(inner)).Build();
        var notices = Channel.CreateUnbounded<BreakpointNotice>();
        await ConsumeAsync(pipeline, AIFunctionFactory.Create(() => 1, "Count"), session, notices.Writer, timeout.Token);
        Assert.False(notices.Reader.TryRead(out _));
        Assert.Equal(2, model.Requests);
    }

    private static async Task<List<ChatResponseUpdate>> ConsumeAsync(
        IChatClient pipeline, AIFunction tool, FlowSession session,
        ChannelWriter<BreakpointNotice> notices, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, session.StopToken);
        using var capture = FlowCaptureScope.Begin();
        using var scope = new FlowExecutionScope(session);
        await using var updates = pipeline.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "Count twice")], new ChatOptions { Tools = [tool] }, linked.Token)
            .GetAsyncEnumerator(linked.Token);
        var result = new List<ChatResponseUpdate>();
        while (true)
        {
            scope.Activate();
            capture.Activate();
            await foreach (var notice in scope.AdvanceAsync(updates, linked.Token))
            {
                await notices.WriteAsync(notice, token);
            }
            if (!scope.HasUpdate)
            {
                return result;
            }
            result.Add(updates.Current);
        }
    }

    private sealed class FakeModel : IChatClient
    {
        public int Requests { get; private set; }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests++;
            await Task.Yield();
            if (Requests == 1)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, (string?)null)
                {
                    Contents = [new FunctionCallContent("one", "Count"), new FunctionCallContent("two", "Count")],
                    FinishReason = ChatFinishReason.ToolCalls,
                };
            }
            else
            {
                Assert.Equal(2, messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Count());
                yield return new ChatResponseUpdate(ChatRole.Assistant, "Done") { FinishReason = ChatFinishReason.Stop };
            }
        }

        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}