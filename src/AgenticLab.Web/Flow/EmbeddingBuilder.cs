namespace AgenticLab.Web.Flow;

/// <summary>
/// Pure helpers that turn the run's (already-tokenized) prompt into a <see cref="EmbeddingsView"/> — a
/// deliberately <em>simulated</em> view of token embeddings and a symbolic neural-network forward pass, for
/// teaching. Each meaningful prompt token is given a small fixed-length fake vector (deterministic, seeded by
/// a stable hash of the lower-cased token, so identical tokens share a vector and land on the same spot) and a
/// 2-D position obtained by projecting that vector onto two fixed pseudo-random axes. Nothing here reflects a
/// real embedding model — the backend exposes no vectors; the numbers are invented but deterministic so they
/// don't flicker across the many per-event re-renders a run triggers. No UI or state dependency.
/// </summary>
internal static class EmbeddingBuilder
{
    /// <summary>How many embedding components to fabricate and render per token (kept small for legibility).</summary>
    private const int Dimensions = 8;

    /// <summary>
    /// Builds the embeddings view from a computed <see cref="InferenceView"/> (reusing its prompt
    /// tokenization). Whitespace tokens are dropped (only meaningful tokens get a point); the predicted token
    /// is the run's first generated token when an answer exists. Returns <see cref="EmbeddingsView.Empty"/>
    /// until the prompt has been tokenized.
    /// </summary>
    public static EmbeddingsView Build(InferenceView inference)
    {
        var meaningful = new List<string>(inference.PromptTokens.Count);
        foreach (var token in inference.PromptTokens)
        {
            if (!string.IsNullOrWhiteSpace(token.Text))
            {
                meaningful.Add(token.Text);
            }
        }

        if (meaningful.Count == 0)
        {
            return EmbeddingsView.Empty;
        }

        // Fabricate a vector per token (each component in −1…1), deterministic per token text.
        var vectors = new double[meaningful.Count][];
        for (var i = 0; i < meaningful.Count; i++)
        {
            var key = meaningful[i].Trim().ToLowerInvariant();
            var vec = new double[Dimensions];
            for (var d = 0; d < Dimensions; d++)
            {
                vec[d] = Component(key, d);
            }

            vectors[i] = vec;
        }

        // Two fixed projection axes (independent of the tokens) give each vector a 2-D position. Normalising
        // across the set keeps the scatter inside the view with a little margin.
        var axisX = Axis(0);
        var axisY = Axis(1);
        var xs = new double[meaningful.Count];
        var ys = new double[meaningful.Count];
        for (var i = 0; i < meaningful.Count; i++)
        {
            xs[i] = Dot(vectors[i], axisX);
            ys[i] = Dot(vectors[i], axisY);
        }

        Normalize(xs);
        Normalize(ys);

        var tokens = new List<EmbeddingToken>(meaningful.Count);
        for (var i = 0; i < meaningful.Count; i++)
        {
            tokens.Add(new EmbeddingToken(meaningful[i], vectors[i], xs[i], ys[i]));
        }

        var predicted = inference.ResponseTokens.Count > 0
            ? inference.ResponseTokens[0].Text
            : null;

        return new EmbeddingsView(tokens, Dimensions, predicted);
    }

    /// <summary>One deterministic vector component in −1…1 for a token/dimension pair.</summary>
    private static double Component(string key, int dim)
    {
        var rng = new Random(unchecked((int)(InferenceBuilder.StableHash(key) ^ (uint)((dim + 1) * 2654435761))));
        return rng.NextDouble() * 2.0 - 1.0;
    }

    /// <summary>One of the two fixed projection axes (independent of any token), as a length-<c>Dimensions</c> vector.</summary>
    private static double[] Axis(int which)
    {
        var rng = new Random(unchecked((int)(0x9E3779B9u ^ (uint)((which + 1) * 40503))));
        var axis = new double[Dimensions];
        for (var d = 0; d < Dimensions; d++)
        {
            axis[d] = rng.NextDouble() * 2.0 - 1.0;
        }

        return axis;
    }

    /// <summary>Dot product of two equal-length vectors.</summary>
    private static double Dot(double[] a, double[] b)
    {
        var sum = 0.0;
        for (var i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
    }

    /// <summary>Rescales values in place into 0.08…0.92 (a margin inside the scatter); flat sets map to 0.5.</summary>
    private static void Normalize(double[] values)
    {
        var min = double.MaxValue;
        var max = double.MinValue;
        foreach (var v in values)
        {
            if (v < min) min = v;
            if (v > max) max = v;
        }

        var range = max - min;
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = range < 1e-9 ? 0.5 : 0.08 + (values[i] - min) / range * 0.84;
        }
    }
}
