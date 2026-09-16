using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using TheSeries.AiService.Application;
using Xunit;

namespace TheSeries.AiService.Tests;

/// <summary>
/// Covers what the flow UI can replay: every LLM round-trip must keep its own response (not just the
/// last one), and each tool call must stay identifiable when the same tool is called several times.
/// </summary>
public sealed class FlowCaptureTests
{
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
        }

        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
