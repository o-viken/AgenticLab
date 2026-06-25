using System.Text.Json;
using System.Text.RegularExpressions;

namespace TheSeries.Web.Flow;

/// <summary>
/// Pure helpers that turn a run's captured events into an <see cref="InferenceView"/> — a deliberately
/// <em>simulated</em> peek "inside the LLM" for teaching. It reads the latest <c>llm-request</c> for the
/// most recent user message (split into word-piece chips, plus a whole-prompt ≈ chars/4 token estimate)
/// and the latest <c>final</c> answer (replayed as autoregressively generated tokens, each with a fabricated
/// candidate list). Nothing here reflects the real model's tokenizer or logits — the backend never exposes
/// tokens; the numbers are invented but <em>deterministic</em> (seeded by a stable hash) so they don't
/// flicker across the many per-event re-renders a run triggers. No UI or state dependency.
/// </summary>
internal static partial class InferenceBuilder
{
    // Splits a string into runs of whitespace, alphanumerics, or other punctuation — the boundaries a
    // crude word-piece tokenizer would break on.
    [GeneratedRegex(@"\s+|[A-Za-z0-9]+|[^\sA-Za-z0-9]+")]
    private static partial Regex TokenRegex();

    // Plausible-looking filler alternatives a sampler might have considered, drawn from deterministically.
    private static readonly string[] CandidatePool =
    {
        " the", " a", " and", " to", " of", " is", " was", " in", " that", " it",
        " for", " with", " on", " as", "ing", "ed", "s", ".", ",", " \u2014",
    };

    /// <summary>
    /// Builds the inference view from the current run's <paramref name="events"/>: the latest captured
    /// <c>llm-request</c> (for the user message + prompt-size estimate) and the latest <c>final</c>/
    /// <c>llm-response</c> (for the generated answer). Returns <see cref="InferenceView.Empty"/> until a
    /// request has been captured.
    /// </summary>
    public static InferenceView Build(IReadOnlyList<FlowEvent> events)
    {
        string? requestJson = null;
        string? answer = null;
        for (var i = events.Count - 1; i >= 0; i--)
        {
            var e = events[i];
            if (answer is null && (e.Kind == "final" || e.Kind == "llm-response") && !string.IsNullOrWhiteSpace(e.Data))
            {
                answer = e.Data;
            }
            else if (requestJson is null && e.Kind == "llm-request" && !string.IsNullOrWhiteSpace(e.Data))
            {
                requestJson = e.Data;
            }

            if (requestJson is not null && answer is not null)
            {
                break;
            }
        }

        if (requestJson is null)
        {
            return InferenceView.Empty;
        }

        var (userMessage, promptChars) = ParseRequest(requestJson);
        var promptTokens = Tokenize(userMessage, withCandidates: false);
        var responseTokens = string.IsNullOrWhiteSpace(answer)
            ? Array.Empty<SimToken>()
            : Tokenize(answer!, withCandidates: true);

        return new InferenceView(
            promptTokens,
            responseTokens,
            PromptTokenEstimate: (promptChars + 3) / 4,
            HasPrompt: promptTokens.Count > 0,
            HasResponse: responseTokens.Count > 0);
    }

    /// <summary>
    /// Pulls the last user message text and the whole-request character count out of a captured
    /// <c>llm-request</c> payload (instructions + every message's content + the tool catalogue). Falls back
    /// to attributing the raw payload length on a malformed capture.
    /// </summary>
    private static (string UserMessage, int PromptChars) ParseRequest(string requestJson)
    {
        var userMessage = string.Empty;
        var chars = 0;
        try
        {
            using var doc = JsonDocument.Parse(PromptSignatureBuilder.ToStrictJson(requestJson));
            var root = doc.RootElement;

            if (root.TryGetProperty("instructions", out var instructions) && instructions.ValueKind == JsonValueKind.String)
            {
                chars += instructions.GetString()?.Length ?? 0;
            }

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    var role = message.TryGetProperty("role", out var r) ? r.GetString() : null;
                    var text = MessageText(message);
                    chars += text.Length;
                    if (role == "user" && !string.IsNullOrWhiteSpace(text))
                    {
                        userMessage = text;
                    }
                }
            }

