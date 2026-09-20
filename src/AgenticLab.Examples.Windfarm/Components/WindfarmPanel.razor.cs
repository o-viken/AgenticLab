using AgenticLab.Extensibility.Examples;
using AgenticLab.Examples.Windfarm.Process;
using Microsoft.AspNetCore.Components;

namespace AgenticLab.Examples.Windfarm.Components;

/// <summary>Windfarm-only presentation and case lifecycle, independent of the hosting page's state types.</summary>
public partial class WindfarmPanel
{
    [Inject] private IWindfarmClient Client { get; set; } = default!;
    /// <summary>The current host-page snapshot and its narrowly scoped commands.</summary>
    [Parameter, EditorRequired] public ExamplePanelContext Context { get; set; } = default!;
    private WindfarmCaseController _state = default!;
    private string _scenario = "inspection";
    private string _reason = string.Empty;
    private bool _disposed;
    private bool Locked => _state.Busy || Context.Running;

    protected override async Task OnInitializedAsync()
    {
        _state = new WindfarmCaseController(Client);
        await _state.InitializeAsync();
    }

    protected override Task OnParametersSetAsync() => _state.SetContextAsync(Context);

    private async Task StartAsync()
    {
        if (Locked) return;
        try
        {
            if (_state.Snapshot is not null)
            {
                var conversationId = await Context.NewConversation();
                await _state.SetContextAsync(Context with { ConversationId = conversationId, RunVersion = 0 });
            }
            await _state.StartAsync(_scenario);
            if (!_disposed && _state.Snapshot is not null)
            {
                _reason = string.Empty;
                Context.SetDraft("Investigate the WT-07 gearbox alarm, compare feasible maintenance windows, consult all three specialists, and prepare a work order for my review.");
            }
        }
        catch (Exception error) { if (!_disposed) _state.ReportError(error.Message); }
    }

    private Task RefreshAsync() => _state.RefreshAsync();
    private Task ApproveAsync() => _state.DecideAsync("approve", _reason);
    private Task RejectAsync() => _state.DecideAsync("reject", _reason);

    /// <inheritdoc />
    public Task BeforeResetAsync(CancellationToken cancellationToken = default) => _state.ArchiveAsync(cancellationToken);

    private static string StageLabel(CaseStage stage) => stage switch
    {
        CaseStage.AwaitingApproval => "Awaiting human approval",
        CaseStage.WorkOrderCreated => "Order recorded",
        CaseStage.Blocked => "Evidence hold",
        _ => stage.ToString(),
    };

    private static string StageTone(CaseStage stage) => stage switch
    {
        CaseStage.WorkOrderCreated => "success",
        CaseStage.AwaitingApproval or CaseStage.Blocked => "warning",
        CaseStage.Rejected => "danger",
        _ => "neutral",
    };

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        _state.Dispose();
    }
}