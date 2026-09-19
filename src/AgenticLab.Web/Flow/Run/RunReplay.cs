namespace AgenticLab.Web.Flow;

/// <summary>
/// The Execution explorer's read-only replay: which exchange and stage the cursor selects, stepping
/// between captured stages, and the Context / Prompt signature / A2A snapshots at that stage (built only
/// from what was captured up to it, so a later tool result never leaks into an earlier stage). While
/// following the live run these fall through to the live projections. Nothing here can reach the
/// controller's send/control paths.
/// </summary>
internal sealed class RunReplay(FlowRunController owner)
{
    private ContextSnapshot _replayContext = ContextSnapshot.Empty;
    private PromptSignatureView _replaySignature = PromptSignatureView.Empty;
    private A2AFlowView _replayA2A = A2AFlowView.Empty;
    private int _replayVersion = -1;
    private string? _replayExchange;
    private int? _replaySequence;

    private ReplayCursor Cursor => owner.View.Cursor;

    /// <summary>Whether the explorer is pinned to a captured stage instead of following the live run.</summary>
    public bool Replaying => !Cursor.FollowingLive;

    /// <summary>The exchange the explorer is showing: the pinned one, or the newest.</summary>
    public ExecutionExchange? SelectedExchange
    {
        get
        {
            var all = owner.Projections.Exchanges;
            if (all.Count == 0)
            {
                return null;
            }

            if (Cursor.ExchangeId is { } id)
            {
                foreach (var exchange in all)
                {
                    if (exchange.Id == id)
                    {
                        return exchange;
                    }
                }
            }

            return all[^1];
        }
    }

    /// <summary>The pinned stage, the first stage of a freshly opened exchange, or the newest stage while following live.</summary>
    public FlowEvent? SelectedStage
    {
        get
        {
            if (SelectedExchange is not { Stages.Count: > 0 } exchange)
            {
                return null;
            }

            if (Cursor.Sequence is { } sequence)
            {
                foreach (var stage in exchange.Stages)
                {
                    if (stage.Sequence == sequence)
                    {
                        return stage;
                    }
                }
            }

            return Cursor.FollowingLive ? exchange.Stages[^1] : exchange.Stages[0];
        }
    }

    private int SelectedStageIndex
    {
        get
        {
            if (SelectedExchange is not { } exchange || SelectedStage is not { } stage)
            {
                return -1;
            }

            for (var i = 0; i < exchange.Stages.Count; i++)
            {
                if (exchange.Stages[i].Sequence == stage.Sequence)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    public bool CanStepBack => SelectedStageIndex > 0;

    public bool CanStepForward =>
        SelectedExchange is { } exchange && SelectedStageIndex is var i && i >= 0 && i < exchange.Stages.Count - 1;

    /// <summary>Selects the previous captured stage. Inspection only — it never re-runs anything.</summary>
    public void StepBack() => StepBy(-1);

    /// <summary>Selects the next captured stage. Inspection only — it never re-runs anything.</summary>
    public void StepForward() => StepBy(1);

    private void StepBy(int delta)
    {
        if (SelectedExchange is not { } exchange)
        {
            return;
        }

        var index = SelectedStageIndex + delta;
        if (index < 0 || index >= exchange.Stages.Count)
        {
            return;
        }

        Cursor.SelectStage(exchange.Id, exchange.Stages[index].Sequence);
    }

    /// <summary>The tool call a captured result answers, matched on the model's own call id.</summary>
    public FlowEvent? CallFor(FlowEvent result) =>
        result.CallId is { } id && SelectedExchange is { } exchange
            ? exchange.Stages.FirstOrDefault(s => s.Kind == "tool-call" && s.CallId == id)
            : null;

    /// <summary>Context shown in the anatomy: live content, or the cached prefix at the replay cursor.</summary>
    public ContextSnapshot DisplayContext
    {
        get
        {
            if (!Replaying)
            {
                return owner.Projections.LiveContext;
            }

            EnsureReplayComputed();
            return _replayContext;
        }
    }

    /// <summary>Prompt signature at the replay cursor, or the live signature when following execution.</summary>
    public PromptSignatureView DisplayPromptSignature
    {
        get
        {
            if (!Replaying)
            {
                return owner.Projections.PromptSignature;
            }

            EnsureReplayComputed();
            return _replaySignature;
        }
    }

    /// <summary>The remote-agent topology and delegation state belonging to the displayed exchange.</summary>
    public A2AFlowView DisplayA2A
    {
        get
        {
            if (Replaying)
            {
                EnsureReplayComputed();
                return _replayA2A;
            }

            return string.IsNullOrEmpty(owner.RunExchangeId)
                ? A2AFlowBuilder.Build(owner.Catalogs.KnownA2A, [], false)
                : owner.Projections.LiveA2A;
        }
    }

    // Cache keyed on state version + cursor so cursor moves and incoming events never leave stale content.
    private void EnsureReplayComputed()
    {
        owner.Projections.EnsureComputed();
        var exchange = SelectedExchange;
        var sequence = SelectedStage?.Sequence;
        if (_replayVersion == owner.StateVersion
            && _replayExchange == exchange?.Id
            && _replaySequence == sequence)
        {
            return;
        }

        var exchanges = owner.Projections.Exchanges;
        _replayContext = exchange is null
            ? ContextSnapshot.Empty
            : ExecutionReplayBuilder.ContextAt(exchanges, exchange.Id, sequence);
        _replaySignature = exchange is null
            ? PromptSignatureView.Empty
            : ExecutionReplayBuilder.SignatureAt(exchanges, exchange.Id, sequence);
        _replayA2A = exchange is null ? A2AFlowView.Empty
            : A2AFlowBuilder.Build(exchange.A2AAgents ?? [], ExecutionReplayBuilder.PrefixThrough(exchange, sequence),
                SelectedStage?.Kind is "final" or "error" ||
                (SelectedStage == exchange.Stages.LastOrDefault() && exchange.Status != ExchangeStatus.Running));
        _replayVersion = owner.StateVersion;
        _replayExchange = exchange?.Id;
        _replaySequence = sequence;
    }

    /// <summary>Drops the replay snapshots so captured payloads are released after events/history change.</summary>
    internal void Invalidate()
    {
        _replayContext = ContextSnapshot.Empty;
        _replaySignature = PromptSignatureView.Empty;
        _replayA2A = A2AFlowView.Empty;
    }
}
