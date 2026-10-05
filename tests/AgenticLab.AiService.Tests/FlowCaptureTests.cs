using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using AgenticLab.AiService.Application.Flow;
using Xunit;

namespace AgenticLab.AiService.Tests;

/// <summary>
/// Covers what the flow UI can replay: every LLM round-trip must keep its own response (not just the
/// last one), and each tool call must stay identifiable when the same tool is called several times.
/// </summary>
public sealed class FlowCaptureTests
{
    [Fact]
    public async Task UsageContributionsMatchSdkAggregationAndPreserveLargeCounts()
    {
        using var model = new UsageModel([
            new() { InputTokenCount = 3000000000, CachedInputTokenCount = 2000000000 },
            new() { InputTokenCount = 100, OutputTokenCount = 0, TotalTokenCount = 3000000100, CachedInputTokenCount = 50 },
        ]);
        using var client = new CapturingChatClient(model);
        using var capture = FlowCaptureScope.Begin();
        var response = await client.GetStreamingResponseAsync("Test").ToChatResponseAsync();
        var turn = Assert.Single(capture.Turns);
        Assert.Equal(new FlowTokenUsage(3000000100, 0, 3000000100, 2000000050), turn.Usage);
        Assert.Equal(response.Usage!.InputTokenCount, turn.Usage!.InputTokenCount);
        Assert.Equal(response.Usage.CachedInputTokenCount, turn.Usage.CachedInputTokenCount);
    }

    [Fact]
    public async Task MissingUsageIsNotZeroAndUncapturedStreamsStillForwardUsage()
    {
        using var model = new UsageModel([]);
        using var client = new CapturingChatClient(model);
        using (var capture = FlowCaptureScope.Begin())
        {
            await client.GetStreamingResponseAsync("Test").ToChatResponseAsync();
            Assert.Null(Assert.Single(capture.Turns).Usage);
        }
        using var reportingModel = new UsageModel([new() { InputTokenCount = 0 }]);
        using var reportingClient = new CapturingChatClient(reportingModel);
        var response = await reportingClient.GetStreamingResponseAsync("Test").ToChatResponseAsync();
        Assert.Equal(0, response.Usage!.InputTokenCount);
        Assert.Null(response.Usage.CachedInputTokenCount);
        Assert.Null(FlowCaptureScope.Current);
    }

    [Fact]
    public async Task CancellationDoesNotPublishAnIncompleteResponseOrUsage()
    {
        using var model = new UsageModel([new() { InputTokenCount = 100 }], cancel: true);
        using var client = new CapturingChatClient(model);
        using var capture = FlowCaptureScope.Begin();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await client.GetStreamingResponseAsync("Test").ToChatResponseAsync());
        var turn = Assert.Single(capture.Turns);
        Assert.Null(turn.ResponseData);
        Assert.Null(turn.Usage);
    }

    private sealed class UsageModel(IReadOnlyList<UsageDetails> reports, bool cancel = false) : IChatClient
    {
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Text");
            foreach (var report in reports)
            {
                yield return new ChatResponseUpdate { Contents = [new UsageContent(report)] };
            }
            if (cancel)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    /// <summary>Structured call metadata survives serialization and snapshots mutable arguments.</summary>
    [Theory]
    [InlineData("research")]
    [InlineData("poet")]
    public void StructuredCall_PreservesTargetAndQuestion(string agentName)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["agentName"] = agentName,
            ["question"] = "First line\nSecond line: \"quoted\"",
        };
        var snapshot = FlowToolCall.Capture(new FunctionCallContent("call-1", "DelegateToAgent", arguments));
        arguments["agentName"] = "changed";
        var captured = new FlowEvent(1, "tool-call", "unchanged", CallId: "call-1", ToolCall: snapshot);
        var restored = System.Text.Json.JsonSerializer.Deserialize<FlowEvent>(
            System.Text.Json.JsonSerializer.Serialize(captured))!;

        Assert.Equal("call-1", restored.CallId);
        Assert.Equal("DelegateToAgent", restored.ToolCall!.Name);
        Assert.Equal(agentName, restored.ToolCall.Arguments.GetProperty("agentName").GetString());
        Assert.Equal("First line\nSecond line: \"quoted\"", restored.ToolCall.Arguments.GetProperty("question").GetString());
    }

    [Fact]
    public async Task EveryRoundTripKeepsItsOwnResponse()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var capture = await RunAsync(timeout.Token);
        var turns = capture.Turns;

        Assert.Equal(2, turns.Count);
        Assert.All(turns, turn => Assert.False(string.IsNullOrWhiteSpace(turn.ResponseData)));
        Assert.All(turns, turn => Assert.False(string.IsNullOrWhiteSpace(turn.ResponseSummary)));

        // The round-trip that asked for the tools must survive the one that produced the answer.
        Assert.Contains("Search", turns[0].ResponseData!);
        Assert.DoesNotContain("Done", turns[0].ResponseData!);
        Assert.Contains("Done", turns[1].ResponseData!);
        Assert.Equal(new FlowTokenUsage(100, 10, 110, 80), turns[0].Usage);
        Assert.Equal(new FlowTokenUsage(200, 20, 220, 160), turns[1].Usage);
    }

    [Fact]
    public async Task ResponseSummaryNamesTheRequestedToolCalls()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var capture = await RunAsync(timeout.Token);
        var turns = capture.Turns;

        Assert.Equal("requested 2 tool calls: Search, Search", turns[0].ResponseSummary);
        Assert.Contains("text: Done", turns[1].ResponseSummary);
    }

    [Fact]
    public async Task RepeatedCallsToTheSameToolStayDistinguishableByCallId()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var capture = await RunAsync(timeout.Token);

        // The second request carries both results back to the model, each tagged with the call it answers.
        var request = capture.Turns[1].RequestData;
        Assert.Contains("call-1", request);
        Assert.Contains("call-2", request);
        Assert.Contains("result for ada", request);
        Assert.Contains("result for lovelace", request);
    }

    private static async Task<FlowCaptureScope> RunAsync(CancellationToken token)
    {
        using var model = new ToolThenAnswerModel();
        using var pipeline = model.AsBuilder()
            .UseFunctionInvocation()
            .Use(inner => new CapturingChatClient(inner))
            .Build();
        var tool = AIFunctionFactory.Create((string query) => $"result for {query}", "Search");

        var capture = FlowCaptureScope.Begin();
        await using var updates = pipeline
            .GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Who?")], new ChatOptions { Tools = [tool] }, token)
            .GetAsyncEnumerator(token);
        while (true)
        {
            // The scope lives in an AsyncLocal that the iterator resets on each yield.
            capture.Activate();
            if (!await updates.MoveNextAsync())
            {
                return capture;
            }
        }
    }

    private sealed class ToolThenAnswerModel : IChatClient
    {
        private int _requests;

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _requests++;
            await Task.Yield();
            if (_requests == 1)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, (string?)null)
                {
                    Contents =
                    [
                        new FunctionCallContent("call-1", "Search", new Dictionary<string, object?> { ["query"] = "ada" }),
                        new FunctionCallContent("call-2", "Search", new Dictionary<string, object?> { ["query"] = "lovelace" }),
                    ],
                    FinishReason = ChatFinishReason.ToolCalls,
                };
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "Done") { FinishReason = ChatFinishReason.Stop };
            }

            yield return new ChatResponseUpdate
            {
                Contents = [new UsageContent(new UsageDetails
                {
                    InputTokenCount = _requests * 100,
                    OutputTokenCount = _requests * 10,
                    TotalTokenCount = _requests * 110,
                    CachedInputTokenCount = _requests * 80,
                })],
            };
        }

        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
