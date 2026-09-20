namespace AgenticLab.Web.Flow;

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

    private const int MaxConversationWidth = 960;

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
    private bool _adaptiveConversationWidth = true;
    private bool _bottomCollapsed;
    private int _bottomHeight = 240;
    private bool _bottomMaximized;
    private bool _discoveryOpen;

    /// <summary>Transient modal visibility; changing it never replaces the page or its conversation.</summary>
    public bool DiscoveryOpen
    {
        get => _discoveryOpen;
        set { _discoveryOpen = value; notify(); }
    }

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
        set { _leftWidth = Math.Clamp(value, MinPanelWidth, MaxConversationWidth); _adaptiveConversationWidth = false; notify(); }
    }

    /// <summary>Uses a proportional conversation column until the user explicitly resizes it.</summary>
    public bool AdaptiveConversationWidth => _adaptiveConversationWidth;

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

    /// <summary>Whether the independent Learn dock is enabled.</summary>
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
    public void Init(bool leftCollapsed, bool rightCollapsed, bool bottomCollapsed, int leftWidth, int rightWidth, int bottomHeight,
        bool adaptiveConversationWidth = false)
    {
        _leftCollapsed = leftCollapsed;
        _rightCollapsed = rightCollapsed;
        _bottomCollapsed = bottomCollapsed;
        _leftWidth = Math.Clamp(leftWidth, MinPanelWidth, MaxConversationWidth);
        _rightWidth = Math.Clamp(rightWidth, MinPanelWidth, MaxPanelWidth);
        _bottomHeight = Math.Clamp(bottomHeight, MinPanelHeight, MaxPanelHeight);
        _adaptiveConversationWidth = adaptiveConversationWidth;
    }

    /// <summary>Restores the proportional workspace without changing run state or independent dock selections.</summary>
    public void Reset()
    {
        Init(false, false, false, 276, 260, 240, adaptiveConversationWidth: true);
        _bottomMaximized = false;
        notify();
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
            var left = _leftCollapsed ? $"{RailWidth}px" : _adaptiveConversationWidth
                ? "minmax(0, 1.18fr)"
                : $"minmax(0, min({_leftWidth}px, 65cqw))";
            var right = !rightPanelEnabled() ? 0 : _rightCollapsed ? RailWidth : _rightWidth;
            var bottom = _bottomCollapsed ? RailHeight : _bottomHeight;
            return $"--left-w: {left}; --right-w: {right}px; --bottom-h: {bottom}px;";
        }
    }
}
