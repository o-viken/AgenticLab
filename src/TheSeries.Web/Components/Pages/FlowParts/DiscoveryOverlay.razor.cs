using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace TheSeries.Web.Components.Pages.FlowParts;

/// <summary>Owns modal focus and discovery cancellation without replacing the live Flow page.</summary>
public partial class DiscoveryOverlay : IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>Disallows reconnecting tools while the live conversation is running.</summary>
    [Parameter] public bool AllowRediscovery { get; set; }

    /// <summary>Dismisses without navigation and reports whether discovery may have changed catalogs.</summary>
    [Parameter] public EventCallback<bool> OnClose { get; set; }

    private ElementReference _dialog;
    private Discovery? _discovery;
    private IJSObjectReference? _module;
    private DotNetObjectReference<DiscoveryOverlay>? _reference;
    private bool _closing;
    private bool _disposed;

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        var module = await JS.InvokeAsync<IJSObjectReference>("import", "./Components/Pages/FlowParts/DiscoveryOverlay.razor.js");
        if (_disposed)
        {
            await module.DisposeAsync();
            return;
        }
        _module = module;
        _reference = DotNetObjectReference.Create(this);
        await _module.InvokeVoidAsync("open", _dialog, _reference);
    }

    /// <summary>Handles Escape/backdrop dismissal after cancelling discovery, not chat.</summary>
    [JSInvokable]
    public async Task CloseAsync()
    {
        if (_closing || _disposed) return;
        _closing = true;
        await InvokeAsync(StateHasChanged);
        if (_discovery is not null) await _discovery.CancelAsync();
        if (!_disposed) await OnClose.InvokeAsync(_discovery?.HasRediscovered ?? false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync("close", _dialog);
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
        }
        _reference?.Dispose();
    }
}