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
    private const string LayoutStorageKey = "theseries-layout";

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

            var storedLayout = await JS.InvokeAsync<string?>("localStorage.getItem", LayoutStorageKey);
            if (!string.IsNullOrEmpty(storedLayout)
                && Enum.TryParse<FlowLayout>(storedLayout, out var layout)
                && layout != _view.Layout)
            {
                _view.Layout = layout;
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

    private async Task SetLayoutAsync(FlowLayout layout)
    {
        _view.Layout = layout;
        try
        {
            await JS.InvokeVoidAsync("localStorage.setItem", LayoutStorageKey, layout.ToString());
        }
        catch
        {
            // Persisting the layout is best-effort.
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
