using System.Text;
using System.Text.Json;

namespace TheSeries.Web.Flow;

/// <summary>
/// Pure helpers that turn the captured <c>llm-request</c> payloads into a <see cref="PromptSignatureView"/>:
/// each conversation exchange's representative request (its last <c>llm-request</c>) broken down by message
/// category (system prompt, user, assistant and tool result — the static tool catalogue is deliberately
/// excluded so the signature reflects only the conversation content that grows turn to turn), the last two
/// exchanges compared, plus a prefix-stability "match" score and a per-exchange reused/added delta. The request
/// <see cref="FlowEvent.Data"/> is the indented JSON rendered by the backend's CapturingChatClient:
/// <c>{ instructions, messages:[{ role, contents:[{ type, text|arguments|result }] }], tools:[{ name, description, parameters }] }</c>.
/// No UI or state dependency.
/// </summary>
internal static class PromptSignatureBuilder
{
    // Category keys (also used as CSS modifier classes in PromptSignature.razor).
    private const string System = "system";
    private const string User = "user";
    private const string Assistant = "assistant";
    private const string Tool = "tool";

    private static readonly (string Key, string Label)[] CategoryOrder =
    {
        (System, "System"),
        (User, "User"),
        (Assistant, "Assistant"),
        (Tool, "Tool result"),
    };

    /// <summary>
    /// Builds the signature view from the last two <c>llm-request</c> events in <paramref name="events"/>.
    /// Returns <see cref="PromptSignatureView.Empty"/> when no request has been captured yet.
    /// </summary>
    /// <summary>
    /// Builds the signature view from the conversation's exchanges. Each exchange is a (label, events) pair —
    /// one completed (or in-progress) user message → answer turn — and its <em>representative</em> request is
    /// the last <c>llm-request</c> captured in it (the fullest prompt for that exchange). The view compares the
    /// last two exchanges (Comparison) and carries every exchange's delta (reused-prefix vs newly-added chars)
    /// for the growth view. Returns <see cref="PromptSignatureView.Empty"/> when no request has been captured.
    /// </summary>
    public static PromptSignatureView Build(IReadOnlyList<(string Label, IReadOnlyList<FlowEvent> Events)> exchanges)
    {
        // Representative request (label + payload) per exchange: the last captured llm-request in it.
        var reps = new List<(string Label, string Json)>(exchanges.Count);
        foreach (var (label, events) in exchanges)
        {
            for (var i = events.Count - 1; i >= 0; i--)
            {
                if (events[i].Kind == "llm-request" && !string.IsNullOrWhiteSpace(events[i].Data))
                {
                    reps.Add((label, events[i].Data!));
                    break;
                }
            }
        }

        if (reps.Count == 0)
        {
            return PromptSignatureView.Empty;
        }

        // Per-exchange breakdown with delta (reused prefix vs added). Each exchange re-sends the whole
        // previous exchange's prompt as its prefix, so the carried-over ("reused") part is exactly the
        // previous exchange's total and the delta is just this exchange's growth — making the bars chain
        // exactly (reused of N == total of N-1, and total[N-1] + added[N] == total[N]).
        var requests = new List<PromptSignatureRequest>(reps.Count);
        var prevTotal = 0;
        for (var i = 0; i < reps.Count; i++)
        {
            var cats = Categorize(reps[i].Json);
            var total = cats.Sum(c => c.Chars);
            var reused = i == 0 ? 0 : Math.Min(prevTotal, total);
            var added = total - reused;
            requests.Add(new PromptSignatureRequest(i + 1, reps[i].Label, cats, total, reused, added));
            prevTotal = total;
        }

        // Comparison = the last two exchanges; Match is the true byte-identical prefix share between them
        // (prompt-cache reuse), which is measured on the canonical-ordered payload and so can differ slightly
        // from the size-based reused/added split above.
        var current = requests[^1];
        var hasPrevious = requests.Count >= 2;
        var previous = hasPrevious ? requests[^2] : null;
        var match = hasPrevious
            ? MatchPercent(CanonicalForMatch(reps[^2].Json), CanonicalForMatch(reps[^1].Json))
            : 0;

        return new PromptSignatureView(
            previous?.Categories ?? Array.Empty<PromptSignatureCategory>(),
            current.Categories,
            previous?.TotalChars ?? 0,
            current.TotalChars,
            match,
            HasPrevious: hasPrevious,
            HasCurrent: true,
            requests);
    }

