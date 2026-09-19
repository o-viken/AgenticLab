using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using AgenticLab.Web.Flow;

namespace AgenticLab.Web.Components.Pages.FlowParts;

/// <summary>The Settings tab's local UI state: the workspace repo picker and the expandable skill/instruction rows.</summary>
public partial class FlowControls
{
    [CascadingParameter]
    private FlowViewState View { get; set; } = default!;

    [CascadingParameter]
    private FlowRunController Run { get; set; } = default!;

    // Local UI state for the workspace repo picker (a filterable dropdown of discovered/recent repos).
    private bool _pickerOpen;
    private string _pickerFilter = string.Empty;

    // Which skill/instruction rows are expanded to show their details (compact list, expand on demand).
    private readonly HashSet<string> _expandedSkills = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedInstructions = new(StringComparer.OrdinalIgnoreCase);

    private void ToggleSkill(string name)
    {
        if (!_expandedSkills.Remove(name))
        {
            _expandedSkills.Add(name);
        }
    }

    private void ToggleInstruction(string name)
    {
        if (!_expandedInstructions.Remove(name))
        {
            _expandedInstructions.Add(name);
        }
    }

    private void TogglePicker()
    {
        _pickerOpen = !_pickerOpen;
        if (!_pickerOpen)
        {
            _pickerFilter = string.Empty;
        }
    }

    // Selects a repo as the workspace and refreshes everything that depends on the workspace path.
    private async Task SelectWorkspace(string path)
    {
        View.Workspace = path;
        _pickerOpen = false;
        _pickerFilter = string.Empty;
        await Run.Catalogs.RefreshWorkspaceContextAsync();
    }

    // The suggestions matching the picker's filter (by repo name or full path); all of them when blank.
    private IReadOnlyList<WorkspaceSuggestion> FilteredSuggestions()
    {
        var all = View.WorkspacePrefs.Suggestions;
        if (string.IsNullOrWhiteSpace(_pickerFilter))
        {
            return all;
        }

        var query = _pickerFilter.Trim();
        return all
            .Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || s.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // Enter picks the first filtered repo; Escape closes the picker.
    private async Task OnPickerKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            _pickerOpen = false;
            _pickerFilter = string.Empty;
            return;
        }

        if (args.Key == "Enter")
        {
            var first = FilteredSuggestions().FirstOrDefault();
            if (first is not null)
            {
                await SelectWorkspace(first.Path);
            }
        }
    }
}
