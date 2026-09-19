namespace AgenticLab.Web.Flow;

/// <summary>
/// The Execution explorer's cursor: which captured exchange/stage is pinned (or none while following the
/// live run) and whether the inspector shows the raw payload. Read-only by design — selecting a stage only
/// changes what is displayed, it never re-runs anything.
/// </summary>
internal sealed class ReplayCursor(Action notify)
{
    private string? _exchangeId;
    private int? _sequence;
    private bool _showRaw;

    /// <summary>
    /// Whether the explorer tracks the newest captured stage. Cleared as soon as the user pins a past
    /// exchange or stage, so incoming live events keep recording without stealing the selection.
    /// </summary>
    public bool FollowingLive => _exchangeId is null;

    /// <summary>The pinned exchange, or null while following the newest.</summary>
    public string? ExchangeId => _exchangeId;

    /// <summary>The pinned stage's sequence within its exchange, or null when only an exchange is pinned.</summary>
    public int? Sequence => _sequence;

    /// <summary>Whether the Execution inspector shows the raw captured payload instead of the readable view.</summary>
    public bool ShowRawStage
    {
        get => _showRaw;
        set { _showRaw = value; notify(); }
    }

    /// <summary>Pins one captured stage.</summary>
    public void SelectStage(string exchangeId, int sequence)
    {
        _exchangeId = exchangeId;
        _sequence = sequence;
        notify();
    }

    /// <summary>Pins an exchange, opening it at its first captured stage.</summary>
    public void SelectExchange(string exchangeId)
    {
        _exchangeId = exchangeId;
        _sequence = null;
        notify();
    }

    /// <summary>Returns to following the newest captured stage of the current run.</summary>
    public void FollowLive()
    {
        if (_exchangeId is null && _sequence is null)
        {
            return;
        }

        _exchangeId = null;
        _sequence = null;
        notify();
    }
}
