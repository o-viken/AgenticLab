namespace TheSeries.Web.Flow;

/// <summary>
/// Holds the flow page's <em>view</em> state — the user's preferences and selections that the controls
/// and diagram bind to (vendor, perspective, the various toggles, the message/agent/workspace inputs,
/// the stepping mode and delay, the disabled-tool set, the expanded-step set and the concept drawer) —
/// together with the values derived from them. It is deliberately free of any run/execution state and
/// of HTTP or JS dependencies; the live run lives in <see cref="FlowRunController"/>.
/// Mutating a property raises <see cref="Changed"/> so the hosting page can re-render.
/// </summary>
internal sealed class FlowViewState(ConceptCatalog concepts)
{
    private readonly List<AgentInfo> _agents = new();
    private readonly List<AgentInfo> _workspaceAgents = new();
    private readonly Dictionary<string, VendorInfo> _vendors = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _disabledTools = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _expanded = new();

    /// <summary>The narrowest a side panel may be dragged before it should be collapsed instead.</summary>
    private const int MinPanelWidth = 240;

    /// <summary>The widest a side panel may be dragged.</summary>
    private const int MaxPanelWidth = 640;

    /// <summary>The shortest the bottom panel may be dragged.</summary>
    private const int MinPanelHeight = 120;

    /// <summary>The tallest the bottom panel may be dragged.</summary>
    private const int MaxPanelHeight = 600;

    /// <summary>The width of a collapsed panel's thin rail (matches the CSS rail width).</summary>
    private const int RailWidth = 44;

    /// <summary>The height of the collapsed bottom panel's thin rail (matches the CSS rail height).</summary>
    private const int RailHeight = 40;

    private string _message = string.Empty;
    private string? _selectedAgent;
    private string _workspace = string.Empty;
    private int _stepDelayMs = 600;
    private FlowMode _mode = FlowMode.Auto;
    private ControlsTab _activeControlsTab = ControlsTab.Chat;
    private bool _outputCollapsed = true;
    private bool _leftPanelCollapsed;
    private bool _rightPanelCollapsed;
    private int _leftPanelWidth = 360;
    private int _rightPanelWidth = 380;
    private bool _bottomPanelCollapsed;
    private int _bottomPanelHeight = 240;
    private bool _showHarnessBoundary;
    private bool _showAgentBoundary;
    private bool _showEnvironment;
    private bool _showPromptSignature;
    private bool _promptSignatureDelta;
    private bool _showInferenceView;
    private bool _showEmbeddingsView;
    private bool _showNetworkView;
    private string? _selectedToken;
    private bool _showConcepts;
    private bool _expandHarness;
    private bool _showFullHarnessPrompt;
    private string _harnessPromptText = string.Empty;
    private Concept? _activeConcept;
    private string _conceptFilter = string.Empty;
    private Perspective _perspective = Perspective.Simple;
    private Vendor _vendor = Vendor.ChatGpt;

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

    /// <summary>The user-authored agents discovered in the active workspace's agents/ folder.</summary>
    public IReadOnlyList<AgentInfo> WorkspaceAgents => _workspaceAgents;

    /// <summary>Replaces the workspace-discovered agents (called when the workspace changes).</summary>
    public void SetWorkspaceAgents(IEnumerable<AgentInfo> agents)
    {
        _workspaceAgents.Clear();
        _workspaceAgents.AddRange(agents);
        Notify();
    }

    // --- Vendors ----------------------------------------------------------

    /// <summary>
    /// Replaces the brand-vendor metadata loaded from the service (keyed by the backend vendor key).
    /// Used to build the vendor picker's roster and the simulated model label without hard-coding them.
    /// </summary>
    public void SetVendors(IEnumerable<VendorInfo> vendors)
    {
        _vendors.Clear();
        foreach (var vendor in vendors)
        {
            _vendors[vendor.Key] = vendor;
        }

        Notify();
    }

