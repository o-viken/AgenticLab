namespace AgenticLab.Web.Flow;

/// <summary>Page-wide display preferences, independent of diagram presets and run controls.</summary>
internal sealed class PresentationOptions(Action notify)
{
    private bool _showTokenUsageSummaries;

    /// <summary>Shows reported usage beside conversation replies and token bars without affecting capture or estimates.</summary>
    public bool ShowTokenUsageSummaries
    {
        get => _showTokenUsageSummaries;
        set
        {
            if (_showTokenUsageSummaries == value) return;
            _showTokenUsageSummaries = value;
            notify();
        }
    }
}