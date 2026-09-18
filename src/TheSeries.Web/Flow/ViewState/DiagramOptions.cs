namespace TheSeries.Web.Flow;

/// <summary>
/// The diagram's view toggles — the boundary overlays, the environment &amp; risk view, the expanded
/// harness anatomy and the Expert-only teaching panels — plus the token pinned across the Inference,
/// Embeddings and Neural network panels.
/// </summary>
internal sealed class DiagramOptions(Action notify)
{
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

    /// <summary>Whether the dashed Harness boundary overlay is shown (Expert only).</summary>
    public bool ShowHarnessBoundary { get => _showHarnessBoundary; set { _showHarnessBoundary = value; notify(); } }

    /// <summary>Whether the dashed Agent boundary overlay is shown (Expert only).</summary>
    public bool ShowAgentBoundary { get => _showAgentBoundary; set { _showAgentBoundary = value; notify(); } }

    /// <summary>Whether the diagram surfaces where each node runs, the agent's risk level and its guardrails. Every perspective.</summary>
    public bool ShowEnvironment { get => _showEnvironment; set { _showEnvironment = value; notify(); } }

    /// <summary>Whether the Harness node is expanded into its "anatomy" (Expert only).</summary>
    public bool ExpandHarness { get => _expandHarness; set { _expandHarness = value; notify(); } }

    /// <summary>Whether the prompt-signature panel is shown (Expert only).</summary>
    public bool ShowPromptSignature { get => _showPromptSignature; set { _showPromptSignature = value; notify(); } }

    /// <summary>Whether the prompt-signature panel shows its delta ("growth") view instead of the comparison.</summary>
    public bool PromptSignatureDelta { get => _promptSignatureDelta; set { _promptSignatureDelta = value; notify(); } }

    /// <summary>Whether the simulated inference panel is shown (Expert only). The tokens are a client-side fabrication.</summary>
    public bool ShowInferenceView { get => _showInferenceView; set { _showInferenceView = value; notify(); } }

    /// <summary>Whether the simulated embeddings panel is shown (Expert only). The vectors are a client-side fabrication.</summary>
    public bool ShowEmbeddingsView { get => _showEmbeddingsView; set { _showEmbeddingsView = value; notify(); } }

    /// <summary>Whether the simulated neural-network panel is shown (Expert only). The network is a client-side fabrication.</summary>
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