    /// <summary>The loaded metadata for the selected brand vendor, or null for the Default vendor or before load.</summary>
    private VendorInfo? CurrentVendorInfo =>
        VendorKey is { } key && _vendors.TryGetValue(key, out var info) ? info : null;

    /// <summary>
    /// The loaded metadata for the given vendor (display name, simulated model label and modes), or null
    /// before load. Used by the vendor rail to fill each icon's hover tooltip without re-selecting it.
    /// </summary>
    public VendorInfo? VendorInfoFor(Vendor vendor) =>
        VendorCatalog.HarnessKey(vendor) is { } key && _vendors.TryGetValue(key, out var info) ? info : null;

    /// <summary>The friendly display name of the given vendor, falling back to its enum name before metadata loads.</summary>
    public string VendorDisplayName(Vendor vendor) => VendorInfoFor(vendor)?.DisplayName ?? vendor.ToString();


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

    /// <summary>Which tab is active in the left Controls panel (Chat, Settings, History, Workspace or Telemetry).</summary>
    public ControlsTab ActiveControlsTab
    {
        get => _activeControlsTab;
        set { _activeControlsTab = value; Notify(); }
    }

    /// <summary>
    /// Whether the Steps/Reply output panel below the diagram is collapsed. It auto-collapses in the
    /// Simple detail level (where the focus is the high-level diagram) and expands in the other levels,
    /// but the user can still toggle it manually within a level.
    /// </summary>
    public bool OutputCollapsed
    {
        get => _outputCollapsed;
        set { _outputCollapsed = value; Notify(); }
    }

    /// <summary>Collapses or expands the Steps/Reply output panel.</summary>
    public void ToggleOutput() { _outputCollapsed = !_outputCollapsed; Notify(); }

    // --- Side panels ------------------------------------------------------

    /// <summary>Whether the left (run controls) panel is collapsed to a thin rail.</summary>
    public bool LeftPanelCollapsed
    {
        get => _leftPanelCollapsed;
        set { _leftPanelCollapsed = value; Notify(); }
    }

    /// <summary>Whether the right (learning) panel is collapsed to a thin rail.</summary>
    public bool RightPanelCollapsed
    {
        get => _rightPanelCollapsed;
        set { _rightPanelCollapsed = value; Notify(); }
    }

    /// <summary>The expanded width (px) of the left panel, clamped to the allowed range.</summary>
    public int LeftPanelWidth
    {
        get => _leftPanelWidth;
        set { _leftPanelWidth = Math.Clamp(value, MinPanelWidth, MaxPanelWidth); Notify(); }
    }

    /// <summary>The expanded width (px) of the right panel, clamped to the allowed range.</summary>
    public int RightPanelWidth
    {
        get => _rightPanelWidth;
        set { _rightPanelWidth = Math.Clamp(value, MinPanelWidth, MaxPanelWidth); Notify(); }
    }

    /// <summary>Whether the bottom (conversation) panel is collapsed to a thin rail.</summary>
    public bool BottomPanelCollapsed
    {
        get => _bottomPanelCollapsed;
        set { _bottomPanelCollapsed = value; Notify(); }
    }

    /// <summary>The expanded height (px) of the bottom panel, clamped to the allowed range.</summary>
    public int BottomPanelHeight
    {
        get => _bottomPanelHeight;
        set { _bottomPanelHeight = Math.Clamp(value, MinPanelHeight, MaxPanelHeight); Notify(); }
    }

    /// <summary>Whether the right (learning) panel is rendered at all (gated by the concept switch).</summary>
    public bool RightPanelVisible => _showConcepts;

    /// <summary>Collapses or expands the left panel.</summary>
    public void ToggleLeftPanel() { _leftPanelCollapsed = !_leftPanelCollapsed; Notify(); }

    /// <summary>Collapses or expands the right panel.</summary>
    public void ToggleRightPanel() { _rightPanelCollapsed = !_rightPanelCollapsed; Notify(); }

