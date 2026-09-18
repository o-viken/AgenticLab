namespace TheSeries.Web.Flow;

/// <summary>
/// The flow page's dockable panel layout: whether each side/bottom panel is collapsed to a rail, its dragged
/// size, the bottom panel's momentary maximize state, and the CSS variables that carry those sizes to the
/// body grid. Persisted (except maximize) by the page in localStorage.
/// </summary>
internal sealed class PanelLayout(Action notify, Func<bool> rightPanelEnabled)
{
    /// <summary>The narrowest a side panel may be dragged before it should be collapsed instead.</summary>
    private const int MinPanelWidth = 240;

    /// <summary>The widest a side panel may be dragged.</summary>
    private const int MaxPanelWidth = 640;

    /// <summary>The shortest the bottom panel may be dragged.</summary>
    private const int MinPanelHeight = 120;

    /// <summary>The tallest the bottom panel may be dragged.</summary>
    private const int MaxPanelHeight = 900;

    /// <summary>The width of a collapsed panel's thin rail (matches the CSS rail width).</summary>
    private const int RailWidth = 44;

    /// <summary>The height of the collapsed bottom panel's thin rail (matches the CSS rail height).</summary>
    private const int RailHeight = 40;

    private bool _leftCollapsed;
    private bool _rightCollapsed;
    private int _leftWidth = 276;
    private int _rightWidth = 260;
    private bool _bottomCollapsed;
    // The Execution dock is the main way to read a run, so it opens tall enough for its three panes.
    private int _bottomHeight = 380;
    private bool _bottomMaximized;

    /// <summary>Whether the left (run controls) panel is collapsed to a thin rail.</summary>
    public bool LeftPanelCollapsed
    {
        get => _leftCollapsed;
        set { _leftCollapsed = value; notify(); }
    }

    /// <summary>Whether the right (learning) panel is collapsed to a thin rail.</summary>
    public bool RightPanelCollapsed
    {
        get => _rightCollapsed;
        set { _rightCollapsed = value; notify(); }
    }

    /// <summary>The expanded width (px) of the left panel, clamped to the allowed range.</summary>
    public int LeftPanelWidth
    {
        get => _leftWidth;
        set { _leftWidth = Math.Clamp(value, MinPanelWidth, MaxPanelWidth); notify(); }
    }

    /// <summary>The expanded width (px) of the right panel, clamped to the allowed range.</summary>
    public int RightPanelWidth
    {
        get => _rightWidth;
        set { _rightWidth = Math.Clamp(value, MinPanelWidth, MaxPanelWidth); notify(); }
    }

    /// <summary>Whether the bottom (Execution) panel is collapsed to a thin rail.</summary>
    public bool BottomPanelCollapsed
    {
        get => _bottomCollapsed;
        set { _bottomCollapsed = value; notify(); }
    }

    /// <summary>The expanded height (px) of the bottom panel, clamped to the allowed range.</summary>
    public int BottomPanelHeight
    {
        get => _bottomHeight;
        set { _bottomHeight = Math.Clamp(value, MinPanelHeight, MaxPanelHeight); notify(); }
    }

    /// <summary>
    /// Whether the bottom (Execution) panel is expanded to fill the main column, for reading a large
    /// captured payload. Deliberately not persisted — it is a momentary way to look at something.
    /// </summary>
    public bool BottomPanelMaximized
    {
        get => _bottomMaximized;
        set { _bottomMaximized = value; notify(); }
    }

    /// <summary>Whether the right (learning) panel is rendered at all (gated by the concept switch).</summary>
    public bool RightPanelVisible => rightPanelEnabled();

    /// <summary>The main column body's class, carrying the bottom panel's maximized state.</summary>
    public string MainBodyClass =>
        _bottomMaximized && !_bottomCollapsed ? "main-panel-body bottom-max" : "main-panel-body";

    public void ToggleLeftPanel() { _leftCollapsed = !_leftCollapsed; notify(); }
    public void ToggleRightPanel() { _rightCollapsed = !_rightCollapsed; notify(); }
    public void ToggleBottomPanel() { _bottomCollapsed = !_bottomCollapsed; notify(); }

    /// <summary>Fills the main column with the bottom panel, or restores it to its dragged height.</summary>
    public void ToggleBottomMaximized() { _bottomMaximized = !_bottomMaximized; notify(); }

    /// <summary>Expands the right panel without raising a change (the caller notifies once).</summary>
    internal void RevealRight() => _rightCollapsed = false;

    /// <summary>Restores persisted panel state without raising change notifications (used during initial load).</summary>
    public void Init(bool leftCollapsed, bool rightCollapsed, bool bottomCollapsed, int leftWidth, int rightWidth, int bottomHeight)
    {
        _leftCollapsed = leftCollapsed;
        _rightCollapsed = rightCollapsed;
        _bottomCollapsed = bottomCollapsed;
        _leftWidth = Math.Clamp(leftWidth, MinPanelWidth, MaxPanelWidth);
        _rightWidth = Math.Clamp(rightWidth, MinPanelWidth, MaxPanelWidth);
        _bottomHeight = Math.Clamp(bottomHeight, MinPanelHeight, MaxPanelHeight);
    }

    /// <summary>
    /// Inline CSS custom properties carrying the live panel sizes to the body grid: a collapsed (or hidden)
    /// side panel reports the rail width (or zero) so the centre flow reclaims the space, and the bottom
    /// panel reports its height (or rail height when collapsed).
    /// </summary>
    public string BodyStyle
    {
        get
        {
            var left = _leftCollapsed ? RailWidth : _leftWidth;
            var right = !rightPanelEnabled() ? 0 : _rightCollapsed ? RailWidth : _rightWidth;
            var bottom = _bottomCollapsed ? RailHeight : _bottomHeight;
            return $"--left-w: {left}px; --right-w: {right}px; --bottom-h: {bottom}px;";
        }
    }
}
