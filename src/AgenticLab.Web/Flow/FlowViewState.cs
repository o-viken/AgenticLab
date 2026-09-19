namespace AgenticLab.Web.Flow;

/// <summary>
/// The flow page's <em>view</em> state root: the handful of top-level selections every part of the page
/// keys off (message, agent, workspace, vendor, active tab) plus the feature-scoped
/// collaborators that hold the rest — <see cref="Layout"/> (dockable panels), <see cref="Concepts"/>
/// (the Learn UI), <see cref="Options"/> (per-run stepping/breakpoint/toggle options),
/// <see cref="WorkspacePrefs"/> (persisted workspace preferences), <see cref="Diagram"/> (diagram
/// toggles + pinned token), <see cref="Cursor"/> (the Execution explorer cursor), <see cref="Roster"/>
/// (agents + vendors), <see cref="Agent"/> (values derived from the selected agent) and
/// <see cref="Harness"/> (the harness/LLM node presentation + system prompt). It is deliberately free of
/// any run/execution state and of HTTP or JS dependencies; the live run lives in
/// <see cref="FlowRunController"/>. Every mutation raises <see cref="Changed"/> so the page re-renders.
/// </summary>
internal sealed class FlowViewState
{
    private string _message = string.Empty;
    private string? _selectedAgent;
    private string _workspace = string.Empty;
    private ControlsTab _activeControlsTab = ControlsTab.Chat;
    private Vendor _vendor = Vendor.ChatGpt;

    public FlowViewState(ConceptCatalog concepts)
    {
        // The drawer and the layout reference each other (open concept reveals the panel; the panel is
        // hidden while the drawer is off), so both take callbacks that are only invoked after construction.
        var layout = new PanelLayout(Notify, () => Concepts!.ShowConcepts);
        Details = new HostDetailsSelection(() => { }, Notify);
        Concepts = new ConceptDrawer(concepts, layout.RevealRight, Notify);
        Layout = layout;
        Options = new RunOptions(Notify);
        WorkspacePrefs = new WorkspacePrefs(Notify);
        Diagram = new DiagramOptions(Notify);
        Cursor = new ReplayCursor(Notify);
        Roster = new AgentRoster(this, Notify);
        Agent = new SelectedAgentView(this);
        Harness = new HarnessView(this, Notify);
    }

    /// <summary>Raised whenever a piece of view state changes so the page can re-render.</summary>
    public event Action? Changed;

    private void Notify() => Changed?.Invoke();

    public PanelLayout Layout { get; }
    public ConceptDrawer Concepts { get; }
    public RunOptions Options { get; }
    public WorkspacePrefs WorkspacePrefs { get; }
    public DiagramOptions Diagram { get; }
    public ReplayCursor Cursor { get; }
    public AgentRoster Roster { get; }
    public SelectedAgentView Agent { get; }
    public HarnessView Harness { get; }

    /// <summary>The single read-only host section in its own dock, independent of Learn.</summary>
    public HostDetailsSelection Details { get; }

    /// <summary>Invalidates selection-dependent fetches immediately, including switches away and back.</summary>
    public int ConfigurationVersion { get; private set; }

    /// <summary>The message the user is composing for the next run.</summary>
    public string Message
    {
        get => _message;
        set { _message = value; Notify(); }
    }

    /// <summary>The selected agent; switching it resets the agent-specific tool/skill/instruction toggles.</summary>
    public string? SelectedAgent
    {
        get => _selectedAgent;
        set
        {
            if (_selectedAgent == value)
            {
                return;
            }

            _selectedAgent = value;
            ConfigurationVersion++;
            Options.ResetForAgent();
            Notify();
        }
    }

    /// <summary>Sets the selected agent without raising change side effects (used during initial load).</summary>
    public void InitSelectedAgent(string? agent)
    {
        _selectedAgent = agent;
        ConfigurationVersion++;
    }

    /// <summary>The workspace path for agents that require one.</summary>
    public string Workspace
    {
        get => _workspace;
        set
        {
            if (_workspace == value) return;
            _workspace = value;
            ConfigurationVersion++;
            Notify();
        }
    }

    /// <summary>Which tab is active in the left Controls panel.</summary>
    public ControlsTab ActiveControlsTab
    {
        get => _activeControlsTab;
        set { _activeControlsTab = value; Notify(); }
    }

    /// <summary>Layout follows visible nodes, not the last preset applied.</summary>
    public string DiagramClass => Diagram.ShowModel ? "p-full" : "p-simple";

    /// <summary>The currently selected vendor/brand (persisted by the page in localStorage).</summary>
    public Vendor Vendor
    {
        get => _vendor;
        set
        {
            if (_vendor == value) return;
            _vendor = value;
            ConfigurationVersion++;
            Notify();
        }
    }

    /// <summary>
    /// The harness key sent with a run so the backend swaps in that vendor's harness system prompt (keeping
    /// the agent's persona). Null for a vendor without a backend key.
    /// </summary>
    public string? VendorKey => VendorCatalog.HarnessKey(_vendor);
}
