using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TheSeries.Web.Flow;

namespace TheSeries.Web.Components.Pages;

/// <summary>
/// Code-behind for the live agent-flow page. Owns the two page-scoped state containers
/// (<see cref="FlowViewState"/> for view/preferences, <see cref="FlowRunController"/> for run state),
/// cascades them to the child components, and bridges their change events to Blazor re-renders.
/// State is instantiated here (not via DI) so it shares the component's circuit lifetime.
/// </summary>
public partial class Flow : IDisposable
{
    private const string VendorStorageKey = "theseries-vendor";
    private const string PanelStorageKey = "theseries-panels";

    [Inject]
    private AiServiceClient Ai { get; set; } = default!;

    [Inject]
    private ConceptCatalog Concepts { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    private FlowViewState _view = default!;
    private FlowRunController _run = default!;

    protected override void OnInitialized()
    {
        _view = new FlowViewState(Concepts);
        _run = new FlowRunController(Ai, _view);
        _view.Changed += OnViewChanged;
        _run.Changed += OnRunChangedAsync;
    }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var vendors = await Ai.GetVendorsAsync();
            if (vendors is not null)
            {
                _view.SetVendors(vendors.Vendors);
            }

            var response = await Ai.GetAgentsAsync();
            if (response is not null)
            {
                _view.SetAgents(response.Agents);
                _view.InitSelectedAgent(_view.VendorDefaultAgent ?? response.Default);
            }

            await _run.RefreshHarnessPromptAsync();
        }
        catch (Exception ex)
        {
            _run.ReportError($"Could not load the agent list: {ex.Message}");
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            var stored = await JS.InvokeAsync<string?>("localStorage.getItem", VendorStorageKey);
            if (!string.IsNullOrEmpty(stored)
                && Enum.TryParse<Vendor>(stored, out var vendor)
                && vendor != _view.Vendor)
            {
                _view.Vendor = vendor;
                _view.SelectedAgent = _view.VendorDefaultAgent ?? _view.SelectedAgent;
                await _run.RefreshWorkspaceContextAsync();
                StateHasChanged();
            }

            var storedPanels = await JS.InvokeAsync<string?>("localStorage.getItem", PanelStorageKey);
            if (!string.IsNullOrEmpty(storedPanels) && PanelState.TryParse(storedPanels, out var panels))
            {
                _view.InitPanels(panels.LeftCollapsed, panels.RightCollapsed, panels.BottomCollapsed,
                    panels.LeftWidth, panels.RightWidth, panels.BottomHeight);
                StateHasChanged();
            }
        }
        catch
        {
            // localStorage may be unavailable (prerender); ignore and keep the default vendor.
        }
    }

    private async Task SetVendorAsync(Vendor vendor)
    {
        _view.Vendor = vendor;
        _view.SelectedAgent = _view.VendorDefaultAgent ?? _view.SelectedAgent;
        try
        {
            await JS.InvokeVoidAsync("localStorage.setItem", VendorStorageKey, vendor.ToString());
        }
        catch
        {
            // Persisting the vendor is best-effort.
        }

        await _run.RefreshWorkspaceContextAsync();
    }

    private Task ToggleLeftPanelAsync()
    {
        _view.ToggleLeftPanel();
        return SavePanelsAsync();
    }

    private Task ToggleRightPanelAsync()
    {
        _view.ToggleRightPanel();
        return SavePanelsAsync();
    }

    private Task SetLeftWidthAsync(int width)
    {
        _view.LeftPanelWidth = width;
        return SavePanelsAsync();
    }

    private Task SetRightWidthAsync(int width)
    {
        _view.RightPanelWidth = width;
        return SavePanelsAsync();
    }

    private Task ToggleBottomPanelAsync()
    {
        _view.ToggleBottomPanel();
        return SavePanelsAsync();
    }

    private Task SetBottomHeightAsync(int height)
    {
        _view.BottomPanelHeight = height;
        return SavePanelsAsync();
    }

    private async Task SavePanelsAsync()
    {
        var state = new PanelState(
            _view.LeftPanelCollapsed,
            _view.RightPanelCollapsed,
            _view.BottomPanelCollapsed,
            _view.LeftPanelWidth,
            _view.RightPanelWidth,
            _view.BottomPanelHeight);
        try
        {
            await JS.InvokeVoidAsync("localStorage.setItem", PanelStorageKey, state.Serialize());
        }
        catch
        {
            // Persisting the panel layout is best-effort.
        }
    }

    private void OnViewChanged() => _ = InvokeAsync(StateHasChanged);

    private Task OnRunChangedAsync() => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        _view.Changed -= OnViewChanged;
        _run.Changed -= OnRunChangedAsync;
        _run.Dispose();
    }
}

/// <summary>The persisted panel layout (collapsed flags + sizes) stored in localStorage.</summary>
internal readonly record struct PanelState(
    bool LeftCollapsed,
    bool RightCollapsed,
    bool BottomCollapsed,
    int LeftWidth,
    int RightWidth,
    int BottomHeight)
{
    /// <summary>Serialises to a compact <c>L|R|B|leftW|rightW|bottomH</c> string (1/0 for the collapsed flags).</summary>
    public string Serialize() =>
        $"{(LeftCollapsed ? 1 : 0)}|{(RightCollapsed ? 1 : 0)}|{(BottomCollapsed ? 1 : 0)}|{LeftWidth}|{RightWidth}|{BottomHeight}";

    /// <summary>Parses the <see cref="Serialize"/> format; returns false for any malformed value.</summary>
    public static bool TryParse(string? value, out PanelState state)
    {
        state = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('|');
        if (parts.Length != 6
            || !int.TryParse(parts[3], out var leftWidth)
            || !int.TryParse(parts[4], out var rightWidth)
            || !int.TryParse(parts[5], out var bottomHeight))
        {
            return false;
        }

        state = new PanelState(parts[0] == "1", parts[1] == "1", parts[2] == "1", leftWidth, rightWidth, bottomHeight);
        return true;
    }
}
