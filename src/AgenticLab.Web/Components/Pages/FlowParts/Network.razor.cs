using Microsoft.AspNetCore.Components;
using AgenticLab.Web.Flow;
using static AgenticLab.Web.Flow.NetworkSimulation;

namespace AgenticLab.Web.Components.Pages.FlowParts;

/// <summary>
/// The Neural network panel's state: a wall-clock timer "decodes" the simulated answer one token at a
/// time so the distribution/pick animates on a loop, with play/pause and manual stepping. The component
/// only exists while the toggle is on, so the timer's lifetime is bounded by the panel's visibility.
/// </summary>
public partial class Network : IDisposable
{
    [CascadingParameter]
    private FlowRunController Run { get; set; } = default!;

    [CascadingParameter]
    private FlowViewState View { get; set; } = default!;

    private EmbeddingsView Emb => Run.Projections.Embeddings;

    /// <summary>The simulated generated answer tokens (each carrying a fabricated candidate distribution).</summary>
    private IReadOnlyList<SimToken> Gen => Run.Projections.Inference.ResponseTokens;

    /// <summary>The decode step the loop is currently on (advanced by the timer, wrapped modulo the token count).</summary>
    private int CurStep => _curStep;

    private int _curStep;
    private System.Timers.Timer? _timer;
    private int _lastGenCount = -1;
    private bool _playing = true;

    protected override void OnInitialized()
    {
        _timer = new System.Timers.Timer(1100) { AutoReset = true };
        _timer.Elapsed += (_, _) => Advance();
        _timer.Start();
    }

    protected override void OnParametersSet()
    {
        // Restart (and resume) the loop from the first token whenever the generated answer changes.
        var count = Gen.Count;
        if (count != _lastGenCount)
        {
            _lastGenCount = count;
            _curStep = 0;
            _playing = count > 0;
        }
    }

    private void Advance()
    {
        if (!_playing)
        {
            return;
        }

        var count = Gen.Count;
        if (count == 0)
        {
            return;
        }

        // Stop at the final token instead of looping — the paused state freezes every animation.
        if (_curStep >= count - 1)
        {
            _playing = false;
            _ = InvokeAsync(StateHasChanged);
            return;
        }

        _curStep++;
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>Toggles the auto-decode loop; pressing play at the end replays from the first token.</summary>
    private void TogglePlay()
    {
        _playing = !_playing;
        if (_playing && Gen.Count > 0 && _curStep >= Gen.Count - 1)
        {
            _curStep = 0;
        }
    }

    /// <summary>Advances one token by hand (also pauses the auto-loop so it stays put).</summary>
    private void StepNext()
    {
        var count = Gen.Count;
        if (count == 0)
        {
            return;
        }

        _playing = false;
        _curStep = (_curStep + 1) % count;
    }

    /// <summary>Steps back one token by hand (also pauses the auto-loop).</summary>
    private void StepPrev()
    {
        var count = Gen.Count;
        if (count == 0)
        {
            return;
        }

        _playing = false;
        _curStep = (_curStep - 1 + count) % count;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>The pinned token's embedding, when the selected token is one of this view's tokens.</summary>
    private EmbeddingToken? Selected
    {
        get
        {
            if (View.Diagram.SelectedToken is null)
            {
                return null;
            }

            foreach (var token in Emb.Tokens)
            {
                if (View.Diagram.IsTokenSelected(token.Text))
                {
                    return token;
                }
            }

            return null;
        }
    }

    // The input layer's values: a pinned token's real vector takes priority; otherwise a vector derived
    // from the current decode token so the layer updates every step. Null when there is nothing to show.
    private double[]? BuildInputVector(SimToken? cur) =>
        Selected is { } sel ? PinnedInputVector(sel)
        : cur is { Text.Length: > 0 } c ? LiveInputVector(c.Text)
        : null;
}
