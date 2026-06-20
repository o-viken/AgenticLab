namespace TheSeries.Web.Flow;

/// <summary>
/// Holds the flow page's <em>view</em> state — the user's preferences and selections that the controls
/// and diagram bind to (theme, perspective, the various toggles, the message/agent/workspace inputs,
/// the stepping mode and delay, the disabled-tool set, the expanded-step set and the concept drawer) —
/// together with the values derived from them. It is deliberately free of any run/execution state and
/// of HTTP or JS dependencies; the live run lives in <see cref="FlowRunController"/>.
/// Mutating a property raises <see cref="Changed"/> so the hosting page can re-render.
/// </summary>
internal sealed class FlowViewState(ConceptCatalog concepts)
{
    private readonly List<AgentInfo> _agents = new();
    private readonly HashSet<string> _disabledTools = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _expanded = new();

    private string _message = string.Empty;
    private string? _selectedAgent;
    private string _workspace = string.Empty;
    private int _stepDelayMs = 600;
    private FlowMode _mode = FlowMode.Auto;
    private FlowLayout _layout = FlowLayout.Stacked;
    private bool _showHarnessBoundary;
    private bool _showAgentBoundary;
    private bool _showEnvironment;
    private bool _showConcepts;
    private bool _expandHarness;
    private bool _conceptPinned;
    private Concept? _activeConcept;
    private Perspective _perspective = Perspective.Expert;
    private Theme _theme = Theme.Default;

    /// <summary>Raised whenever a piece of view state changes so the page can re-render.</summary>
    public event Action? Changed;

    private void Notify() => Changed?.Invoke();

    // --- Agents -----------------------------------------------------------

    /// <summary>The agents the service registered (loaded once on initialise).</summary>
    public IReadOnlyList<AgentInfo> Agents => _agents;

    /// <summary>Replaces the known agents (called after loading them from the service).</summary>
    public void SetAgents(IEnumerable<AgentInfo> agents)
    {
        _agents.Clear();
        _agents.AddRange(agents);
        Notify();
    }

    // --- Run inputs -------------------------------------------------------

    /// <summary>The message the user is composing for the next run.</summary>
    public string Message
    {
        get => _message;
        set { _message = value; Notify(); }
    }

