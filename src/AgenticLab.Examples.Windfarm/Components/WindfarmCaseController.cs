using AgenticLab.Extensibility.Examples;
using AgenticLab.Examples.Windfarm.Api;
using AgenticLab.Examples.Windfarm.Process;

namespace AgenticLab.Examples.Windfarm.Components;

internal sealed class WindfarmCaseController(IWindfarmClient client) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private ExamplePanelContext? _context;
    private int _generation;
    private bool _disposed;
    private DecisionRequest? _pendingDecision;
    public IReadOnlyList<ScenarioSummary> Scenarios { get; private set; } = [];
    public CaseSnapshot? Snapshot { get; private set; }
    public bool Busy { get; private set; }
    public string? Error { get; private set; }
    public bool Running => _context?.Running ?? false;
    public bool CanDecide => !Busy && !Running && Snapshot?.Stage == CaseStage.AwaitingApproval;

    public async Task InitializeAsync()
    {
        try
        {
            var scenarios = await client.ScenariosAsync(_lifetime.Token);
            if (!_disposed) Scenarios = scenarios;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!_disposed) Error = error.Message; }
    }

    public async Task SetContextAsync(ExamplePanelContext context)
    {
        var previous = _context;
        _context = context;
        var changed = previous?.ConversationId != context.ConversationId;
        if (changed)
        {
            _generation++;
            Snapshot = null;
            _pendingDecision = null;
            Error = null;
            Busy = false;
        }
        if (!Busy && (changed || (!context.Running && (previous?.Running == true || previous?.RunVersion != context.RunVersion))))
            await RefreshAsync();
    }

    public async Task RefreshAsync(bool preserveError = false)
    {
        if (_context is null || _disposed) return;
        var conversation = _context.ConversationId;
        var generation = ++_generation;
        try
        {
            var result = await client.ActiveAsync(conversation, _lifetime.Token);
            if (!Current(conversation, generation)) return;
            Snapshot = result;
            if (!preserveError) Error = null;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (Current(conversation, generation)) Error = error.Message; }
    }

    public Task StartAsync(string scenarioId) => MutateAsync((conversation, token) => client.StartAsync(conversation, scenarioId, token));

    public Task DecideAsync(string decision, string? reason)
    {
        if (!CanDecide || Snapshot is not { ProposalHash: not null } snapshot) return Task.CompletedTask;
        if (_pendingDecision is null || _pendingDecision.Revision != snapshot.Revision
            || _pendingDecision.ProposalHash != snapshot.ProposalHash || _pendingDecision.Decision != decision || _pendingDecision.Reason != reason)
            _pendingDecision = new(snapshot.ConversationId, snapshot.Revision, snapshot.ProposalHash, decision, Guid.NewGuid().ToString("n"), reason);
        var request = _pendingDecision;
        return MutateAsync((_, token) => client.DecideAsync(snapshot.Id, request, token));
    }

    public async Task ArchiveAsync(CancellationToken cancellationToken = default)
    {
        if (Busy || Running) throw new InvalidOperationException("Wait for the current operation before resetting the case.");
        if (Snapshot is not { } snapshot) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        Busy = true;
        _generation++;
        try
        {
            await client.ArchiveAsync(snapshot.ConversationId, snapshot.Id, linked.Token);
            if (!_disposed && _context?.ConversationId == snapshot.ConversationId)
            {
                Snapshot = null;
                _pendingDecision = null;
            }
        }
        finally { Busy = false; }
    }

    public void ReportError(string message) => Error = message;

    private async Task MutateAsync(Func<string, CancellationToken, Task<CaseSnapshot>> action)
    {
        if (_context is null || Busy || Running || _disposed) return;
        var conversation = _context.ConversationId;
        var generation = ++_generation;
        Busy = true;
        Error = null;
        try
        {
            var result = await action(conversation, _lifetime.Token);
            if (Current(conversation, generation))
            {
                Snapshot = result;
                _pendingDecision = null;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (Current(conversation, generation))
            {
                Error = error.Message;
                await RefreshAsync(preserveError: true);
            }
        }
        finally { if (!_disposed && _context?.ConversationId == conversation) Busy = false; }
    }

    private bool Current(string conversation, int generation) => !_disposed && _generation == generation && _context?.ConversationId == conversation;

    public void Dispose()
    {
        _disposed = true;
        _generation++;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}