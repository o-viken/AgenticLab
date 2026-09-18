namespace TheSeries.Web.Flow;

/// <summary>
/// How the merged Client + AiService ("Agent host") node and the Model node present themselves
/// for the current vendor and perspective — their labels and the active system (harness) prompt fetched
/// from the service, shown in the anatomy's System Prompt box as a preview or the full text.
/// </summary>
internal sealed class HarnessView(FlowViewState owner, Action notify)
{
    private string _promptText = string.Empty;
    private bool _showFullPrompt;

    /// <summary>The merged node's title: the vendor name when selected, otherwise Agent host.</summary>
    public string Label => owner.Vendor == Vendor.Default
        ? "Agent host"
        : owner.Roster.VendorName;

    /// <summary>The merged node's subtitle: Non-technical states the grouping plainly; Technical/Expert name the agent.</summary>
    public string Subtitle => owner.Perspective switch
    {
        Perspective.Simple => "Agent service",
        Perspective.NonTechnical => "Agent host · Client + AiService",
        Perspective.Technical => $"Agent host · Client + AiService · {owner.SelectedAgent ?? "Agent"}",
        _ => $"Agent host · AiService · {owner.SelectedAgent ?? "Agent"}",
    };

    /// <summary>
    /// The LLM node's provider/model label. The Default vendor and the Expert perspective show the real Azure
    /// OpenAI deployment; a brand vendor's other perspectives show its simulated provider label to mimic that
    /// the product runs on its own model, even though the backend is always Azure OpenAI.
    /// </summary>
    public string LlmModelLabel
    {
        get
        {
            var simulated = owner.Roster.CurrentVendorInfo?.ModelLabel ?? string.Empty;
            if (owner.Vendor != Vendor.Default && owner.Perspective != Perspective.Expert && simulated.Length > 0)
            {
                return simulated;
            }

            var deployment = owner.Agent.Info?.ModelId;
            return string.IsNullOrWhiteSpace(deployment) ? "Azure OpenAI" : $"Azure OpenAI · {deployment}";
        }
    }

    // --- System prompt ------------------------------------------------------

    /// <summary>The descriptive fallback for the System Prompt box before the real text has been fetched.</summary>
    public string PromptBody => owner.Vendor == Vendor.Default
        ? "Agent host instructions: operating guidance and the tool loop"
        : $"{owner.Roster.VendorName} system prompt: replaces the shared host instructions for this run (persona kept)";

    /// <summary>The actual harness (system) prompt text for the selected vendor and agent, fetched from the service.</summary>
    public string PromptText => _promptText;

    public bool HasPromptText => !string.IsNullOrWhiteSpace(_promptText);

    /// <summary>Whether the anatomy shows the full system prompt text (vs. a short preview).</summary>
    public bool ShowFullPrompt
    {
        get => _showFullPrompt;
        set { _showFullPrompt = value; notify(); }
    }

    /// <summary>The fetched text as a short preview (or the full text when expanded), else the descriptive fallback.</summary>
    public string PromptDisplay
    {
        get
        {
            if (!HasPromptText)
            {
                return PromptBody;
            }

            var text = _promptText.Trim();
            if (_showFullPrompt || text.Length <= 160)
            {
                return text;
            }

            return text[..160].TrimEnd() + "…";
        }
    }

    /// <summary>Replaces the fetched prompt (after the vendor or agent changes), collapsing any expanded full view.</summary>
    public void SetPrompt(string? prompt)
    {
        _promptText = prompt ?? string.Empty;
        _showFullPrompt = false;
        notify();
    }
}
