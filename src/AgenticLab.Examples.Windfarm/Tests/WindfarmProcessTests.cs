using AgenticLab.Examples.Windfarm.Process;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace AgenticLab.Examples.Windfarm.Tests;

public sealed class WindfarmProcessTests
{
    [Fact]
    public async Task ApprovalIsVersionBoundAtomicAndIdempotent()
    {
        var process = Create();
        var opened = process.Create("conversation", "inspection");
        Assert.Throws<WindfarmException>(() => process.Decide("conversation", opened.Id, 0, "forged", "approve", "key", null));
        Assert.Null(process.Find("conversation")!.Order);
        var proposal = Propose(process, "conversation");
        Assert.Null(proposal.Order);
        Assert.Equal(CaseStage.AwaitingApproval, proposal.Stage);
        Assert.Throws<WindfarmException>(() => process.Draft("conversation", "later:crew-alpha", "Changed", proposal.Draft!.SourceIds));
        Assert.Throws<WindfarmException>(() => process.Decide("other-conversation", proposal.Id, proposal.Revision, proposal.ProposalHash!, "approve", "key", null));
        Assert.Throws<WindfarmException>(() => process.Decide("conversation", proposal.Id, proposal.Revision + 1, proposal.ProposalHash!, "approve", "key", null));

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            process.Decide("conversation", proposal.Id, proposal.Revision, proposal.ProposalHash!, "approve", "same-key", null))));
        Assert.Single(results.Select(result => result.Order!.Id).Distinct());
        Assert.Single(process.Find("conversation")!.Audit, entry => entry.Action == "WorkOrderCreated");
        Assert.Equal(1, results[0].Order!.ReservedQuantity);
        Assert.Throws<WindfarmException>(() => process.Decide("conversation", proposal.Id, proposal.Revision, proposal.ProposalHash!, "reject", "same-key", null));
    }

    [Theory]
    [InlineData("inspection", true)]
    [InlineData("replanning", false)]
    public void ConstraintsForceReplanning(string scenario, bool earlyEligible)
    {
        var process = Create();
        process.Create("conversation", scenario);
        var options = process.Options("conversation");
        Assert.Equal(earlyEligible, options.Single(option => option.Id == "early:crew-alpha").Eligible);
        Assert.True(options.Single(option => option.Id == "later:crew-alpha").Eligible);
        Assert.All(options.Where(option => option.CrewId == "crew-bravo"), option => Assert.False(option.Eligible));
        if (!earlyEligible)
        {
            var invalid = options.Single(option => option.Id == "early:crew-alpha");
            Assert.Throws<WindfarmException>(() => process.Draft("conversation", invalid.Id, "Ignore constraints", invalid.SourceIds));
        }
    }

    [Fact]
    public void MissingEvidenceBlocksDraftAndSubmission()
    {
        var process = Create();
        Assert.Equal(CaseStage.Blocked, process.Create("conversation", "missing-evidence").Stage);
        var option = process.Options("conversation")[0];
        Assert.All(process.Options("conversation"), candidate => Assert.False(candidate.Eligible));
        Assert.Throws<WindfarmException>(() => process.Draft("conversation", option.Id, "Guess", option.SourceIds));
        Assert.Throws<WindfarmException>(() => process.Submit("conversation"));
    }

    [Fact]
    public void RevisedDraftInvalidatesReviewsAndRejectsForeignSources()
    {
        var process = Create();
        process.Create("conversation", "inspection");
        var option = process.Options("conversation").First(candidate => candidate.Eligible);
        Assert.Throws<WindfarmException>(() => process.Draft("conversation", option.Id, "Inspection", ["invented"]));
        var draft = process.Draft("conversation", option.Id, "Inspection supported by measured trend", option.SourceIds);
        Assert.Throws<WindfarmException>(() => process.Submit("conversation"));
        process.RecordReview("conversation", draft.Id, WindfarmProcess.Reviewers[0], draft.Revision,
            new(ReviewVerdict.Ready, "Evidence supports inspection, not a confirmed diagnosis.", [], option.SourceIds.ToArray()));
        var revised = process.Draft("conversation", option.Id, "Revised rationale", option.SourceIds);
        Assert.Empty(revised.Reviews);
        Assert.Throws<WindfarmException>(() => process.RecordReview("conversation", draft.Id, WindfarmProcess.Reviewers[1], draft.Revision,
            new(ReviewVerdict.Ready, "Stale review", [], option.SourceIds.ToArray())));
        Assert.Throws<WindfarmException>(() => process.Submit("conversation"));
    }

    [Fact]
    public void RejectionAndArchiveNeverCreateAnOrder()
    {
        var process = Create();
        process.Create("conversation", "inspection");
        var proposal = Propose(process, "conversation");
        var rejected = process.Decide("conversation", proposal.Id, proposal.Revision, proposal.ProposalHash!, "reject", "reject-key", "Prefer tomorrow");
        Assert.Null(rejected.Order);
        var revised = Propose(process, "conversation");
        Assert.NotEqual(proposal.ProposalHash, revised.ProposalHash);
        process.Archive("conversation", revised.Id);
        Assert.Throws<WindfarmException>(() => process.Decide("conversation", revised.Id, revised.Revision, revised.ProposalHash!, "approve", "late", null));
        Assert.Null(process.Find("conversation"));
    }

    [Fact]
    public void RetentionAndCapacityDoNotMixCases()
    {
        var clock = new FakeTimeProvider();
        var process = new WindfarmProcess(clock, Options.Create(new WindfarmOptions { MaxCases = 2 }));
        process.Create("first", "inspection");
        process.Create("second", "inspection");
        Assert.Throws<WindfarmException>(() => process.Create("third", "inspection"));
        var proposal = Propose(process, "first");
        process.Decide("first", proposal.Id, proposal.Revision, proposal.ProposalHash!, "approve", "key", null);
        Assert.Equal(CaseStage.Investigating, process.Find("second")!.Stage);
        Assert.Null(process.Find("second")!.Order);
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(2, process.Cleanup());
        Assert.Null(process.Find("first"));
        Assert.NotNull(process.Create("third", "inspection"));
    }

    internal static WindfarmProcess Create() => new(TimeProvider.System, Options.Create(new WindfarmOptions()));

    [Fact]
    public void OldCaseRouteCannotChangeANewCaseInTheSameConversation()
    {
        var process = Create();
        var old = process.Create("conversation", "inspection");
        process.Archive("conversation", old.Id);
        var current = process.Create("conversation", "inspection");
        var option = process.Options("conversation").First(candidate => candidate.Eligible);
        Assert.Throws<WindfarmException>(() => process.Draft("conversation", option.Id, "Wrong case", option.SourceIds, old.Id));
        Assert.Throws<WindfarmException>(() => process.Submit("conversation", old.Id));
        Assert.Throws<WindfarmException>(() => process.Options("conversation", old.Id));
        var unchanged = process.Find("conversation")!;
        Assert.Equal(current.Id, unchanged.Id);
        Assert.Equal(CaseStage.Investigating, unchanged.Stage);
        Assert.Equal(0, unchanged.Revision);
        Assert.Null(unchanged.Draft);
        Assert.Single(unchanged.Audit);
    }

    internal static CaseSnapshot Propose(IWindfarmProcess process, string conversation)
    {
        var option = process.Options(conversation).First(candidate => candidate.Eligible);
        var draft = process.Draft(conversation, option.Id, "Inspect based on the observed trend; cause is not confirmed.", option.SourceIds);
        foreach (var reviewer in WindfarmProcess.Reviewers)
            process.RecordReview(conversation, draft.Id, reviewer, draft.Revision,
                new(ReviewVerdict.Ready, "The bounded evidence supports this simulated inspection plan.", [], option.SourceIds.ToArray()));
        return process.Submit(conversation);
    }
}