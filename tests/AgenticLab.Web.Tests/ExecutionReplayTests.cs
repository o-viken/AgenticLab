using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using AgenticLab.Web;
using AgenticLab.Web.Components.Pages;
using AgenticLab.Web.Flow;
using Xunit;

namespace AgenticLab.Web.Tests;

/// <summary>
/// Protects what the Execution explorer claims about a run: stages must group into the round-trip they
/// belong to, read in causal order, and a partial run must never be reported as a completed one.
/// </summary>
public sealed class ExecutionReplayTests
{
    [Theory]
    [InlineData("1|0|1|400|300|450", false)]
    [InlineData("1|0|1|400|300|450|0", false)]
    [InlineData("1|0|1|400|300|450|1", true)]
    public void LayoutPreferences_PreserveLegacySizesAndRoundTrip(string stored, bool adaptive)
    {
        Assert.True(PanelState.TryParse(stored, out var state));
        Assert.Equal(new PanelState(true, false, true, 400, 300, 450, adaptive), state);
        Assert.True(PanelState.TryParse(state.Serialize(), out var restored));
        Assert.Equal(state, restored);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0|0|0|276|260")]
    [InlineData("0|0|0|wide|260|380|1")]
    [InlineData("0|0|0|276|260|380|invalid")]
    [InlineData("0|0|0|276|260|380|1|extra")]
    public void LayoutPreferences_RejectMalformedState(string? stored) =>
        Assert.False(PanelState.TryParse(stored, out _));

    [Fact]
    public void LayoutSizing_AdaptsUntilDraggedAndClampsIndependentTracks()
    {
        var changes = 0;
        var layout = new PanelLayout(() => changes++, () => true);
        Assert.True(layout.AdaptiveConversationWidth);
        Assert.Contains("--left-w: minmax(0, 1.18fr)", layout.BodyStyle);
        layout.LeftPanelWidth = 2000;
        Assert.False(layout.AdaptiveConversationWidth);
        Assert.Equal(960, layout.LeftPanelWidth);
        Assert.Contains("min(960px, 65cqw)", layout.BodyStyle);
        layout.RightPanelWidth = 2000;
        Assert.Equal(640, layout.RightPanelWidth);
        layout.LeftPanelCollapsed = true;
        Assert.Contains("--left-w: 44px", layout.BodyStyle);
        var beforeReset = changes;
        layout.Reset();
        Assert.Equal(beforeReset + 1, changes);
        Assert.True(layout.AdaptiveConversationWidth);
        Assert.False(layout.LeftPanelCollapsed);
        Assert.Equal(240, layout.BottomPanelHeight);
        layout.Init(false, false, false, 410, 310, 440);
        Assert.False(layout.AdaptiveConversationWidth);
        Assert.Equal(410, layout.LeftPanelWidth);
        Assert.Equal(310, layout.RightPanelWidth);
        Assert.Equal(440, layout.BottomPanelHeight);
    }

    [Fact]
    public void LayoutReset_PreservesDraftOptionsReplayAndIndependentDocks()
    {
        var view = new FlowViewState(new ConceptCatalog(new ReplayEnvironment(), NullLogger<ConceptCatalog>.Instance));
        view.Message = "draft";
        view.Cursor.SelectStage("exchange", 3);
        view.Options.SetToolEnabled("test", false);
        view.Concepts.OpenConcept("tools");
        view.Details.Open(HostDetailSection.Tools);
        view.Details.Width = 420;
        view.Layout.LeftPanelWidth = 500;
        view.Layout.BottomPanelMaximized = true;
        view.Layout.Reset();
        Assert.Equal("draft", view.Message);
        Assert.Equal("exchange", view.Cursor.ExchangeId);
        Assert.Equal(3, view.Cursor.Sequence);
        Assert.False(view.Options.IsToolEnabled("test"));
        Assert.True(view.Layout.RightPanelVisible);
        Assert.Equal(HostDetailSection.Tools, view.Details.Section);
        Assert.Equal(420, view.Details.Width);
        Assert.False(view.Layout.BottomPanelMaximized);
        Assert.True(view.Layout.AdaptiveConversationWidth);
    }

    /// <summary>The client stays outside the host while application-owned host layers retain their contributor.</summary>
    [Fact]
    public void HostDetails_SectionsExcludeClientAndKeepApplicationContributors()
    {
        Assert.DoesNotContain("Client", Enum.GetNames<HostDetailSection>());
        Assert.Equal("app", HostDetailsSelection.Contributor(HostDetailSection.SystemPrompt));
        Assert.Equal("app", HostDetailsSelection.Contributor(HostDetailSection.Environment));
    }

    /// <summary>Host labels describe the service and selected agent, not the client displaying them.</summary>
    [Fact]
    public void HostDetails_HostLabelsDescribeServiceWithoutClient()
    {
        var view = new FlowViewState(new ConceptCatalog(new ReplayEnvironment(), NullLogger<ConceptCatalog>.Instance));
        view.Vendor = Vendor.Default;
        view.SelectedAgent = "Coder";
        view.Diagram.ShowTechnicalLabels = false;
        Assert.Equal("Agent host", view.Harness.Label);
        Assert.Equal("Agent service", view.Harness.Subtitle);

        view.Diagram.ShowTechnicalLabels = true;
        Assert.Equal("Agent host · AiService · Coder", view.Harness.Subtitle);
        view.SelectedAgent = "Chat";
        Assert.Equal("Agent host · AiService · Chat", view.Harness.Subtitle);
    }

    [Fact]
    public void HostDetails_SelectsIndividualA2AAgentsAndClearsSelection()
    {
        var reveals = 0;
        var notifications = 0;
        var details = new HostDetailsSelection(() => reveals++, () => notifications++);
        details.OpenA2A("research");
        Assert.Equal(HostDetailSection.A2A, details.Section);
        Assert.Equal("research", details.A2AAgentName);
        Assert.True(details.Active);
        Assert.Equal(1, details.Activation);
        Assert.Equal(1, reveals);
        Assert.Equal(1, notifications);

        details.Collapsed = true;
        details.OpenA2A("poet");
        Assert.Equal("poet", details.A2AAgentName);
        Assert.False(details.Collapsed);
        Assert.Equal(2, details.Activation);

        details.Open(HostDetailSection.A2A);
        Assert.Null(details.A2AAgentName);
        details.OpenA2A("research");
        details.Open(HostDetailSection.Settings);
        Assert.Null(details.A2AAgentName);
        details.OpenA2A("research");
        details.Close();
        Assert.False(details.Active);
        Assert.Null(details.A2AAgentName);
    }

    [Fact]
    public void HostDetails_ShowsOnlySelectedPartWithoutExecutionOrComposedExtras()
    {
        var view = new FlowViewState(new ConceptCatalog(new ReplayEnvironment(), NullLogger<ConceptCatalog>.Instance));
        view.Roster.SetAgents([new AgentInfo("Coder", "Selected persona description", [], RequiresWorkspace: true, SupportsSkills: true, ModelId: "test-model")]);
        view.SelectedAgent = "Coder";
        view.Harness.SetPrompt("Exact host prompt");
        view.Message = "Unsubmitted draft";
        using var handler = new ReplayHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        using var run = new FlowRunController(new AiServiceClient(http), view, new());
        var settings = Assert.Single(HostDetailsBuilder.Build(HostDetailSection.Settings, view, run));
        Assert.Equal("Provider: Azure OpenAI\nDeployment: test-model", settings.Text);
        Assert.Equal("Exact host prompt", Assert.Single(HostDetailsBuilder.Build(HostDetailSection.SystemPrompt, view, run)).Text);
        Assert.Equal("Selected persona description", Assert.Single(HostDetailsBuilder.Build(HostDetailSection.Persona, view, run)).Text);
        Assert.Single(HostDetailsBuilder.Build(HostDetailSection.Skills, view, run));
        Assert.Single(HostDetailsBuilder.Build(HostDetailSection.Instructions, view, run));
        Assert.Equal("No message submitted.", Assert.Single(HostDetailsBuilder.Build(HostDetailSection.UserPrompt, view, run)).Text);
    }

    [Fact]
    public void HostDetails_CaptureUsesMatchingExchangeAndCausalPrefix()
    {
        var view = new FlowViewState(new ConceptCatalog(new ReplayEnvironment(), NullLogger<ConceptCatalog>.Instance));
        view.SelectedAgent = "Chat";
        var first = new FlowEvent(2, "llm-request", "first", null, 1, "{\"instructions\":\"<agentMode>Exact persona</agentMode>\",\"tools\":[]}");
        var later = new FlowEvent(4, "llm-request", "later", null, 2, "{\"instructions\":\"future\"}");
        var exchange = ExecutionReplayBuilder.Build([new ConversationTurn("capture", "hello", "Chat", "", null,
            [new FlowEvent(1, "received", "received", null), first, later], view.VendorKey)], -1)[0];
        Assert.Null(HostDetailsBuilder.RequestFor(view, exchange, 1, true));
        Assert.Same(first, HostDetailsBuilder.RequestFor(view, exchange, 2, true));
        Assert.Same(later, HostDetailsBuilder.RequestFor(view, exchange, 1, false));
        Assert.Equal("Exact persona", PromptSignatureBuilder.AgentModeText(HostDetailsBuilder.CapturedField(first.Data, "instructions")));
        Assert.Equal("[]", HostDetailsBuilder.CapturedField(first.Data, "tools"));
        Assert.Null(HostDetailsBuilder.CapturedField("not json", "tools"));
        Assert.Null(HostDetailsBuilder.CapturedField("[]", "tools"));
        view.Workspace = "remembered workspace";
        Assert.Null(HostDetailsBuilder.RequestFor(view, exchange, 2, true));
        Assert.Same(first, HostDetailsBuilder.RequestFor(view, exchange with { Workspace = view.Workspace }, 2, true));
        view.Workspace = "";
        view.SelectedAgent = "Other";
        Assert.Null(HostDetailsBuilder.RequestFor(view, exchange, 2, true));
        view.SelectedAgent = "Chat";
        view.Vendor = Vendor.Default;
        Assert.Null(HostDetailsBuilder.RequestFor(view, exchange, 2, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostDetails_StalePromptSuccessOrFailureCannotOverwriteNewSelection(bool oldFails)
    {
        var view = new FlowViewState(new ConceptCatalog(new ReplayEnvironment(), NullLogger<ConceptCatalog>.Instance));
        using var handler = new InspectorHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        var catalogs = new WorkspaceCatalogs(new AiServiceClient(http), view, () => Task.CompletedTask);
        view.SelectedAgent = "First";
        var older = catalogs.RefreshHarnessPromptAsync();
        view.SelectedAgent = "Second";
        Assert.False(view.Harness.HasPromptText);
        var newer = catalogs.RefreshHarnessPromptAsync();
        handler.Pending[1].SetResult(new(HttpStatusCode.OK) { Content = new StringContent("{\"prompt\":\"new prompt\"}") });
        await newer;
        handler.Pending[0].SetResult(new(oldFails ? HttpStatusCode.InternalServerError : HttpStatusCode.OK)
            { Content = new StringContent("{\"prompt\":\"old prompt\"}") });
        await older;
        Assert.Equal("new prompt", view.Harness.PromptText);
        var repeatedOlder = catalogs.RefreshHarnessPromptAsync();
        var repeatedNewer = catalogs.RefreshHarnessPromptAsync();
        handler.Pending[3].SetResult(new(HttpStatusCode.OK) { Content = new StringContent("{\"prompt\":\"latest\"}") });
        await repeatedNewer;
        handler.Pending[2].SetResult(new(HttpStatusCode.OK) { Content = new StringContent("{\"prompt\":\"stale\"}") });
        await repeatedOlder;
        Assert.Equal("latest", view.Harness.PromptText);
        view.Workspace = "different workspace";
        Assert.False(view.Harness.HasPromptText);
    }

    private sealed class InspectorHandler : HttpMessageHandler
    {
        public List<TaskCompletionSource<HttpResponseMessage>> Pending { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending.Add(completion);
            return completion.Task;
        }
    }

    [Fact]
    public async Task HostDetails_CataloguesHideOldWorkspaceAndIgnoreLateResults()
    {
        var view = new FlowViewState(new ConceptCatalog(new ReplayEnvironment(), NullLogger<ConceptCatalog>.Instance));
        view.Roster.SetAgents([new AgentInfo("Coder", "description", ["ReadFile"], RequiresWorkspace: true, SupportsSkills: true)]);
        view.SelectedAgent = "Coder";
        view.Workspace = "first";
        using var handler = new InspectorHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        var catalogs = new WorkspaceCatalogs(new AiServiceClient(http), view, () => Task.CompletedTask);
        var oldRequest = catalogs.RefreshKnownSkillsAsync();
        view.Workspace = "second";
        Assert.Empty(catalogs.KnownSkills);
        var currentRequest = catalogs.RefreshKnownSkillsAsync();
        handler.Pending[1].SetResult(new(HttpStatusCode.OK) { Content = new StringContent("{\"skills\":[{\"name\":\"current\",\"description\":\"new\"}]}") });
        await currentRequest;
        handler.Pending[0].SetResult(new(HttpStatusCode.OK) { Content = new StringContent("{\"skills\":[{\"name\":\"stale\",\"description\":\"old\"}]}") });
        await oldRequest;
        Assert.Equal("current", Assert.Single(catalogs.KnownSkills).Name);
        view.Workspace = "third";
        Assert.Empty(catalogs.KnownSkills);
    }

    [Fact]
    public async Task HostDetails_InspectionPreservesConversationAndClearsCapturesOnReset()
    {
        var view = new FlowViewState(new ConceptCatalog(new ReplayEnvironment(), NullLogger<ConceptCatalog>.Instance));
        using var handler = new ReplayHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        using var run = new FlowRunController(new AiServiceClient(http), view, new());
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        run.Changed += () =>
        {
            if (!run.Running) finished.TrySetResult();
            return Task.CompletedTask;
        };
        view.Message = "sent message";
        await run.SendAsync();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var exchange = Assert.Single(run.Projections.Exchanges);
        view.Cursor.SelectStage(exchange.Id, 1);
        view.Message = "draft";
        view.Details.Open(HostDetailSection.Context);
        var blocks = HostDetailsBuilder.Build(HostDetailSection.Context, view, run);
        Assert.Contains(blocks, block => block.Title == "Messages sent to the model" && block.Text.Contains("hello"));
        Assert.DoesNotContain(blocks, block => block.Text == "answer");
        view.Details.Open(HostDetailSection.Tools);
        view.Concepts.OpenConcept("tools");
        Assert.True(view.Details.Active);
        Assert.Equal(HostDetailSection.Tools, view.Details.Section);
        Assert.Equal("draft", view.Message);
        Assert.Equal(exchange.Id, view.Cursor.ExchangeId);
        Assert.Single(handler.ConversationIds);
        view.Details.Open(HostDetailSection.Context);
        await run.NewConversationAsync();
        Assert.Equal(HostDetailSection.Context, view.Details.Section);
        Assert.DoesNotContain(HostDetailsBuilder.Build(HostDetailSection.Context, view, run), block => block.Text.Contains("hello"));
    }

    [Fact]
    public void HostDetails_ReplacesSelectionIndependentlyOfLearnWithoutChangingRunInputs()
    {
        var catalog = new ConceptCatalog(new ReplayEnvironment(), NullLogger<ConceptCatalog>.Instance);
        var view = new FlowViewState(catalog);
        view.Message = "unsent draft";
        view.Cursor.SelectStage("exchange", 3);
        view.Options.SetToolEnabled("test", false);
        view.Layout.RightPanelCollapsed = true;
        view.Details.Open(HostDetailSection.SystemPrompt);
        Assert.False(view.Layout.RightPanelVisible);
        Assert.True(view.Layout.RightPanelCollapsed);
        Assert.False(view.Details.Collapsed);
        view.Details.Open(HostDetailSection.Tools);
        Assert.Equal(HostDetailSection.Tools, view.Details.Section);
        view.Concepts.ShowConcepts = true;
        Assert.True(view.Details.Active);
        Assert.True(view.Layout.RightPanelVisible);
        view.Details.Width = 420;
        Assert.Equal(260, view.Layout.RightPanelWidth);
        view.Layout.RightPanelWidth = 300;
        Assert.Equal(420, view.Details.Width);
        view.Details.Collapsed = true;
        Assert.False(view.Layout.RightPanelCollapsed);
        Assert.Contains("44px", view.Details.PanelStyle);
        view.Concepts.OpenConcept("tools");
        Assert.True(view.Details.Collapsed);
        view.Details.Open(HostDetailSection.Tools);
        Assert.False(view.Details.Collapsed);
        view.Concepts.ShowConcepts = false;
        Assert.True(view.Details.Active);
        view.Layout.ToggleRightPanel();
        Assert.Equal(HostDetailSection.Tools, view.Details.Section);
        Assert.Equal("unsent draft", view.Message);
        Assert.Equal("exchange", view.Cursor.ExchangeId);
        Assert.Equal(3, view.Cursor.Sequence);
        Assert.False(view.Options.IsToolEnabled("test"));
        view.SelectedAgent = "another agent";
        view.Vendor = Vendor.Default;
        Assert.Equal(HostDetailSection.Tools, view.Details.Section);
        view.Details.Close();
        Assert.False(view.Layout.RightPanelVisible);
        view.Concepts.ShowConcepts = true;
        view.Details.Open(HostDetailSection.Persona);
        view.Details.Close();
        Assert.True(view.Layout.RightPanelVisible);
        Assert.False(view.Details.Active);
        Assert.Contains("0px", view.Details.PanelStyle);
    }

    [Theory]
    [InlineData(nameof(DiagramOptions.ShowModel))]
    [InlineData(nameof(DiagramOptions.ShowLoop))]
    [InlineData(nameof(DiagramOptions.ShowTechnicalLabels))]
    [InlineData(nameof(DiagramOptions.ShowTools))]
    [InlineData(nameof(DiagramOptions.ShowSkills))]
    [InlineData(nameof(DiagramOptions.ShowMcp))]
    [InlineData(nameof(DiagramOptions.ShowA2A))]
    [InlineData(nameof(DiagramOptions.ShowHarnessBoundary))]
    [InlineData(nameof(DiagramOptions.ShowAgentBoundary))]
    [InlineData(nameof(DiagramOptions.ShowEnvironment))]
    [InlineData(nameof(DiagramOptions.ExpandHarness))]
    [InlineData(nameof(DiagramOptions.ShowPromptSignature))]
    [InlineData(nameof(DiagramOptions.ShowInferenceView))]
    [InlineData(nameof(DiagramOptions.ShowEmbeddingsView))]
    [InlineData(nameof(DiagramOptions.ShowNetworkView))]
    public void DiagramPresets_EveryDisplayOptionParticipatesInMatchingAndReset(string optionName)
    {
        var option = typeof(DiagramOptions).GetProperty(optionName)!;
        foreach (var preset in Enum.GetValues<DiagramPreset>())
        {
            var diagram = new DiagramOptions(() => { });
            diagram.ApplyPreset(preset);
            var original = (bool)option.GetValue(diagram)!;
            option.SetValue(diagram, !original);
            Assert.Null(diagram.Preset);
            option.SetValue(diagram, original);
            Assert.Equal(preset, diagram.Preset);
            option.SetValue(diagram, !original);
            diagram.ApplyPreset(preset);
            Assert.Equal(original, option.GetValue(diagram));
            Assert.Equal(preset, diagram.Preset);
        }
    }

    [Fact]
    public void DiagramPresets_ApplyAtomicallyAndRecognizeCustomOptions()
    {
        var notifications = 0;
        var diagram = new DiagramOptions(() => notifications++);
        Assert.Equal(DiagramPreset.Basic, diagram.Preset);
        diagram.ApplyPreset(DiagramPreset.Technical);
        Assert.Equal(1, notifications);
        Assert.True(diagram.ShowModel);
        Assert.True(diagram.ShowLoop);
        Assert.True(diagram.ShowTechnicalLabels);
        Assert.True(diagram.ShowTools);
        Assert.Equal(DiagramPreset.Technical, diagram.Preset);
        diagram.ShowTools = false;
        Assert.Null(diagram.Preset);
        diagram.ShowTools = true;
        Assert.Equal(DiagramPreset.Technical, diagram.Preset);
        diagram.ShowModel = false;
        Assert.True(diagram.ShowLoop);
        Assert.Null(diagram.Preset);
        diagram.ShowModel = true;
        Assert.Equal(DiagramPreset.Technical, diagram.Preset);
        diagram.ShowSkills = diagram.ShowMcp = diagram.ShowA2A = true;
        diagram.ShowHarnessBoundary = diagram.ShowAgentBoundary = diagram.ShowEnvironment = true;
        diagram.ExpandHarness = diagram.ShowPromptSignature = diagram.ShowInferenceView = true;
        diagram.ShowEmbeddingsView = diagram.ShowNetworkView = true;
        diagram.SelectToken("context");
        diagram.PromptSignatureDelta = true;
        var beforeReset = notifications;
        diagram.ApplyPreset(DiagramPreset.Basic);
        Assert.Equal(beforeReset + 1, notifications);
        Assert.Equal(DiagramPreset.Basic, diagram.Preset);
        Assert.False(diagram.ShowTools);
        Assert.Equal("context", diagram.SelectedToken);
        Assert.True(diagram.PromptSignatureDelta);
    }

    /// <summary>Opening Discovery is a transient layout change, not a new conversation or replay selection.</summary>
    [Fact]
    public async Task DiscoveryOverlay_PreservesConversationDraftAndReplay()
    {
        using var handler = new ReplayHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        var catalog = new ConceptCatalog(new ReplayEnvironment(), NullLogger<ConceptCatalog>.Instance);
        var view = new FlowViewState(catalog);
        using var run = new FlowRunController(new AiServiceClient(http), view, new());
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        run.Changed += () =>
        {
            if (!run.Running) finished.TrySetResult();
            return Task.CompletedTask;
        };
        view.Message = "first";
        await run.SendAsync();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var exchange = Assert.Single(run.Projections.Exchanges);
        view.Cursor.SelectStage(exchange.Id, 2);
        view.Message = "unsent draft";
        view.Workspace = "test workspace";
        var conversationId = Assert.Single(handler.ConversationIds);
        var selectedStage = run.Replay.SelectedStage;
        view.Options.SetToolEnabled("test-tool", false);
        view.Options.SetSkillEnabled("test-skill", false);
        view.Options.SetInstructionEnabled("test-instruction", true);
        view.Options.SetBreakpoint("before-tool", true);
        view.Diagram.ApplyPreset(DiagramPreset.Technical);
        view.Diagram.ShowTools = true;
        view.Diagram.ExpandHarness = true;
        view.Diagram.ApplyPreset(DiagramPreset.Basic);
        Assert.False(view.Options.IsToolEnabled("test-tool"));
        Assert.False(view.Options.IsSkillEnabled("test-skill"));
        Assert.True(view.Options.IsInstructionEnabled("test-instruction"));
        Assert.True(view.Options.IsBreakpointEnabled("before-tool"));
        view.Layout.DiscoveryOpen = true;
        Assert.True(view.Layout.DiscoveryOpen);
        view.Layout.DiscoveryOpen = false;
        Assert.False(view.Layout.DiscoveryOpen);
        Assert.Equal("unsent draft", view.Message);
        Assert.Equal("test workspace", view.Workspace);
        Assert.Equal(exchange.Id, Assert.Single(run.Projections.Exchanges).Id);
        Assert.Equal(selectedStage, run.Replay.SelectedStage);
        Assert.Equal(0, handler.Resets);
        finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await run.SendAsync();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, handler.ConversationIds.Count);
        Assert.All(handler.ConversationIds, actual => Assert.Equal(conversationId, actual));
    }

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
        internal List<string> ConversationIds { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/chat/reset")
            {
                Resets++;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            Assert.Equal("/chat/stream", request.RequestUri.AbsolutePath);
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            ConversationIds.Add(payload.RootElement.GetProperty("conversationId").GetString()!);
            FlowEvent[] events =
            [
                Event(1, "llm-request", 1) with
                { Data = """{"instructions":"rules","messages":[{"role":"user","contents":[{"text":"hello"}]}]}""" },
                Event(2, "final", 1) with { Detail = "answer", Data = "answer" },
            ];
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Concat(events.Select(stage => $"event: flow\ndata: {JsonSerializer.Serialize(stage)}\n\n"))),
            };
        }
    }

    private sealed class ReplayEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "AgenticLab.Web.Tests";
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

    [Fact]
    public void HostDetails_A2AInspectsOnlyTheSelectedAgentAndCausalCapture()
    {
        A2AChip[] roster = [new("research", "Research description"), new("poet", "Poetry description")];
        var question = "Full question\n" + new string('q', 400);
        var answer = "Full answer\n" + new string('a', 400);
        var call = Delegation(2, "research-call", "research", question);
        var result = Event(3, "tool-result", 1, "research-call") with { Data = answer };
        var other = Delegation(4, "poet-call", "poet", "Unrelated request");
        var exchange = ExecutionReplayBuilder.Build([new ConversationTurn("a2a", "hello", "Orchestrator", "done", null,
            [Event(1, "received", 0), call, result, other], A2AAgents: roster)], -1)[0];
        const string source = "Captured exchange 1";
        var atCall = A2AFlowBuilder.Build(roster, ExecutionReplayBuilder.PrefixThrough(exchange, 2), false);
        var details = HostDetailsBuilder.BuildA2A(atCall, "RESEARCH", true, source);
        Assert.Contains(details, block => block.Title == "Description" && block.Text == "Research description");
        Assert.Contains(details, block => block.Title == "Status" && block.Text == "Delegation requested");
        Assert.Contains(details, block => block.Title == "Request" && block.Text == question && block.Source == source);
        Assert.Contains(details, block => block.Title == "Result" && block.Text == "No result captured at this position.");
        Assert.DoesNotContain(details, block => block.Text == answer || block.Text.Contains("Poetry") || block.Text == "Unrelated request");
        Assert.Contains(details, block => block.Title == "Remote configuration" && block.Text.Contains("not exposed"));

        var atResult = A2AFlowBuilder.Build(roster, ExecutionReplayBuilder.PrefixThrough(exchange, 3), false);
        details = HostDetailsBuilder.BuildA2A(atResult, "research", true, source);
        Assert.Contains(details, block => block.Title == "Result" && block.Text == answer && block.Source == source);
        var otherDetails = HostDetailsBuilder.BuildA2A(atResult, "poet", true, source);
        Assert.Contains(otherDetails, block => block.Title == "Status" && block.Text == "Available");
        Assert.DoesNotContain(otherDetails, block => block.Text == question || block.Text == answer);
    }

    [Fact]
    public void HostDetails_A2ACatalogueLinksAgentsAndHandlesMissingData()
    {
        var display = A2AFlowBuilder.Build([new("research", "Research description"), new("poet", "Poetry description")], [], false);
        var catalogue = HostDetailsBuilder.BuildA2A(display, null, true, "Discovered A2A agent");
        Assert.Equal(["research", "poet"], catalogue.Select(block => block.A2AAgentName));
        var missing = Assert.Single(HostDetailsBuilder.BuildA2A(display, "removed", true, "Discovered A2A agent"));
        Assert.Contains("not available", missing.Text);
        Assert.Equal("No connected agents available.", Assert.Single(HostDetailsBuilder.BuildA2A(A2AFlowView.Empty, null, true, "Discovery")).Text);
        Assert.Equal("Not applicable to this agent.", Assert.Single(HostDetailsBuilder.BuildA2A(A2AFlowView.Empty, null, false, "Discovery")).Text);
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