    /// <summary>Counts the characters each category contributes to one captured request payload.</summary>
    private static IReadOnlyList<PromptSignatureCategory> Categorize(string requestJson)
    {
        var counts = new Dictionary<string, int>
        {
            [System] = 0,
            [User] = 0,
            [Assistant] = 0,
            [Tool] = 0,
        };

        try
        {
            using var doc = JsonDocument.Parse(ToStrictJson(requestJson));
            var root = doc.RootElement;

            if (root.TryGetProperty("instructions", out var instructions) && instructions.ValueKind == JsonValueKind.String)
            {
                counts[System] += instructions.GetString()?.Length ?? 0;
            }

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    var role = message.TryGetProperty("role", out var r) ? r.GetString() : null;
                    var bucket = role switch
                    {
                        "system" => System,
                        "user" => User,
                        "assistant" => Assistant,
                        "tool" => Tool,
                        _ => User,
                    };
                    counts[bucket] += MessageChars(message);
                }
            }
        }
        catch (JsonException)
        {
            // Malformed capture — fall back to attributing the whole payload to the system prompt so the
            // bar still reflects the request size rather than collapsing to zero.
            counts[System] = requestJson.Length;
        }

        return CategoryOrder
            .Select(c => new PromptSignatureCategory(c.Key, c.Label, counts[c.Key]))
            .ToArray();
    }

    /// <summary>
    /// The backend captures requests as <em>display-only</em> JSON: escaped <c>\n</c>/<c>\r</c> sequences are
    /// turned into real line breaks so multi-line content renders on actual lines in the Steps view. Those
    /// raw control characters inside string literals make the payload invalid strict JSON (System.Text.Json
    /// rejects them), so this re-escapes any control character that appears inside a string back into its
    /// JSON escape, leaving the structural whitespace between tokens untouched.
    /// </summary>
    private static string ToStrictJson(string display)
    {
        var sb = new StringBuilder(display.Length + 16);
        var inString = false;
        var escaped = false;

        foreach (var ch in display)
        {
            if (!inString)
            {
                if (ch == '"')
                {
                    inString = true;
                }

                sb.Append(ch);
                continue;
            }

            if (escaped)
            {
                sb.Append(ch);
                escaped = false;
                continue;
            }

            switch (ch)
            {
                case '\\':
                    sb.Append(ch);
                    escaped = true;
                    break;
                case '"':
                    sb.Append(ch);
                    inString = false;
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (ch < 0x20)
                    {
                        sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(ch);
                    }

                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>Sums the text/arguments/result lengths across one message's content parts.</summary>
    private static int MessageChars(JsonElement message)
    {
        if (!message.TryGetProperty("contents", out var contents) || contents.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        var chars = 0;
        foreach (var content in contents.EnumerateArray())
        {
            if (content.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                chars += text.GetString()?.Length ?? 0;
            }

            if (content.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String)
            {
                chars += result.GetString()?.Length ?? 0;
            }

            if (content.TryGetProperty("arguments", out var arguments) && arguments.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                chars += arguments.GetRawText().Length;
            }
        }

        return chars;
    }

    /// <summary>
    /// The share (0–100) of the current request that is byte-identical to the previous one, measured as the
    /// longest common prefix over the current length — the same prefix reuse that prompt caching rewards.
    /// </summary>
    private static int MatchPercent(string previous, string current)
    {
        if (current.Length == 0)
        {
            return 0;
        }

        var common = 0;
        var max = Math.Min(previous.Length, current.Length);
        while (common < max && previous[common] == current[common])
        {
            common++;
        }

        return (int)Math.Round(common * 100.0 / current.Length);
    }

    /// <summary>
    /// Reorders a captured request into the conversation prefix a model caches against — the stable
    /// <em>system prompt</em> first, then the conversation messages in order — so the prefix match reflects
    /// real prompt-cache reuse. The static tool catalogue is excluded here too (consistent with the rest of
    /// the signature) so the match measures only the conversation content's prefix stability. Falls back to
    /// the raw payload when it cannot be parsed.
    /// </summary>
    private static string CanonicalForMatch(string requestJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(ToStrictJson(requestJson));
            var root = doc.RootElement;
            var sb = new StringBuilder(requestJson.Length);

            if (root.TryGetProperty("instructions", out var instructions) && instructions.ValueKind == JsonValueKind.String)
            {
                sb.Append(instructions.GetString());
            }

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    sb.Append(message.GetRawText());
                }
            }

            return sb.ToString();
        }
        catch (JsonException)
        {
            return requestJson;
        }
    }
}
