using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using TheSeries.Web;
using TheSeries.Web.Flow;
using Xunit;

namespace TheSeries.Web.Tests;

/// <summary>
/// Protects what the Execution explorer claims about a run: stages must group into the round-trip they
/// belong to, read in causal order, and a partial run must never be reported as a completed one.
/// </summary>
public sealed class ExecutionReplayTests
{
    /// <summary>Streaming, replay caches, numbering and telemetry remain consistent when archives are evicted.</summary>
    [Fact]
    public async Task Retention_ControllerEvictsWithoutChangingCurrentCaptureOrServerMemory()
    {
        var totals = new Dictionary<string, long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name is ReplayHistory.MeterName or FlowRunController.MeterName)
            {
                current.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            Assert.Equal(0, tags.Length);
            totals[instrument.Name] = totals.GetValueOrDefault(instrument.Name) + value;
        });
        listener.Start();
        using var handler = new ReplayHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        var catalog = new ConceptCatalog(new ReplayEnvironment(), NullLogger<ConceptCatalog>.Instance);
        var view = new FlowViewState(catalog);
        using var run = new FlowRunController(new AiServiceClient(http), view, new() { MaxArchivedExchanges = 1 });
        Assert.Equal(1, totals.GetValueOrDefault("flow.active_pages"));
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        run.Changed += () =>
        {
            _ = run.Projections.Exchanges;
            _ = run.Replay.DisplayContext;
            _ = run.Replay.DisplayPromptSignature;
            if (!run.Running)
            {
                finished.TrySetResult();
            }
            return Task.CompletedTask;
        };
        async Task SendAsync(string message)
        {
            finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
            view.Message = message;
            await run.SendAsync();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        await SendAsync("first");
        var firstId = Assert.Single(run.Projections.Exchanges).Id;
        await SendAsync("second");
        view.Cursor.SelectStage(firstId, 2);
        Assert.True(run.Replay.Replaying);
        _ = run.Replay.DisplayPromptSignature;
        _ = run.Replay.DisplayContext;
        await SendAsync("third");

        Assert.False(run.Replay.Replaying);
        Assert.Equal([2, 3], run.Projections.Exchanges.Select(exchange => exchange.Number));
        Assert.DoesNotContain(run.Projections.Exchanges, exchange => exchange.Id == firstId);
        Assert.Equal("second", Assert.Single(run.Turns).Message);
        Assert.Equal(1, run.EvictedExchanges);
        Assert.Equal(2, run.Events.Count);
        Assert.Equal(run.Replay.DisplayPromptSignature.CurrentChars, run.Replay.DisplayContext.Chars);
        Assert.Equal(0, handler.Resets);
        Assert.Equal(1, totals["replay.archived.exchanges"]);
        Assert.Equal(2, totals["replay.archived.events"]);
        Assert.Equal(ReplayHistory.EstimatePayloadBytes(run.Turns[0]), totals["replay.archived.payload_bytes"]);
        Assert.Equal(4, totals["flow.retained_events"]);
        Assert.True(totals["flow.retained_payload_bytes"] > ReplayHistory.EstimatePayloadBytes(run.Turns[0]));

        await run.NewConversationAsync();
        Assert.Equal(1, handler.Resets);
        Assert.Empty(run.Projections.Exchanges);
        Assert.Empty(run.Turns);
        Assert.False(run.Replay.DisplayPromptSignature.HasCurrent);
        Assert.Equal(0, run.EvictedExchanges);
        Assert.Equal(0, totals["replay.archived.payload_bytes"]);
        await SendAsync("new first");
        Assert.Equal(1, Assert.Single(run.Projections.Exchanges).Number);
        await SendAsync("new second");
        run.Dispose();
        run.Dispose();
        Assert.Equal(0, totals["flow.active_pages"]);
        Assert.Equal(0, totals["flow.retained_events"]);
        Assert.Equal(0, totals["flow.retained_payload_bytes"]);
        Assert.Equal(0, totals["replay.archived.exchanges"]);
        Assert.Equal(0, totals["replay.archived.events"]);
        Assert.Equal(0, totals["replay.archived.payload_bytes"]);
        Assert.Equal(1, totals["replay.evicted.exchanges"]);
    }

    /// <summary>Long synthetic conversations plateau at the configured archive count.</summary>
    [Fact]
    public void Retention_LongConversationReachesAPlateau()
    {
        using var history = new ReplayHistory(new() { MaxArchivedExchanges = 5 });
        var turn = new ConversationTurn("test", "hello", "Chat", "reply", null,
            [Event(1, "llm-request", 1) with { Data = new string('x', 100_000) }]);
        for (var index = 0; index < 200; index++)
        {
            history.Add(turn);
            Assert.True(history.Turns.Count <= 5);
            Assert.True(history.PayloadBytes <= ReplayHistory.EstimatePayloadBytes(turn) * 5);
        }
        Assert.Equal(195, history.EvictedExchanges);
        Assert.Equal(5, history.EventCount);
    }

    /// <summary>Zero limits disable archives and invalid limits cannot silently disable eviction.</summary>
    [Fact]
    public void Retention_ValidatesLimitsAndAllowsZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReplayHistory(new() { MaxArchivedExchanges = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReplayHistory(new() { MaxArchivedPayloadBytes = -1 }));
        using var history = new ReplayHistory(new() { MaxArchivedExchanges = 0 });
        history.Add(new("test", "hello", "Chat", "reply", null, []));
        Assert.Empty(history.Turns);
        Assert.Equal(1, history.EvictedExchanges);
    }

    private sealed class ReplayHandler : HttpMessageHandler
    {
        internal int Resets { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/chat/reset")
            {
                Resets++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            Assert.Equal("/chat/stream", request.RequestUri.AbsolutePath);
            FlowEvent[] events =
            [
                Event(1, "llm-request", 1) with
                { Data = """{"instructions":"rules","messages":[{"role":"user","contents":[{"text":"hello"}]}]}""" },
                Event(2, "final", 1) with { Detail = "answer", Data = "answer" },
            ];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Concat(events.Select(stage => $"event: flow\ndata: {JsonSerializer.Serialize(stage)}\n\n"))),
            });
        }
    }

    private sealed class ReplayEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "TheSeries.Web.Tests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>Eviction removes whole oldest exchanges, leaving retained captures intact.</summary>
    [Fact]
    public void Retention_KeepsNewestWholeExchanges()
    {
        using var history = new ReplayHistory(new() { MaxArchivedExchanges = 2 });
        var first = new ConversationTurn("one", "first", "Chat", "reply", null, [Event(1, "final", 1)]);
        var second = first with { Id = "two" };
        var third = first with { Id = "three" };
        history.Add(first);
        history.Add(second);
        history.Add(third);

        Assert.Equal(["two", "three"], history.Turns.Select(turn => turn.Id));
        Assert.Same(second, history.Turns[0]);
        Assert.Same(third, history.Turns[1]);
        Assert.Equal(1, history.EvictedExchanges);
        Assert.Equal(2, history.EventCount);
        Assert.Equal(ReplayHistory.EstimatePayloadBytes(second) + ReplayHistory.EstimatePayloadBytes(third), history.PayloadBytes);
    }

    /// <summary>The byte budget includes structured arguments and permits an exact-boundary capture.</summary>
    [Fact]
    public void Retention_EnforcesPayloadBudgetAndDropsOversizedArchive()
    {
        var turn = new ConversationTurn("one", "hello", "Chat", "reply", null,
            [Delegation(1, "call", "research", new string('x', 100))]);
        var bytes = ReplayHistory.EstimatePayloadBytes(turn);
        Assert.True(bytes > 200);
        using var history = new ReplayHistory(new() { MaxArchivedPayloadBytes = bytes });
        history.Add(turn);
        Assert.Single(history.Turns);
        history.Add(turn with { Id = "two" });
        Assert.Equal("two", Assert.Single(history.Turns).Id);
        history.Add(turn with { Message = new string('x', (int)bytes) });
        Assert.Empty(history.Turns);
        Assert.Equal(0, history.PayloadBytes);
        Assert.Equal(0, history.EventCount);
        Assert.Equal(3, history.EvictedExchanges);
    }

    /// <summary>New conversation and disposal release retained data and reset the local eviction count.</summary>
    [Fact]
    public void Retention_ClearAndDisposeReleaseArchives()
    {
        using var history = new ReplayHistory(new() { MaxArchivedExchanges = 1 });
        var turn = new ConversationTurn("one", "hello", "Chat", "reply", null, []);
        history.Add(turn);
        history.Add(turn with { Id = "two" });
        history.Clear();
        Assert.Empty(history.Turns);
        Assert.Equal(0, history.EvictedExchanges);
        Assert.Equal(0, history.PayloadBytes);
        history.Add(turn);
        history.Dispose();
        Assert.Empty(history.Turns);
        Assert.Equal(0, history.PayloadBytes);
        Assert.Throws<ObjectDisposedException>(() => history.Add(turn));
    }

    /// <summary>Each result answers its own call, including repeated and interleaved targets.</summary>
    [Fact]
    public void A2A_PairsCallsAndDoesNotRevealFutureResults()
    {
        A2AChip[] roster = [new("research", "Research"), new("poet", "Poetry")];
        var first = Delegation(3, "first", " RESEARCH ", "First\nquestion");
        var second = Delegation(5, "second", "poet", "Write a poem");
        var repeated = Delegation(6, "third", "research", "Another question");
        var result = Event(7, "tool-result", 1, "first") with { Data = "First answer" };
        var exchange = ExecutionReplayBuilder.Build([new ConversationTurn("a", "hello", "Orchestrator", "done", null,
            [Event(2, "llm-request", 1), first, Event(4, "llm-response", 1), second, repeated, result], A2AAgents: roster)], -1)[0];

        var atCall = A2AFlowBuilder.Build(exchange.A2AAgents!, ExecutionReplayBuilder.PrefixThrough(exchange, 3), false);
        Assert.Equal("research", atCall.ActiveAgent);
        Assert.Equal("send", atCall.Direction);
        Assert.Equal("First\nquestion", atCall.Agents[0].Question);
        Assert.Null(atCall.Agents[0].Result);
        Assert.Equal("Available", atCall.Agents[1].Status);
        var atResult = A2AFlowBuilder.Build(exchange.A2AAgents!, ExecutionReplayBuilder.PrefixThrough(exchange, 7), false);
        Assert.Equal("research", atResult.ActiveAgent);
        Assert.Equal("recv", atResult.Direction);
        Assert.Equal("First\nquestion", atResult.Agents[0].Question);
        Assert.Equal("First answer", atResult.Agents[0].Result);
        Assert.Null(atResult.Agents[1].Result);
        Assert.Equal("poet", exchange.A2AAgents![1].Name);
    }

    /// <summary>Unknown or legacy targets do not activate a discovered agent; stopped calls stay incomplete.</summary>
    [Fact]
    public void A2A_DegradesWithoutInventingActivity()
    {
        A2AChip[] roster = [new("research", "Research")];
        var unknown = A2AFlowBuilder.Build(roster, [Delegation(1, "a", "unknown", "question")], false);
        Assert.Null(unknown.ActiveAgent);
        Assert.Equal("Available", unknown.Agents[0].Status);
        Assert.Null(A2AFlowBuilder.Build(roster, [Event(1, "tool-call", 1)], false).ActiveAgent);
        var malformed = Delegation(1, "a", "research", "question") with
        { ToolCall = new("DelegateToAgent", System.Text.Json.JsonSerializer.SerializeToElement("invalid")) };
        Assert.Null(A2AFlowBuilder.Build(roster, [malformed], false).ActiveAgent);
        var stopped = A2AFlowBuilder.Build(roster, [Delegation(1, "a", "research", "question")], true);
        Assert.Equal("No result captured", stopped.Agents[0].Status);
    }

    private static FlowEvent Delegation(int sequence, string callId, string agentName, string question) =>
        new(sequence, "tool-call", "LLM → Tool: DelegateToAgent", null, 1, CallId: callId,
            ToolCall: new("DelegateToAgent", System.Text.Json.JsonSerializer.SerializeToElement(new { agentName, question })));

    /// <summary>Unrelated functions and unmatched results never borrow a known agent's identity.</summary>
    [Fact]
    public void A2A_RequiresADelegationCallAndMatchingId()
    {
        A2AChip[] roster = [new("research", "Research")];
        var call = Delegation(1, "a", "research", "question");
        var unrelated = call with { ToolCall = call.ToolCall! with { Name = "Calculate" } };
        Assert.Null(A2AFlowBuilder.Build(roster, [unrelated], false).ActiveAgent);
        var mismatch = Event(2, "tool-result", 1, "other") with { Data = "Unrelated result" };
        var display = A2AFlowBuilder.Build(roster, [call, mismatch], false);
        Assert.Null(display.ActiveAgent);
        Assert.Null(display.Agents[0].Result);
        Assert.Equal("Delegation requested", display.Agents[0].Status);
    }

    /// <summary>Old payloads remain readable and metadata does not change prompt/context accounting.</summary>
    [Fact]
    public void A2A_MetadataIsOptionalAndExcludedFromPromptSize()
    {
        var legacy = System.Text.Json.JsonSerializer.Deserialize<FlowEvent>(
            """{"Sequence":1,"Kind":"tool-call","Label":"Tool call","Detail":null}""")!;
        Assert.Null(legacy.ToolCall);
        var call = Delegation(3, "a", "research", "question");
        var request = Event(2, "llm-request", 1) with
        { Data = """{"instructions":"rules","messages":[{"role":"user","contents":[{"text":"hello"}]}]}""" };
        var before = PromptSignatureBuilder.Build([("test", (IReadOnlyList<FlowEvent>)[request, call with { ToolCall = null }])]);
        var after = PromptSignatureBuilder.Build([("test", (IReadOnlyList<FlowEvent>)[request, call])]);
        Assert.Equal(before.CurrentChars, after.CurrentChars);
    }

    /// <summary>A round-trip reads request → response → the calls it asked for → their results.</summary>
    [Fact]
    public void Turn_OrdersStagesCausally()
    {
        var exchange = Build(
            Event(1, "received", turn: 0),
            // The capture emits tool calls while the response is still streaming.
            Event(2, "llm-request", turn: 1),
            Event(3, "tool-call", turn: 1, callId: "a"),
            Event(4, "llm-response", turn: 1),
            Event(5, "tool-result", turn: 1, callId: "a"),
            Event(6, "final", turn: 1));

        var turn = Assert.Single(exchange.Turns);
        Assert.Equal(1, turn.Number);
        Assert.Equal(
            ["llm-request", "llm-response", "tool-call", "tool-result"],
            turn.Stages.Select(s => s.Kind).ToArray());
    }

    /// <summary>Intake and delivery belong to the exchange, not to the round-trip they land in.</summary>
    [Fact]
    public void IntakeAndDelivery_SitOutsideTheTurns()
    {
        var exchange = Build(
            Event(1, "received", turn: 0),
            Event(2, "llm-request", turn: 1),
            Event(3, "llm-response", turn: 1),
            Event(4, "final", turn: 1));

        Assert.Equal("received", Assert.Single(exchange.Intake).Kind);
        Assert.Equal("final", Assert.Single(exchange.Outcome).Kind);
        Assert.DoesNotContain(exchange.Turns.SelectMany(t => t.Stages), s => s.Kind is "received" or "final");
    }

    /// <summary>Each round-trip keeps its own response, so a multi-turn run is fully inspectable.</summary>
    [Fact]
    public void MultipleTurns_EachKeepItsOwnStages()
    {
        var exchange = Build(
            Event(1, "received", turn: 0),
            Event(2, "llm-request", turn: 1),
            Event(3, "llm-response", turn: 1),
            Event(4, "tool-result", turn: 1, callId: "a"),
            Event(5, "llm-request", turn: 2),
            Event(6, "llm-response", turn: 2),
            Event(7, "final", turn: 2));

        Assert.Equal([1, 2], exchange.Turns.Select(t => t.Number).ToArray());
        Assert.Equal(2, exchange.Turns.Count(t => t.Stages.Any(s => s.Kind == "llm-response")));
    }

    /// <summary>Stepping walks intake, then each round-trip in order, then delivery.</summary>
    [Fact]
    public void Stages_FlattenInDisplayOrder()
    {
        var exchange = Build(
            Event(1, "received", turn: 0),
            Event(2, "llm-request", turn: 1),
            Event(3, "llm-response", turn: 1),
            Event(4, "llm-request", turn: 2),
            Event(5, "llm-response", turn: 2),
            Event(6, "final", turn: 2));

        Assert.Equal([1, 2, 3, 4, 5, 6], exchange.Stages.Select(s => s.Sequence).ToArray());
    }

    /// <summary>A run that never delivered an answer is reported as stopped, not completed.</summary>
    [Fact]
    public void Status_DistinguishesPartialRuns()
    {
        Assert.Equal(ExchangeStatus.Completed, Build(Event(1, "final", turn: 1)).Status);
        Assert.Equal(ExchangeStatus.Stopped, Build(Event(1, "llm-request", turn: 1)).Status);
        Assert.Equal(ExchangeStatus.Running, BuildLive(Event(1, "llm-request", turn: 1)).Status);
        Assert.Equal(ExchangeStatus.Failed, ExecutionReplayBuilder.Build(
            [new ConversationTurn("x", "hi", "Agent", string.Empty, "boom", [Event(1, "error", turn: 0)])], -1)[0].Status);
    }

    /// <summary>An exchange with nothing captured yet is still listed, so a fresh send is visible.</summary>
    [Fact]
    public void ExchangeWithNoStages_IsStillListed()
    {
        var exchange = BuildLive();

        Assert.Empty(exchange.Stages);
        Assert.Empty(exchange.Turns);
        Assert.Equal(ExchangeStatus.Running, exchange.Status);
    }

    /// <summary>The recorded agent and vendor are the ones the run used, not the current selection.</summary>
    [Fact]
    public void Exchange_KeepsTheSettingsItRanWith()
    {
        var exchange = ExecutionReplayBuilder.Build(
            [new ConversationTurn("x", "hi", "Coder", "done", null, [], "claude-code", "C:/repo")], -1)[0];

        Assert.Equal("Coder", exchange.Agent);
        Assert.Equal("claude-code", exchange.Vendor);
        Assert.Equal("C:/repo", exchange.Workspace);
    }

    /// <summary>
    /// Every round-trip now emits a response, so the one that asked for a tool must say so rather than
    /// being labelled as text.
    /// </summary>
    [Theory]
    [InlineData("{\n  \"text\": null,\n  \"toolCalls\": [\n    { \"name\": \"SearchWiki\" }\n  ]\n}", "\U0001F527 function call")]
    [InlineData("{\n  \"text\": null,\n  \"toolCalls\": [\n    { \"name\": \"A\" },\n    { \"name\": \"B\" }\n  ]\n}", "\U0001F527 2 function calls")]
    [InlineData("{\n  \"text\": \"Here is the answer\",\n  \"toolCalls\": null\n}", "\U0001F4AC text")]
    [InlineData("{\n  \"text\": \"Looking that up\",\n  \"toolCalls\": [\n    { \"name\": \"SearchWiki\" }\n  ]\n}", "\U0001F527 function call")]
    public void ResponseHint_NamesWhatTheResponseActuallyCarried(string data, string expected)
    {
        var response = new FlowEvent(1, "llm-response", "LLM → Harness", null, 1, data);

        Assert.Equal(expected, FlowEventMapping.ResponseHintFor(response));
    }

    /// <summary>A captured response holding neither text nor calls must not claim either.</summary>
    [Fact]
    public void ResponseHint_IsAbsentForAnEmptyResponse()
    {
        var response = new FlowEvent(1, "llm-response", "LLM → Harness", null, 1, "{\n  \"text\": null,\n  \"toolCalls\": null\n}");

        Assert.Null(FlowEventMapping.ResponseHintFor(response));
    }

    /// <summary>Replay Context follows displayed order, not capture sequence, and never leaks later results.</summary>
    [Fact]
    public void Context_UsesCausalPrefixAndCapturedSize()
    {
        const string request = """{"instructions":"rules","messages":[{"role":"user","contents":[{"text":"question"}]}]}""";
        var exchanges = ExecutionReplayBuilder.Build(
            [new ConversationTurn("x", "Actual question", "Agent", "Future answer", null,
                [Event(1, "received", 0) with { Data = "Agent: Agent" },
                 Event(2, "llm-request", 1) with { Data = request },
                 Event(3, "tool-call", 1),
                 Event(4, "llm-response", 1) with { Data = """{"text":"Looking up"}""" },
                 Event(5, "tool-result", 1) with { Data = "Future result" },
                 Event(6, "final", 1) with { Data = "Future answer" }])], -1);

        var intake = ExecutionReplayBuilder.ContextAt(exchanges, "x", 1);
        Assert.Equal("Actual question", Assert.Single(intake.Current).Preview);
        Assert.Null(intake.Chars);
        var sent = ExecutionReplayBuilder.ContextAt(exchanges, "x", 2);
        Assert.Equal(13, sent.Chars);
        Assert.Single(sent.Current);
        var call = ExecutionReplayBuilder.ContextAt(exchanges, "x", 3);
        Assert.Equal(["User message", "Assistant message"], call.Current.Select(entry => entry.Label).ToArray());
        Assert.DoesNotContain(call.Current, entry => entry.Preview.Contains("Future"));
        var response = ExecutionReplayBuilder.ContextAt(exchanges, "x", 4);
        Assert.Equal(response.Current.ToArray(), call.Current.ToArray());
        Assert.Equal(response.Chars, call.Chars);
        var result = ExecutionReplayBuilder.ContextAt(exchanges, "x", 5);
        Assert.Equal("Future result", result.Current[^1].Preview);
        Assert.Equal(call.Chars, result.Chars);
        var final = ExecutionReplayBuilder.ContextAt(exchanges, "x", 6);
        Assert.Equal("Future answer", final.Current[^1].Preview);
        Assert.Equal("agent", final.Current[^1].Source);
    }

    /// <summary>Only exchanges before the selected one contribute history, including empty and failed runs.</summary>
    [Fact]
    public void Context_ExcludesLaterExchangesAndHandlesMissingCapture()
    {
        var exchanges = ExecutionReplayBuilder.Build(
            [new ConversationTurn("first", "First question", "Agent", "First answer", null, [Event(1, "final", 1)]),
             new ConversationTurn("failed", "Failed question", "Agent", "", "Failure", [Event(1, "error", 0)]),
             new ConversationTurn("empty", "Empty question", "Agent", "", null, []),
             new ConversationTurn("later", "Later question", "Agent", "Later answer", null, [Event(1, "final", 1)])], -1);

        var first = ExecutionReplayBuilder.ContextAt(exchanges, "first", 1);
        Assert.Empty(first.History);
        Assert.DoesNotContain(first.Current, entry => entry.Preview.Contains("Later"));
        var failed = ExecutionReplayBuilder.ContextAt(exchanges, "failed", 1);
        Assert.Equal(["First question", "First answer"], failed.History.Select(entry => entry.Preview).ToArray());
        Assert.All(failed.History, entry => Assert.True(entry.History));
        Assert.Equal("agent", failed.History[^1].Source);
        var empty = ExecutionReplayBuilder.ContextAt(exchanges, "empty", null);
        Assert.Empty(empty.Current);
        Assert.Equal("Failure", empty.History[^1].Preview);
        Assert.Null(empty.Chars);
        Assert.Equal(0, empty.BarWidth);
        Assert.Same(ContextSnapshot.Empty, ExecutionReplayBuilder.ContextAt(exchanges, "missing", 1));
        Assert.Empty(ExecutionReplayBuilder.ContextAt(exchanges, "first", 999).Current);
    }

    /// <summary>Signature playback scopes both comparison and growth to the selected causal prefix.</summary>
    [Fact]
    public void Signature_ExcludesFutureRequestsResponsesAndExchanges()
    {
        const string request = """{"instructions":"rules","messages":[{"role":"user","contents":[{"text":"question"}]}]}""";
        const string laterRequest = """{"instructions":"rules","messages":[{"role":"user","contents":[{"text":"question"}]},{"role":"tool","contents":[{"result":"LATER_RESULT"}]}]}""";
        var earlierEvents = new[] { Event(1, "llm-request", 1) with { Data = request }, Event(2, "final", 1) with { Data = "Earlier answer" } };
        var selectedEvents = new[] {
            Event(1, "received", 0),
            Event(2, "llm-request", 1) with { Data = request },
            Event(3, "tool-call", 1),
            Event(4, "llm-response", 1) with { Data = """{"text":"Looking up"}""" },
            Event(5, "tool-result", 1) with { Data = "LATER_RESULT" },
            Event(6, "llm-request", 2) with { Data = laterRequest },
            Event(7, "final", 2) with { Data = "LATER_ANSWER" }
        };
        var exchanges = ExecutionReplayBuilder.Build(
            [new("first", "Earlier question", "Agent", "Earlier answer", null, earlierEvents),
             new("selected", "Selected question", "Agent", "LATER_ANSWER", null, selectedEvents),
             new("future", "FUTURE_EXCHANGE", "Agent", "Future answer", null, earlierEvents)], -1);

        var sent = ExecutionReplayBuilder.SignatureAt(exchanges, "selected", 2);
        Assert.Equal(["Earlier question", "Selected question"], sent.Requests.Select(item => item.Label).ToArray());
        Assert.True(sent.HasPrevious);
        Assert.Equal(13, sent.CurrentChars);
        Assert.Equal(0, sent.Current.Single(category => category.Key == "assistant").Chars);
        Assert.Equal(0, sent.Current.Single(category => category.Key == "tool").Chars);
        Assert.Equal(PromptSignatureBuilder.Build([("Earlier question", earlierEvents)]).CurrentChars, sent.PreviousChars);

        var call = ExecutionReplayBuilder.SignatureAt(exchanges, "selected", 3);
        var response = ExecutionReplayBuilder.SignatureAt(exchanges, "selected", 4);
        Assert.Equal(response.Current.ToArray(), call.Current.ToArray());
        Assert.True(call.CurrentChars > sent.CurrentChars);
        Assert.Equal(0, call.Current.Single(category => category.Key == "tool").Chars);
        var nextRequest = ExecutionReplayBuilder.SignatureAt(exchanges, "selected", 6);
        Assert.Equal("LATER_RESULT".Length, nextRequest.Current.Single(category => category.Key == "tool").Chars);
        foreach (var sequence in new[] { 2, 3, 4, 5, 6, 7 })
        {
            var signature = ExecutionReplayBuilder.SignatureAt(exchanges, "selected", sequence);
            Assert.Equal(ExecutionReplayBuilder.ContextAt(exchanges, "selected", sequence).Chars, signature.CurrentChars);
            Assert.Equal(signature.CurrentChars, signature.Requests[^1].ReusedChars + signature.Requests[^1].AddedChars);
            Assert.Equal(2, signature.Requests.Count);
        }
        var completed = ExecutionReplayBuilder.SignatureAt(exchanges, "selected", 7);
        var expected = PromptSignatureBuilder.Build([("Earlier question", earlierEvents), ("Selected question", selectedEvents)]);
        Assert.Equal(expected.Current.ToArray(), completed.Current.ToArray());
        Assert.Equal(expected.MatchPercent, completed.MatchPercent);
        Assert.False(ExecutionReplayBuilder.SignatureAt(exchanges, "first", 2).HasPrevious);
    }

    /// <summary>Intake, empty captures and invalid cursors cannot display an older exchange as current.</summary>
    [Fact]
    public void Signature_BeforeRequestIsEmptyEvenWithEarlierHistory()
    {
        var exchanges = ExecutionReplayBuilder.Build(
            [new("first", "Earlier", "Agent", "Answer", null, [Event(1, "llm-request", 1)]),
             new("selected", "Selected", "Agent", "", null, [Event(1, "received", 0)]),
             new("empty", "Empty", "Agent", "", null, [])], -1);
        Assert.Same(PromptSignatureView.Empty, ExecutionReplayBuilder.SignatureAt(exchanges, "selected", 1));
        Assert.Same(PromptSignatureView.Empty, ExecutionReplayBuilder.SignatureAt(exchanges, "empty", null));
        Assert.Same(PromptSignatureView.Empty, ExecutionReplayBuilder.SignatureAt(exchanges, "first", 999));
        Assert.Same(PromptSignatureView.Empty, ExecutionReplayBuilder.SignatureAt(exchanges, "missing", 1));
    }

    private static ExecutionExchange Build(params FlowEvent[] events) =>
        ExecutionReplayBuilder.Build([new ConversationTurn("x", "hi", "Agent", "done", null, events)], -1)[0];

    private static ExecutionExchange BuildLive(params FlowEvent[] events) =>
        ExecutionReplayBuilder.Build([new ConversationTurn("x", "hi", "Agent", string.Empty, null, events)], 0)[0];

    private static FlowEvent Event(int sequence, string kind, int turn, string? callId = null) =>
        new(sequence, kind, kind, null, turn, "{}", callId);
}