    /// <summary>The selected agent; switching it resets which tools are disabled (names are agent-specific).</summary>
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
            _disabledTools.Clear();
            Notify();
        }
    }

    /// <summary>Sets the selected agent without raising change side effects (used during initial load).</summary>
    public void InitSelectedAgent(string? agent) => _selectedAgent = agent;

    /// <summary>The workspace path for agents that require one.</summary>
    public string Workspace
    {
        get => _workspace;
        set { _workspace = value; Notify(); }
    }

    /// <summary>The auto-mode server-side delay applied before each step, in milliseconds.</summary>
    public int StepDelayMs
    {
        get => _stepDelayMs;
        set { _stepDelayMs = value; Notify(); }
    }

    /// <summary>Whether the run is paced automatically or one step per click.</summary>
    public FlowMode Mode
    {
        get => _mode;
        set { _mode = value; Notify(); }
    }

    /// <summary>How the page arranges the controls relative to the flow (stacked or side by side).</summary>
    public FlowLayout Layout
    {
        get => _layout;
        set { _layout = value; Notify(); }
    }

    // --- Tool toggles -----------------------------------------------------

    /// <summary>Whether the given tool is currently enabled (offered to the model) for the next run.</summary>
    public bool IsToolEnabled(string tool) => !_disabledTools.Contains(tool);

    /// <summary>Switches a tool on or off for the next run.</summary>
    public void SetToolEnabled(string tool, bool enabled)
    {
        if (enabled)
        {
            _disabledTools.Remove(tool);
        }
        else
        {
            _disabledTools.Add(tool);
        }

        Notify();
    }

    /// <summary>The disabled tool names for the next run, or null when none are disabled.</summary>
    public IReadOnlyList<string>? DisabledToolsOrNull =>
        _disabledTools.Count > 0 ? _disabledTools.ToArray() : null;

    // --- Diagram toggles --------------------------------------------------

    /// <summary>Whether the dashed Harness boundary overlay is shown (Expert only).</summary>
    public bool ShowHarnessBoundary
    {
        get => _showHarnessBoundary;
        set { _showHarnessBoundary = value; Notify(); }
    }

    /// <summary>Whether the dashed Agent boundary overlay is shown (Expert only).</summary>
    public bool ShowAgentBoundary
    {
        get => _showAgentBoundary;
        set { _showAgentBoundary = value; Notify(); }
    }

    /// <summary>
    /// When true the diagram surfaces where each node runs, the selected agent's risk level and the
    /// guardrails enforced for it. Available in every perspective.
    /// </summary>
    public bool ShowEnvironment
    {
        get => _showEnvironment;
        set { _showEnvironment = value; Notify(); }
    }

    /// <summary>Whether the Harness node is expanded into its "anatomy" (Expert only).</summary>
    public bool ExpandHarness
    {
        get => _expandHarness;
        set { _expandHarness = value; Notify(); }
    }

    // --- Stepping helper toggles -----------------------------------------

    /// <summary>The level of detail shown in the diagram and Steps list.</summary>
    public Perspective Perspective
    {
        get => _perspective;
        set { _perspective = value; Notify(); }
    }

    /// <summary>The currently selected visual theme (persisted by the page in localStorage).</summary>
    public Theme Theme
    {
        get => _theme;
        set { _theme = value; Notify(); }
    }

    // --- Concept drawer ---------------------------------------------------

    /// <summary>Master switch for the in-app learning UI; turning it off also closes any open drawer.</summary>
    public bool ShowConcepts
    {
        get => _showConcepts;
        set
        {
            _showConcepts = value;
            if (!value)
            {
                _activeConcept = null;
            }

            Notify();
        }
    }

    /// <summary>The learning concept currently shown in the slide-in drawer, or null when it is closed.</summary>
    public Concept? ActiveConcept => _activeConcept;

    /// <summary>When true the drawer docks as a persistent sidebar; when false it floats over the page.</summary>
    public bool ConceptPinned => _conceptPinned;

    /// <summary>Opens the learning drawer for the given concept id (a Concepts/&lt;id&gt; folder); ignores unknown ids.</summary>
    public void OpenConcept(string id)
    {
        _activeConcept = concepts.Get(id);
        Notify();
    }

    /// <summary>Closes the learning drawer.</summary>
    public void CloseConcept()
    {
        _activeConcept = null;
        Notify();
    }

    /// <summary>Toggles between the docked sidebar and the floating overlay presentation of the drawer.</summary>
    public void ToggleConceptPin()
    {
        _conceptPinned = !_conceptPinned;
        Notify();
    }

    // --- Expanded steps ---------------------------------------------------

    /// <summary>Whether the given step's full request/response data panel is shown.</summary>
    public bool IsStepExpanded(int sequence) => _expanded.Contains(sequence);

    /// <summary>Toggles whether a step's full request/response data panel is shown.</summary>
    public void ToggleStep(int sequence)
    {
        if (!_expanded.Remove(sequence))
        {
            _expanded.Add(sequence);
        }

        Notify();
    }

    /// <summary>Clears the expanded-step set (called when a new run starts).</summary>
    public void ClearExpanded() => _expanded.Clear();

    // --- Derived view values ---------------------------------------------

    /// <summary>The CSS class applied to the diagram so the grid layout matches the selected perspective.</summary>
    public string PerspectiveClass => _perspective switch
    {
        Perspective.NonTechnical => "p-nontech",
        Perspective.Technical => "p-mid",
        _ => "p-full",
    };

    /// <summary>The CSS class applied to the page body so the controls sit beside the flow in the split layout.</summary>
    public string LayoutClass => _layout == FlowLayout.Split ? "layout-split" : string.Empty;

    /// <summary>The CSS class applied to the root so the brand theme's variable overrides take effect.</summary>
    public string ThemeClass => ThemeCatalog.ThemeClass(_theme);

    /// <summary>The friendly display name of the selected theme (shown in the page title and header).</summary>
    public string ThemeName => ThemeCatalog.ThemeName(_theme);

    /// <summary>Reserves room on the right for the docked concept sidebar (only when a concept is open and pinned).</summary>
    public string PinnedClass => _activeConcept is not null && _conceptPinned ? "drawer-pinned" : string.Empty;

    /// <summary>
    /// The agents offered by the current theme, restricted to those the service actually registered
    /// (an unknown name in the roster is silently skipped). Order follows the roster.
    /// </summary>
    public IReadOnlyList<AgentChoice> AvailableAgents =>
        (ThemeCatalog.ThemeAgents.TryGetValue(_theme, out var roster) ? roster : Array.Empty<AgentChoice>())
            .Where(c => _agents.Any(a => string.Equals(a.Name, c.Name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

    /// <summary>The current theme's default (first available) agent, or null when the theme offers none.</summary>
    public string? ThemeDefaultAgent => AvailableAgents.Count > 0 ? AvailableAgents[0].Name : null;

    /// <summary>
    /// The label shown on the merged Client + AiService node: the brand theme name when one is picked,
    /// otherwise the perspective's standard label (Harness in Expert, Application elsewhere).
    /// </summary>
    public string HarnessLabel => _theme == Theme.Default
        ? (_perspective == Perspective.Expert ? "Harness" : "Application")
        : ThemeName;

    private AgentInfo? Selected => _agents.FirstOrDefault(a => a.Name == _selectedAgent);

    /// <summary>Whether the currently selected agent requires a workspace path before it can run.</summary>
    public bool SelectedAgentRequiresWorkspace => Selected?.RequiresWorkspace ?? false;

    /// <summary>Whether the currently selected agent uses workspace skills, so the harness Skills box is shown.</summary>
    public bool SelectedAgentSupportsSkills => Selected?.SupportsSkills ?? false;

    /// <summary>The selected agent's risk level (None/Low/Medium/High), surfaced in the environment &amp; risk view.</summary>
    public string SelectedAgentRiskLevel => Selected?.RiskLevel ?? "None";

    /// <summary>The guardrails enforced for the selected agent, shown as chips in the environment &amp; risk view.</summary>
    public IReadOnlyList<string> SelectedAgentGuardrails => Selected?.Guardrails ?? Array.Empty<string>();

    /// <summary>The CSS modifier for the risk meter, matching the level (risk-none/-low/-medium/-high).</summary>
    public string RiskLevelClass => $"risk-{SelectedAgentRiskLevel.ToLowerInvariant()}";

    /// <summary>How many of the three risk-meter segments are filled for the selected agent.</summary>
    public int RiskFilledSegments => ThemeCatalog.RiskFilledSegments(SelectedAgentRiskLevel);

    /// <summary>
    /// The label on the merged node's environment badge: workspace agents run on your machine with file
    /// and shell access; the rest run as a server process with no access to your local files or shell.
    /// </summary>
    public string HarnessEnvLabel => SelectedAgentRequiresWorkspace
        ? "💻 Your machine — file + shell access"
        : "🖧 Server process — no local access";

    /// <summary>The tooltip explaining the merged node's environment badge.</summary>
    public string HarnessEnvTitle => SelectedAgentRequiresWorkspace
        ? "This agent runs on your behalf on this machine, with access to the workspace files and the shell."
        : "This agent runs as a service process; it has no access to your local files or shell.";

    /// <summary>The badge colour class: amber for an agent with local machine reach, neutral otherwise.</summary>
    public string EnvBadgeClass => SelectedAgentRequiresWorkspace ? "env-machine" : "env-server";

    /// <summary>The tools available to the currently selected agent, shown in the harness Tools box.</summary>
    public string SelectedAgentTools
    {
        get
        {
            var tools = Selected?.Tools;
            return tools is { Count: > 0 } ? string.Join(" · ", tools) : "—";
        }
    }

    /// <summary>The full tool list of the selected agent, used to render the per-tool on/off checkboxes.</summary>
    public IReadOnlyList<string> SelectedAgentToolNames => Selected?.Tools ?? Array.Empty<string>();

    /// <summary>The selected agent's persona/description, shown in the harness anatomy view.</summary>
    public string SelectedAgentDescription => Selected?.Description ?? "—";

    /// <summary>
    /// The subtitle of the merged Client + AiService node. Non-technical states the grouping plainly;
    /// Technical/Expert name the agent running inside it.
    /// </summary>
    public string HarnessSubtitle => _perspective switch
    {
        Perspective.NonTechnical => "Client + AiService",
        Perspective.Technical => $"Client + AiService · {_selectedAgent ?? "Agent"}",
        _ => $"AiService · {_selectedAgent ?? "Agent"}",
    };

    /// <summary>
    /// Filters a flow's events down to those the current perspective shows: Expert shows everything,
    /// Technical hides the tool-call/result detail, NonTechnical keeps only the message in and the
    /// final answer out (plus errors).
    /// </summary>
    public IReadOnlyList<FlowEvent> VisibleEvents(IEnumerable<FlowEvent> events) => _perspective switch
    {
        Perspective.NonTechnical => events.Where(e => e.Kind is "received" or "final" or "error").ToList(),
        Perspective.Technical => events.Where(e => e.Kind is not "tool-call" and not "tool-result").ToList(),
        _ => events as IReadOnlyList<FlowEvent> ?? events.ToList(),
    };
}