            if (root.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Array)
            {
                foreach (var tool in tools.EnumerateArray())
                {
                    chars += tool.GetRawText().Length;
                }
            }
        }
        catch (JsonException)
        {
            return (string.Empty, requestJson.Length);
        }

        return (userMessage, chars);
    }

    /// <summary>Concatenates the text parts of one message's content array.</summary>
    private static string MessageText(JsonElement message)
    {
        if (!message.TryGetProperty("contents", out var contents) || contents.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var content in contents.EnumerateArray())
        {
            if (content.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                var value = text.GetString();
                if (!string.IsNullOrEmpty(value))
                {
                    parts.Add(value);
                }
            }
        }

        return string.Join(string.Empty, parts);
    }

    /// <summary>
    /// Splits <paramref name="text"/> into illustrative word-piece tokens: whitespace runs become their own
    /// tokens, a leading single space attaches to the following word (mirroring byte-pair encoders' leading-
    /// space convention), and long alphanumeric chunks are broken into ~4-char pieces. When
    /// <paramref name="withCandidates"/> is set, each non-whitespace token also gets a fabricated candidate
    /// list for the hover popover.
    /// </summary>
    private static IReadOnlyList<SimToken> Tokenize(string text, bool withCandidates)
    {
        var tokens = new List<SimToken>();
        var index = 0;
        var pendingSpace = false;
        foreach (Match m in TokenRegex().Matches(text))
        {
            var chunk = m.Value;

            // Pure whitespace: a single space is held to fold into the next word piece; any other run
            // (multiple spaces, newlines, tabs) becomes its own token.
            if (char.IsWhiteSpace(chunk[0]))
            {
                if (chunk == " ")
                {
                    pendingSpace = true;
                    continue;
                }

                tokens.Add(MakeToken(chunk, index++, withCandidates: false));
                continue;
            }

            var prefix = pendingSpace ? " " : string.Empty;
            pendingSpace = false;

            // Break long alphanumeric runs into ~4-char word pieces; keep punctuation/short runs whole.
            if (chunk.Length > 5 && IsAlphaNumeric(chunk))
            {
                for (var i = 0; i < chunk.Length; i += 4)
                {
                    var piece = chunk.Substring(i, Math.Min(4, chunk.Length - i));
                    var withPrefix = i == 0 ? prefix + piece : piece;
                    tokens.Add(MakeToken(withPrefix, index++, withCandidates));
                }
            }
            else
            {
                tokens.Add(MakeToken(prefix + chunk, index++, withCandidates));
            }
        }

        return tokens;
    }

    private static bool IsAlphaNumeric(string s)
    {
        foreach (var c in s)
        {
            if (!char.IsLetterOrDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Builds one token with a deterministic chosen-probability and (optionally) a candidate list.</summary>
    private static SimToken MakeToken(string textValue, int index, bool withCandidates)
    {
        if (!withCandidates)
        {
            return new SimToken(textValue, 1.0, Array.Empty<TokenCandidate>());
        }

        var rng = new Random(unchecked((int)(StableHash(textValue) ^ (uint)(index * 2654435761))));
        var chosen = 0.45 + rng.NextDouble() * 0.5; // 0.45–0.95
        var candidates = new List<TokenCandidate>(4) { new(textValue, chosen) };

        var remaining = 1.0 - chosen;
        var alts = 2 + rng.Next(2); // 2–3 alternates
        for (var i = 0; i < alts && remaining > 0.01; i++)
        {
            var share = i == alts - 1 ? remaining : remaining * (0.4 + rng.NextDouble() * 0.4);
            remaining -= share;
            var alt = CandidatePool[rng.Next(CandidatePool.Length)];
            candidates.Add(new TokenCandidate(alt, Math.Round(share, 3)));
        }

        return new SimToken(textValue, chosen, candidates);
    }

    /// <summary>
    /// A stable FNV-1a hash (not <see cref="string.GetHashCode()"/>, which is randomized per process) so the
    /// fabricated probabilities are identical across re-renders and process restarts. Shared with
    /// <see cref="EmbeddingBuilder"/> so its fake vectors are likewise deterministic.
    /// </summary>
    internal static uint StableHash(string s)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var c in s)
        {
            hash ^= c;
            hash *= prime;
        }

        return hash;
    }
}