    /// <summary>Collapses or expands the bottom panel.</summary>
    public void ToggleBottomPanel() { _bottomPanelCollapsed = !_bottomPanelCollapsed; Notify(); }

    /// <summary>Restores persisted panel state without raising change notifications (used during initial load).</summary>
    public void InitPanels(bool leftCollapsed, bool rightCollapsed, bool bottomCollapsed, int leftWidth, int rightWidth, int bottomHeight)
    {
        _leftPanelCollapsed = leftCollapsed;
        _rightPanelCollapsed = rightCollapsed;
        _bottomPanelCollapsed = bottomCollapsed;
        _leftPanelWidth = Math.Clamp(leftWidth, MinPanelWidth, MaxPanelWidth);
        _rightPanelWidth = Math.Clamp(rightWidth, MinPanelWidth, MaxPanelWidth);
        _bottomPanelHeight = Math.Clamp(bottomHeight, MinPanelHeight, MaxPanelHeight);
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
            var left = _leftPanelCollapsed ? RailWidth : _leftPanelWidth;
            var right = !_showConcepts ? 0 : _rightPanelCollapsed ? RailWidth : _rightPanelWidth;
            var bottom = _bottomPanelCollapsed ? RailHeight : _bottomPanelHeight;
            return $"--left-w: {left}px; --right-w: {right}px; --bottom-h: {bottom}px;";
        }
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

    /// <summary>Whether the prompt-signature panel (request composition by category) is shown (Expert only).</summary>
    public bool ShowPromptSignature
    {
        get => _showPromptSignature;
        set { _showPromptSignature = value; Notify(); }
    }

    /// <summary>
    /// Whether the prompt-signature panel shows its delta ("growth") view — one bar per conversation
    /// exchange splitting reused-prefix from newly-added chars — instead of the default comparison.
    /// </summary>
    public bool PromptSignatureDelta
    {
        get => _promptSignatureDelta;
        set { _promptSignatureDelta = value; Notify(); }
    }

    /// <summary>
    /// Whether the simulated inference panel (the latest prompt tokenized + the answer replayed as
    /// autoregressively generated tokens) is shown (Expert only). The tokens are a client-side fabrication.
    /// </summary>
    public bool ShowInferenceView
    {
        get => _showInferenceView;
        set { _showInferenceView = value; Notify(); }
    }

    /// <summary>
    /// Whether the simulated embeddings panel (prompt tokens as fake vectors and a 2-D meaning map) is shown
    /// (Expert only). The vectors are a client-side fabrication.
    /// </summary>
    public bool ShowEmbeddingsView
    {
        get => _showEmbeddingsView;
        set { _showEmbeddingsView = value; Notify(); }
    }

    /// <summary>
    /// Whether the simulated neural-network panel (a symbolic forward pass that consumes the embedding
    /// vectors) is shown (Expert only). The network is a client-side fabrication.
    /// </summary>
    public bool ShowNetworkView
    {
        get => _showNetworkView;
        set { _showNetworkView = value; Notify(); }
    }

    /// <summary>
    /// The token the user has "pinned" (by clicking it in the Inference or Embeddings panels), normalised to
    /// lower-cased + trimmed so the same word matches across panels. When set, that token is cross-highlighted
    /// everywhere it appears and its fabricated vector/coordinates are shown numerically and fed into the
    /// symbolic network's input layer — linking the tokenizer, the embedding and the forward pass. Null when
    /// nothing is selected.
    /// </summary>
    public string? SelectedToken => _selectedToken;

    /// <summary>
    /// Toggles the pinned token: clicking the already-selected token clears the selection, otherwise selects
    /// the (normalised) token. Blank/whitespace tokens are ignored (they carry no embedding).
    /// </summary>
    public void SelectToken(string? text)
    {
        var key = string.IsNullOrWhiteSpace(text) ? null : text.Trim().ToLowerInvariant();
        _selectedToken = (key is not null && key == _selectedToken) ? null : key;
        Notify();
    }

