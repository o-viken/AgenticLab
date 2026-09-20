using AgenticLab.Extensibility.Examples;
using AgenticLab.Examples.Windfarm.Api;
using AgenticLab.Examples.Windfarm.Components;
using AgenticLab.Examples.Windfarm.Data;
using AgenticLab.Examples.Windfarm.Process;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AgenticLab.Examples.Windfarm.Tests;

public sealed class WindfarmPanelTests
{
    [Fact]
    public async Task LateCaseResponseCannotOverwriteNewConversation()
    {
        var client = new FakeClient();
        var old = client.Process.Create("old", "inspection");
        var current = client.Process.Create("current", "replanning");
        client.DelayedRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var controller = new WindfarmCaseController(client);
        var stale = controller.SetContextAsync(Context("old"));
        await controller.SetContextAsync(Context("current"));
        client.DelayedRead.SetResult(old);
        await stale;
        Assert.Equal(current.Id, controller.Snapshot!.Id);
    }

    [Fact]
    public async Task ApprovalUsesExactProposalAndSuppressesDoubleClick()
    {
        var client = new FakeClient();
        client.Process.Create("conversation", "inspection");
        var proposal = WindfarmProcessTests.Propose(client.Process, "conversation");
        client.DecisionRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var controller = new WindfarmCaseController(client);
        await controller.SetContextAsync(Context("conversation"));
        Assert.True(controller.CanDecide);
        var pending = controller.DecideAsync("approve", "Reviewed");
        await controller.DecideAsync("approve", "Reviewed");
        Assert.False(controller.CanDecide);
        var request = Assert.Single(client.Decisions);
        Assert.Equal(proposal.Revision, request.Revision);
        Assert.Equal(proposal.ProposalHash, request.ProposalHash);
        Assert.Equal("conversation", request.ConversationId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.ArchiveAsync());
        client.DecisionRelease.SetResult();
        await pending;
        Assert.Equal(CaseStage.WorkOrderCreated, controller.Snapshot!.Stage);
        Assert.False(controller.CanDecide);
    }

    [Fact]
    public async Task FailedDecisionRetryKeepsIdempotencyKeyAndResetArchivesCase()
    {
        var client = new FakeClient { FailFirstDecision = true };
        var original = client.Process.Create("conversation", "inspection");
        WindfarmProcessTests.Propose(client.Process, "conversation");
        using var controller = new WindfarmCaseController(client);
        await controller.SetContextAsync(Context("conversation"));
        await controller.DecideAsync("approve", null);
        Assert.NotNull(controller.Error);
        Assert.True(controller.CanDecide);
        await controller.DecideAsync("approve", null);
        Assert.Equal(2, client.Decisions.Count);
        Assert.Equal(client.Decisions[0].IdempotencyKey, client.Decisions[1].IdempotencyKey);
        Assert.NotNull(controller.Snapshot!.Order);
        await controller.ArchiveAsync();
        Assert.Null(controller.Snapshot);
        Assert.Equal(CaseStage.Archived, client.Process.Get("conversation", original.Id).Stage);
    }

    [Fact]
    public async Task RunningContextCannotAuthorizeAnOrder()
    {
        var client = new FakeClient();
        client.Process.Create("conversation", "inspection");
        WindfarmProcessTests.Propose(client.Process, "conversation");
        using var controller = new WindfarmCaseController(client);
        await controller.SetContextAsync(Context("conversation") with { Running = true });
        await controller.DecideAsync("approve", null);
        Assert.Empty(client.Decisions);
        Assert.Null(client.Process.Find("conversation")!.Order);
    }

    [Fact]
    public async Task RegisteredPanelRendersAuthoritativeProposalWithoutWebStateDependencies()
    {
        var client = new FakeClient();
        client.Process.Create("conversation", "inspection");
        var proposal = WindfarmProcessTests.Propose(client.Process, "conversation");
        var services = new ServiceCollection().AddLogging().AddSingleton<IWindfarmClient>(client);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<WindfarmPanel>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["Context"] = Context("conversation") }));
            return output.ToHtmlString();
        });
        Assert.Contains("Awaiting human approval", html);
        Assert.Contains("Approve plan", html);
        Assert.Contains("Reject plan", html);
        Assert.Contains(proposal.ProposalHash![..12], html);
        Assert.Contains("crew-alpha", html);
        Assert.Contains("Synthetic operations", html);
        Assert.Contains("/ r1", html);
        Assert.DoesNotContain("@current", html);
        Assert.DoesNotContain("aria-busy=\"True\"", html);
    }

    private static ExamplePanelContext Context(string conversation) =>
        new(conversation, "windfarm", "WindfarmCoordinator", false, 0, _ => { }, () => Task.FromResult("next"));

    [Fact]
    public async Task PageNotificationDuringCreateCannotDiscardTheCreatedCase()
    {
        var client = new FakeClient { StartRelease = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var controller = new WindfarmCaseController(client);
        await controller.SetContextAsync(Context("conversation"));
        var creating = controller.StartAsync("inspection");
        await controller.SetContextAsync(Context("conversation") with { RunVersion = 1 });
        client.StartRelease.SetResult();
        await creating;
        Assert.NotNull(controller.Snapshot);
        Assert.Equal("conversation", controller.Snapshot.ConversationId);
    }

    private sealed class FakeClient : IWindfarmClient
    {
        public WindfarmProcess Process { get; } = WindfarmProcessTests.Create();
        public TaskCompletionSource<CaseSnapshot?>? DelayedRead { get; set; }
        public TaskCompletionSource? DecisionRelease { get; set; }
        public TaskCompletionSource? StartRelease { get; set; }
        public bool FailFirstDecision { get; set; }
        public List<DecisionRequest> Decisions { get; } = [];
        public Task<IReadOnlyList<ScenarioSummary>> ScenariosAsync(CancellationToken cancellationToken) => Task.FromResult(WindfarmFixtures.List());
        public Task<CaseSnapshot?> ActiveAsync(string conversationId, CancellationToken cancellationToken) =>
            conversationId == "old" && DelayedRead is not null ? DelayedRead.Task : Task.FromResult(Process.Find(conversationId));
        public async Task<CaseSnapshot> StartAsync(string conversationId, string scenarioId, CancellationToken cancellationToken)
        {
            if (StartRelease is not null) await StartRelease.Task.WaitAsync(cancellationToken);
            return Process.Create(conversationId, scenarioId);
        }
        public async Task<CaseSnapshot> DecideAsync(string caseId, DecisionRequest request, CancellationToken cancellationToken)
        {
            Decisions.Add(request);
            if (DecisionRelease is not null) await DecisionRelease.Task.WaitAsync(cancellationToken);
            if (FailFirstDecision && Decisions.Count == 1) throw new HttpRequestException("Connection interrupted.");
            return Process.Decide(request.ConversationId, caseId, request.Revision, request.ProposalHash, request.Decision, request.IdempotencyKey, request.Reason);
        }
        public Task ArchiveAsync(string conversationId, string caseId, CancellationToken cancellationToken)
        {
            Process.Archive(conversationId, caseId);
            return Task.CompletedTask;
        }
    }
}