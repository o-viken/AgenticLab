namespace TheSeries.Web.Flow;

/// <summary>
/// The in-app learning UI state: the master "Learn" switch, the concept open in the right panel and the
/// topic-index filter. Opening a concept also switches the learning UI on and asks the layout to reveal
/// the right panel (via <paramref name="revealRightPanel"/>).
/// </summary>
internal sealed class ConceptDrawer(ConceptCatalog concepts, Action revealRightPanel, Action notify)
{
    private bool _show;
    private Concept? _active;
    private string _filter = string.Empty;

    /// <summary>Master switch for the in-app learning UI; turning it off also closes any open concept.</summary>
    public bool ShowConcepts
    {
        get => _show;
        set
        {
            _show = value;
            if (!value)
            {
                _active = null;
                _filter = string.Empty;
            }

            notify();
        }
    }

    /// <summary>The concept currently shown in the right learning panel, or null when none is selected.</summary>
    public Concept? ActiveConcept => _active;

    /// <summary>Free-text filter applied to the learning panel's topic index (matches title and summary).</summary>
    public string ConceptFilter
    {
        get => _filter;
        set { _filter = value ?? string.Empty; notify(); }
    }

    /// <summary>Opens a concept (a Concepts/&lt;id&gt; folder) in the right panel, making it visible and expanded; ignores unknown ids.</summary>
    public void OpenConcept(string id)
    {
        var concept = concepts.Get(id);
        if (concept is null)
        {
            return;
        }

        _active = concept;
        _show = true;
        revealRightPanel();
        notify();
    }

    /// <summary>Clears the selected concept (the panel falls back to its topic index).</summary>
    public void CloseConcept()
    {
        _active = null;
        notify();
    }
}
