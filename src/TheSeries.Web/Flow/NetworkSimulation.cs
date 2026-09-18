using System.Globalization;
using System.Text;

namespace TheSeries.Web.Flow;

/// <summary>
/// The pure math behind the simulated Neural network panel: the symbolic layer geometry, the fabricated
/// input vectors, the per-token output-node shuffle, node sizing/tinting and the display helpers. Nothing
/// here is real model internals — it exists so the panel can teach the shape of a forward pass.
/// </summary>
internal static class NetworkSimulation
{
    public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Simulated sampling settings shown next to the distribution (the backend never exposes the real values).
    public const int Vocab = 50_257;
    public const double Temperature = 0.7;
    public const double TopP = 0.9;

    // Symbolic network shape: input (embedding) → two hidden layers → output. Input and output layers carry
    // the same node count; only the top few output nodes map to real candidates (the rest stay at rest).
    public static readonly int[] LayerSizes = { 8, 6, 5, 8 };
    public const int VbW = 320;
    public const int VbH = 120;

    public static double LayerX(int layer) => 24 + layer * ((VbW - 48) / (double)(LayerSizes.Length - 1));

    public static double NodeY(int count, int i) => (i + 1) / (double)(count + 1) * VbH;

    public static string F(double value) => value.ToString("0.##", Inv);

    /// <summary>The leftover probability mass spread across the rest of the vocabulary.</summary>
    public static int RestPercent(IReadOnlyList<TokenCandidate> ordered)
    {
        var shown = 0;
        foreach (var c in ordered)
        {
            shown += c.Percent;
        }

        return Math.Max(0, 100 - shown);
    }

    /// <summary>A pinned token's real embedding as the input layer (padded to the layer width).</summary>
    public static double[] PinnedInputVector(EmbeddingToken token)
    {
        var dims = LayerSizes[0];
        var v = new double[dims];
        for (var i = 0; i < dims; i++)
        {
            v[i] = i < token.Vector.Count ? token.Vector[i] : 0.0;
        }

        return v;
    }

    /// <summary>An input vector derived deterministically from the current decode token, so the layer changes each step.</summary>
    public static double[] LiveInputVector(string tokenText)
    {
        var dims = LayerSizes[0];
        var v = new double[dims];
        for (var i = 0; i < dims; i++)
        {
            // FNV-1a of "<token>#<dim>" mapped to [-1, 1] — stable per token, different per dimension.
            var h = InferenceBuilder.StableHash(tokenText + "#" + i);
            v[i] = ((h % 2000) / 1000.0) - 1.0;
        }

        return v;
    }

    /// <summary>The step's candidates sorted high→low probability; the picked token sorts first.</summary>
    public static IReadOnlyList<TokenCandidate> Ordered(SimToken token)
    {
        var list = new List<TokenCandidate>(token.Candidates);
        list.Sort((a, b) => b.Probability.CompareTo(a.Probability));
        return list;
    }

    /// <summary>
    /// Maps candidate ranks to output-node positions via a deterministic per-token shuffle, so the picked
    /// candidate lands on a different node each step. Returns, per node, the candidate rank shown there or -1.
    /// </summary>
    public static int[] OutputSlots(string seedText, int nodeCount, int candidateCount)
    {
        var order = new int[nodeCount];
        for (var i = 0; i < nodeCount; i++)
        {
            order[i] = i;
        }

        // Fisher–Yates shuffle seeded by the token text so the layout is stable across re-renders.
        var rng = new Random(unchecked((int)InferenceBuilder.StableHash(seedText)));
        for (var i = nodeCount - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        var slots = new int[nodeCount];
        Array.Fill(slots, -1);
        for (var rank = 0; rank < candidateCount && rank < nodeCount; rank++)
        {
            slots[order[rank]] = rank;
        }

        return slots;
    }

    /// <summary>Input nodes follow the input vector; output nodes grow with their candidate's probability; resting nodes stay default.</summary>
    public static double NodeRadius(bool isInput, int i, int rank, IReadOnlyList<TokenCandidate>? ordered, double[]? inputVec)
    {
        if (isInput)
        {
            return inputVec is not null && i < inputVec.Length
                ? 3.0 + Math.Min(1.0, Math.Abs(inputVec[i])) * 4.0
                : 4.0;
        }

        if (rank >= 0 && ordered is not null && rank < ordered.Count)
        {
            return 3.0 + Math.Min(1.0, ordered[rank].Probability) * 5.0;
        }

        return 4.0;
    }

    /// <summary>Input nodes are tinted by sign/size; output nodes fade in by probability (the fill colour comes from CSS).</summary>
    public static string NodeStyle(bool isInput, int i, int rank, IReadOnlyList<TokenCandidate>? ordered, double[]? inputVec)
    {
        if (isInput)
        {
            return inputVec is not null && i < inputVec.Length ? $"fill:{Diverging(inputVec[i])}" : string.Empty;
        }

        if (rank >= 0 && ordered is not null && rank < ordered.Count)
        {
            var alpha = (0.25 + Math.Min(1.0, ordered[rank].Probability) * 0.75).ToString("0.##", Inv);
            return $"opacity:{alpha}";
        }

        return string.Empty;
    }

    /// <summary>
    /// SVG &lt;text&gt; labels showing each input node's component value, as raw markup because Razor reserves
    /// the bare &lt;text&gt; tag; styled inline so it works without scoped-CSS rewriting.
    /// </summary>
    public static string InputValueLabels(double[] vec)
    {
        var sb = new StringBuilder();
        var x = F(LayerX(0) - 8);
        const string style = "font-family:var(--mono,ui-monospace,Consolas,monospace);font-size:5px;fill:var(--text-dim)";
        for (var i = 0; i < LayerSizes[0] && i < vec.Length; i++)
        {
            var y = F(NodeY(LayerSizes[0], i) + 2);
            var val = vec[i].ToString("+0.0;-0.0", Inv);
            sb.Append($"<text x=\"{x}\" y=\"{y}\" text-anchor=\"end\" style=\"{style}\">{val}</text>");
        }

        return sb.ToString();
    }

    /// <summary>Diverging colour (red positive, blue negative, opacity by magnitude) matching the Embeddings heatmap.</summary>
    public static string Diverging(double value)
    {
        var alpha = (0.12 + Math.Min(1.0, Math.Abs(value)) * 0.78).ToString("0.##", Inv);
        var rgb = value >= 0 ? "229,84,84" : "74,140,255";
        return $"rgba({rgb},{alpha})";
    }

    /// <summary>Renders whitespace tokens with a visible glyph (↵/␣) so they don't vanish.</summary>
    public static string Display(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        if (text.Contains('\n'))
        {
            return "↵";
        }

        return string.IsNullOrWhiteSpace(text) ? "␣" : text;
    }
}
