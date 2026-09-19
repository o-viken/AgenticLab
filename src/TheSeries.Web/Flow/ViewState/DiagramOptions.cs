namespace TheSeries.Web.Flow;

/// <summary>
/// The diagram's view toggles — the boundary overlays, the environment &amp; risk view, the expanded
/// harness anatomy and teaching panels — plus the token pinned across the Inference,
/// Embeddings and Neural network panels.
/// </summary>
internal sealed class DiagramOptions(Action notify)
{
    private bool _showModel;
    private bool _showLoop;
    private bool _showTools;
    private bool _showSkills;
    private bool _showMcp;
    private bool _showA2A;
    private bool _showTechnicalLabels;
    private bool _showHarnessBoundary;
    private bool _showAgentBoundary;
    private bool _showEnvironment;
    private bool _expandHarness;
    private bool _showPromptSignature;
    private bool _promptSignatureDelta;
    private bool _showInferenceView;
    private bool _showEmbeddingsView;
    private bool _showNetworkView;
    private string? _selectedToken;

    /// <summary>Whether the model and its request/response arrows are visible.</summary>
    public bool ShowModel { get => _showModel; set { _showModel = value; notify(); } }

    /// <summary>The loop preference is retained while the model is hidden.</summary>
    public bool ShowLoop { get => _showLoop; set { _showLoop = value; notify(); } }

    /// <summary>Controls tool details in both host layouts and active tool resources, not tool permissions.</summary>
    public bool ShowTools { get => _showTools; set { _showTools = value; notify(); } }

    /// <summary>Whether the host's skill catalogue is visible.</summary>
    public bool ShowSkills { get => _showSkills; set { _showSkills = value; notify(); } }

    /// <summary>Whether MCP discovery details are visible.</summary>
    public bool ShowMcp { get => _showMcp; set { _showMcp = value; notify(); } }

    /// <summary>Whether connected agents and their delegated activity are visible.</summary>
    public bool ShowA2A { get => _showA2A; set { _showA2A = value; notify(); } }

    /// <summary>Whether implementation and actual deployment labels replace the branded overview labels.</summary>
    public bool ShowTechnicalLabels { get => _showTechnicalLabels; set { _showTechnicalLabels = value; notify(); } }

    /// <summary>Null denotes a custom combination; preset identity never controls rendering.</summary>
    public DiagramPreset? Preset =>
        _showSkills || _showMcp || _showA2A || _showHarnessBoundary || _showAgentBoundary ||
        _showEnvironment || _expandHarness || _showPromptSignature || _showInferenceView ||
        _showEmbeddingsView || _showNetworkView
            ? null
            : (_showModel, _showLoop, _showTechnicalLabels, _showTools) switch
            {
                (false, false, false, false) => DiagramPreset.Basic,
                (true, true, true, true) => DiagramPreset.Technical,
                _ => null,
            };

    /// <summary>Replaces visibility preferences atomically without changing panel interactions or execution state.</summary>
    public void ApplyPreset(DiagramPreset preset)
    {
        if (!Enum.IsDefined(preset)) throw new ArgumentOutOfRangeException(nameof(preset));
        _showModel = _showLoop = _showTechnicalLabels = _showTools = preset == DiagramPreset.Technical;
        _showSkills = _showMcp = _showA2A = false;
        _showHarnessBoundary = _showAgentBoundary = _showEnvironment = false;
        _expandHarness = _showPromptSignature = _showInferenceView = _showEmbeddingsView = _showNetworkView = false;
        notify();
    }

    /// <summary>Whether the dashed host boundary overlay is shown.</summary>
    public bool ShowHarnessBoundary { get => _showHarnessBoundary; set { _showHarnessBoundary = value; notify(); } }

    /// <summary>Whether the dashed Agent boundary overlay is shown.</summary>
    public bool ShowAgentBoundary { get => _showAgentBoundary; set { _showAgentBoundary = value; notify(); } }

    /// <summary>Whether the diagram surfaces where each node runs, the agent's risk level and its guardrails.</summary>
    public bool ShowEnvironment { get => _showEnvironment; set { _showEnvironment = value; notify(); } }

    /// <summary>Whether the host node is expanded into its anatomy.</summary>
    public bool ExpandHarness { get => _expandHarness; set { _expandHarness = value; notify(); } }

    /// <summary>Whether the prompt-signature panel is shown.</summary>
    public bool ShowPromptSignature { get => _showPromptSignature; set { _showPromptSignature = value; notify(); } }

    /// <summary>Whether the prompt-signature panel shows its delta ("growth") view instead of the comparison.</summary>
    public bool PromptSignatureDelta { get => _promptSignatureDelta; set { _promptSignatureDelta = value; notify(); } }

    /// <summary>Whether the simulated inference panel is shown. The tokens are a client-side fabrication.</summary>
    public bool ShowInferenceView { get => _showInferenceView; set { _showInferenceView = value; notify(); } }

    /// <summary>Whether the simulated embeddings panel is shown. The vectors are a client-side fabrication.</summary>
    public bool ShowEmbeddingsView { get => _showEmbeddingsView; set { _showEmbeddingsView = value; notify(); } }

    /// <summary>Whether the simulated neural-network panel is shown. The network is a client-side fabrication.</summary>
    public bool ShowNetworkView { get => _showNetworkView; set { _showNetworkView = value; notify(); } }

    /// <summary>
    /// The token pinned by clicking it in the Inference or Embeddings panel, normalised (lower-cased +
    /// trimmed) so the same word matches across panels. Null when nothing is selected.
    /// </summary>
    public string? SelectedToken => _selectedToken;

    /// <summary>Toggles the pinned token; clicking the already-selected token clears it. Blank tokens are ignored.</summary>
    public void SelectToken(string? text)
    {
        var key = string.IsNullOrWhiteSpace(text) ? null : text.Trim().ToLowerInvariant();
        _selectedToken = (key is not null && key == _selectedToken) ? null : key;
        notify();
    }

    /// <summary>Whether <paramref name="text"/> is the currently pinned token (case/whitespace-insensitive).</summary>
    public bool IsTokenSelected(string? text)
        => _selectedToken is not null
           && !string.IsNullOrWhiteSpace(text)
           && string.Equals(text.Trim().ToLowerInvariant(), _selectedToken, StringComparison.Ordinal);
}

/// <summary>Named starting points for the diagram's display options.</summary>
internal enum DiagramPreset
{
    Basic,
    Technical,
}
