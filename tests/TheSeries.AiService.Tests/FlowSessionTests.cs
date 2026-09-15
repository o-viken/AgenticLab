using TheSeries.AiService.Application;
using Xunit;

namespace TheSeries.AiService.Tests;

public sealed class FlowSessionTests
{
    [Fact]
    public async Task EarlyUserAnswerSurvivesBreakpointAndNextQuestionWaitsForANewAnswer()
    {
        using var scope = UserInputScope.Begin();
        scope.ProvideAnswer("Early answer");
        Assert.Equal("Early answer", await scope.WaitForAnswerAsync(CancellationToken.None));
        var second = scope.WaitForAnswerAsync(CancellationToken.None);
        Assert.False(second.IsCompleted);
        scope.ProvideAnswer("Second answer");
        Assert.Equal("Second answer", await second.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task SelectionUpdatesDoNotReleaseCurrentPauseAndNextReleasesOnlyOneStep()
    {
        using var session = new FlowSession("test", false, 0);
        session.SetBreakpoints(["before-tool"]);
        var wait = session.WaitForBreakpointAsync("before-tool", "Count", CancellationToken.None);
        var notice = await session.BreakpointEvents.Reader.ReadAsync();
        session.SetBreakpoints([]);
        session.Advance();
        session.Advance();
        Assert.False(wait.IsCompleted);
        Assert.True(session.ReleaseBreakpoint(notice.Id, true));
        await wait;
        await session.WaitForStepAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var nextStep = session.WaitForStepAsync(cancellation.Token);
        Assert.False(nextStep.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => nextStep);
        await session.WaitForBreakpointAsync("before-tool", "Count", CancellationToken.None);
    }

    [Fact]
    public async Task ExistingManualAndAutoPauseGatesWorkWithoutBreakpoints()
    {
        using var session = new FlowSession("discovery", true, 0);
        var manual = session.WaitForStepAsync(CancellationToken.None);
        Assert.False(manual.IsCompleted);
        session.Advance();
        await manual.WaitAsync(TimeSpan.FromSeconds(5));
        session.Manual = false;
        session.Paused = true;
        var paused = session.WaitForStepAsync(CancellationToken.None);
        Assert.False(paused.IsCompleted);
        session.Paused = false;
        await paused.WaitAsync(TimeSpan.FromSeconds(5));
        await session.WaitForStepAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BreakpointRequiresMatchingReleaseInEitherMode(bool manual)
    {
        using var session = new FlowSession("test", manual, 0);
        session.SetBreakpoints(["before-model"]);
        var wait = session.WaitForBreakpointAsync("before-model", null, CancellationToken.None);
        Assert.False(wait.IsCompleted);
        var notice = await session.BreakpointEvents.Reader.ReadAsync();
        Assert.True(notice.Paused);
        Assert.False(session.ReleaseBreakpoint("stale", false));
        Assert.False(wait.IsCompleted);
        Assert.True(session.ReleaseBreakpoint(notice.Id, false));
        await wait.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(session.Manual);
        Assert.False((await session.BreakpointEvents.Reader.ReadAsync()).Paused);

        var next = session.WaitForBreakpointAsync("before-model", null, CancellationToken.None);
        var nextNotice = await session.BreakpointEvents.Reader.ReadAsync();
        Assert.False(session.ReleaseBreakpoint(notice.Id, false));
        Assert.False(next.IsCompleted);
        Assert.True(session.ReleaseBreakpoint(nextNotice.Id, true));
        await next;
        Assert.True(session.Manual);
    }

    [Fact]
    public async Task DisabledBoundaryDoesNotWaitAndStopCancelsEnabledBoundary()
    {
        using var session = new FlowSession("test", false, 0);
        await session.WaitForBreakpointAsync("before-tool", "tool", CancellationToken.None);
        session.SetBreakpoints(["before-tool"]);
        var wait = session.WaitForBreakpointAsync("before-tool", "tool", CancellationToken.None);
        Assert.False(wait.IsCompleted);
        session.Stop();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }
}