    /// <summary>Whether <paramref name="text"/> is the currently pinned token (case/whitespace-insensitive).</summary>
    public bool IsTokenSelected(string? text)
        => _selectedToken is not null
           && !string.IsNullOrWhiteSpace(text)
           && string.Equals(text.Trim().ToLowerInvariant(), _selectedToken, StringComparison.Ordinal);

    // --- Stepping helper toggles -----------------------------------------

    /// <summary>The level of detail shown in the diagram and Steps list.</summary>
    public Perspective Perspective
    {
        get => _perspective;
        set
        {
            _perspective = value;
            // The Steps/Reply detail is noise in the Simple level, so collapse it there and reveal it elsewhere.
            _outputCollapsed = value == Perspective.Simple;
            Notify();
        }
    }

    /// <summary>The currently selected vendor/brand (persisted by the page in localStorage).</summary>
    public Vendor Vendor
    {
        get => _vendor;
        set { _vendor = value; Notify(); }
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
                _conceptFilter = string.Empty;
            }

            Notify();
        }
    }

    /// <summary>The learning concept currently shown in the right learning panel, or null when none is selected.</summary>
    public Concept? ActiveConcept => _activeConcept;

    /// <summary>Free-text filter applied to the learning panel's topic index (matches concept title and summary).</summary>
    public string ConceptFilter
    {
        get => _conceptFilter;
        set { _conceptFilter = value ?? string.Empty; Notify(); }
    }

    /// <summary>
    /// Opens the given concept (a Concepts/&lt;id&gt; folder) in the right learning panel, making sure that
    /// panel is visible and expanded; ignores unknown ids.
    /// </summary>
    public void OpenConcept(string id)
    {
        var concept = concepts.Get(id);
        if (concept is null)
        {
            return;
        }

        _activeConcept = concept;
        _showConcepts = true;
        _rightPanelCollapsed = false;
        Notify();
    }

    /// <summary>Clears the selected concept (the learning panel falls back to its hint + legend).</summary>
    public void CloseConcept()
    {
        _activeConcept = null;
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
        Perspective.Simple => "p-simple",
        Perspective.NonTechnical => "p-nontech",
        Perspective.Technical => "p-mid",
        _ => "p-full",
    };

    /// <summary>The CSS class applied to the root so the vendor's brand palette overrides take effect.</summary>
    public string VendorClass => VendorCatalog.CssClass(_vendor);

    /// <summary>The friendly display name of the selected vendor (shown in the page title and header).</summary>
    public string VendorName => CurrentVendorInfo?.DisplayName ?? _vendor.ToString();

    /// <summary>
    /// The harness key sent with a run so the backend swaps in that vendor's harness system prompt
    /// (replacing the shared harness while keeping the agent's persona). Null for the Default vendor.
    /// </summary>
    public string? VendorKey => VendorCatalog.HarnessKey(_vendor);

    /// <summary>
    /// The body text describing the System Prompt layer in the harness anatomy. For a brand vendor it names
    /// the vendor whose harness prompt replaces the shared one for the run; for the Default vendor it
    /// describes the standard shared harness.
    /// </summary>
    public string HarnessPromptBody => _vendor == Vendor.Default
        ? "Global harness prompt — operating rules + tool loop (expert coding assistant++)"
        : $"{VendorName} system prompt — replaces the shared harness for this run (persona kept)";

    /// <summary>The actual harness (system) prompt text for the selected vendor and agent, fetched from the service.</summary>
    public string HarnessPromptText => _harnessPromptText;

    /// <summary>Whether the harness anatomy shows the full system prompt text (vs. a short preview).</summary>
    public bool ShowFullHarnessPrompt
    {
        get => _showFullHarnessPrompt;
        set { _showFullHarnessPrompt = value; Notify(); }
    }

    /// <summary>Whether a fetched system prompt is available to show in the harness anatomy.</summary>
    public bool HasHarnessPromptText => !string.IsNullOrWhiteSpace(_harnessPromptText);

    /// <summary>
    /// The system prompt as shown in the anatomy's System Prompt box: the fetched text (a short preview, or
    /// the full text when expanded), or the descriptive fallback when no text has been fetched yet.
    /// </summary>
    public string HarnessPromptDisplay
    {
        get
        {
            if (!HasHarnessPromptText)
            {
                return HarnessPromptBody;
            }

            var text = _harnessPromptText.Trim();
            if (_showFullHarnessPrompt || text.Length <= 160)
            {
                return text;
            }

            return text[..160].TrimEnd() + "…";
        }
    }

    /// <summary>
    /// Replaces the fetched harness prompt text (called after the vendor or agent changes), collapsing
    /// any expanded full view so the preview reflects the new prompt.
    /// </summary>
    public void SetHarnessPrompt(string? prompt)
    {
        _harnessPromptText = prompt ?? string.Empty;
        _showFullHarnessPrompt = false;
        Notify();
    }

    /// <summary>
    /// The agents offered by the current vendor, restricted to those the service actually registered
    /// (an unknown name in the roster is silently skipped). Order follows the roster. The modes come from
    /// the vendor's loaded metadata (including the non-brand Default vendor).
    /// </summary>
    public IReadOnlyList<AgentChoice> AvailableAgents =>
        (CurrentVendorInfo?.Modes.Select(m => new AgentChoice(m.Agent, m.Label)) ?? Enumerable.Empty<AgentChoice>())
            .Where(c => _agents.Any(a => string.Equals(a.Name, c.Name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

    /// <summary>The current vendor's default (first available) agent, or null when the vendor offers none.</summary>
    public string? VendorDefaultAgent => AvailableAgents.Count > 0 ? AvailableAgents[0].Name : null;

    /// <summary>
    /// Whether the current vendor offers a workspace-requiring agent, so workspace-discovered agents are
    /// relevant and should be fetched and appended to the picker.
    /// </summary>
    public bool VendorHasWorkspaceAgent =>
        AvailableAgents.Any(c => _agents.Any(a =>
            string.Equals(a.Name, c.Name, StringComparison.OrdinalIgnoreCase) && a.RequiresWorkspace));

    /// <summary>
    /// The workspace-discovered agents as picker choices (label = name), appended to the dropdown after a
    /// separator. Empty unless the current vendor offers a workspace agent and some were discovered.
    /// </summary>
    public IReadOnlyList<AgentChoice> WorkspaceAgentChoices =>
        VendorHasWorkspaceAgent
            ? _workspaceAgents.Select(a => new AgentChoice(a.Name, a.Name)).ToList()
            : Array.Empty<AgentChoice>();

    /// <summary>
    /// The label shown on the merged Client + AiService node: the brand vendor name when one is picked,
    /// otherwise the perspective's standard label (Harness in Expert, Application elsewhere).
    /// </summary>
    public string HarnessLabel => _vendor == Vendor.Default
        ? (_perspective == Perspective.Expert ? "Harness" : "Application")
        : VendorName;

    /// <summary>
    /// The provider/model label shown on the LLM node. In the Default vendor or the Expert perspective the
    /// real Azure OpenAI deployment is shown (so engineers see the actual model); in a brand vendor's other
    /// perspectives a simulated provider label is shown instead (e.g. Claude on Anthropic), to mimic that
    /// the product runs on its own model even though the backend is always Azure OpenAI.
    /// </summary>
    public string LlmModelLabel
    {
        get
        {
            var simulated = CurrentVendorInfo?.ModelLabel ?? string.Empty;
            if (_vendor != Vendor.Default && _perspective != Perspective.Expert && simulated.Length > 0)
            {
                return simulated;
            }

            var deployment = Selected?.ModelId;
            return string.IsNullOrWhiteSpace(deployment) ? "Azure OpenAI" : $"Azure OpenAI · {deployment}";
        }
    }

    private AgentInfo? Selected =>
        _agents.FirstOrDefault(a => a.Name == _selectedAgent)
        ?? _workspaceAgents.FirstOrDefault(a => a.Name == _selectedAgent);

    /// <summary>Whether the currently selected agent requires a workspace path before it can run.</summary>
    public bool SelectedAgentRequiresWorkspace => Selected?.RequiresWorkspace ?? false;

    /// <summary>Whether the currently selected agent uses workspace skills, so the harness Skills box is shown.</summary>
    public bool SelectedAgentSupportsSkills => Selected?.SupportsSkills ?? false;

    /// <summary>
    /// Whether the currently selected agent picks up the workspace's custom instructions. These apply to
    /// any agent that runs with a workspace, so the harness anatomy's Custom Instructions box can show
    /// when instructions are present.
    /// </summary>
    public bool SelectedAgentSupportsInstructions => Selected?.RequiresWorkspace ?? false;

    /// <summary>The selected agent's risk level (None/Low/Medium/High), surfaced in the environment &amp; risk view.</summary>
    public string SelectedAgentRiskLevel => Selected?.RiskLevel ?? "None";

    /// <summary>The guardrails enforced for the selected agent, shown as chips in the environment &amp; risk view.</summary>
    public IReadOnlyList<string> SelectedAgentGuardrails => Selected?.Guardrails ?? Array.Empty<string>();

    /// <summary>The CSS modifier for the risk meter, matching the level (risk-none/-low/-medium/-high).</summary>
    public string RiskLevelClass => $"risk-{SelectedAgentRiskLevel.ToLowerInvariant()}";

    /// <summary>How many of the three risk-meter segments are filled for the selected agent.</summary>
    public int RiskFilledSegments => VendorCatalog.RiskFilledSegments(SelectedAgentRiskLevel);

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

    /// <summary>A compact count of the selected agent's enabled tools, shown in the harness Tools box
    /// (the full names would grow the node too much). Reads e.g. "6 tools" or "4 of 6 tools" when some
    /// are toggled off, and "—" when the agent has none.</summary>
    public string SelectedAgentTools
    {
        get
        {
            var tools = Selected?.Tools;
            if (tools is not { Count: > 0 })
            {
                return "—";
            }

            var total = tools.Count;
            var enabled = tools.Count(IsToolEnabled);
            var label = total == 1 ? "tool" : "tools";
            return enabled == total ? $"{total} {label}" : $"{enabled} of {total} {label}";
        }
    }

    /// <summary>The full tool list of the selected agent, used to render the per-tool on/off checkboxes.</summary>
    public IReadOnlyList<string> SelectedAgentToolNames => Selected?.Tools ?? Array.Empty<string>();

    /// <summary>The CSS risk class (<c>risk-low</c>/<c>risk-medium</c>/<c>risk-high</c>) for a tool, so the
    /// harness can colour-code tools by how much they can do rather than showing them as a flat list.</summary>
    public string ToolRiskClass(string tool) => ToolRiskCatalog.CssClass(tool);

    /// <summary>A tooltip explaining a tool's risk tier and why (e.g. read-only vs. runs commands on the host).</summary>
    public string ToolRiskTitle(string tool) => $"{tool} — {ToolRiskCatalog.Reason(tool)}";


    /// <summary>The selected agent's persona/description, shown in the harness anatomy view.</summary>
    public string SelectedAgentDescription => Selected?.Description ?? "—";

    /// <summary>
    /// The subtitle of the merged Client + AiService node. Non-technical states the grouping plainly;
    /// Technical/Expert name the agent running inside it.
    /// </summary>
    public string HarnessSubtitle => _perspective switch
    {
        Perspective.Simple => "Service",
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
        Perspective.Simple => events.Where(e => e.Kind is "received" or "final" or "error").ToList(),
        Perspective.NonTechnical => events.Where(e => e.Kind is "received" or "final" or "error").ToList(),
        Perspective.Technical => events.Where(e => e.Kind is not "tool-call" and not "tool-result").ToList(),
        _ => events as IReadOnlyList<FlowEvent> ?? events.ToList(),
    };
}
