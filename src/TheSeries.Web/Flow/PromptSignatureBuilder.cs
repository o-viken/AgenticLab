using System.Text;
using System.Text.Json;

namespace TheSeries.Web.Flow;

/// <summary>
/// Pure helpers that turn the captured <c>llm-request</c> payloads into a <see cref="PromptSignatureView"/>:
/// the most recent request broken down by message category (system prompt, user, assistant, tool result and
/// the tool catalogue) compared against the previous request, plus a prefix-stability "match" score. The
/// request <see cref="FlowEvent.Data"/> is the indented JSON rendered by the backend's CapturingChatClient:
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
    private const string Tools = "tools";

    private static readonly (string Key, string Label)[] CategoryOrder =
    {
        (System, "System"),
        (User, "User"),
        (Assistant, "Assistant"),
        (Tool, "Tool result"),
        (Tools, "Tools catalogue"),
    };

    /// <summary>
    /// Builds the signature view from the last two <c>llm-request</c> events in <paramref name="events"/>.
    /// Returns <see cref="PromptSignatureView.Empty"/> when no request has been captured yet.
    /// </summary>
    public static PromptSignatureView Build(IReadOnlyList<FlowEvent> events)
    {
        string? current = null;
        string? previous = null;
        for (var i = events.Count - 1; i >= 0; i--)
        {
            if (events[i].Kind != "llm-request" || string.IsNullOrWhiteSpace(events[i].Data))
            {
                continue;
            }

            if (current is null)
            {
                current = events[i].Data;
            }
            else
            {
                previous = events[i].Data;
                break;
            }
        }

        if (current is null)
        {
            return PromptSignatureView.Empty;
        }

        var currentCats = Categorize(current);
        var previousCats = previous is null ? Array.Empty<PromptSignatureCategory>() : Categorize(previous);

        var currentChars = currentCats.Sum(c => c.Chars);
        var previousChars = previousCats.Sum(c => c.Chars);
        var match = previous is null ? 0 : MatchPercent(CanonicalForMatch(previous), CanonicalForMatch(current));

        return new PromptSignatureView(
            previousCats,
            currentCats,
            previousChars,
            currentChars,
            match,
            HasPrevious: previous is not null,
            HasCurrent: true);
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
            [Tools] = 0,
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

            if (root.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Array)
            {
                foreach (var tool in tools.EnumerateArray())
                {
                    counts[Tools] += ToolChars(tool);
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

    /// <summary>Sums a tool definition's name, description and parameter-schema lengths.</summary>
    private static int ToolChars(JsonElement tool)
    {
        var chars = 0;
        if (tool.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
        {
            chars += name.GetString()?.Length ?? 0;
        }

        if (tool.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.String)
        {
            chars += description.GetString()?.Length ?? 0;
        }

        if (tool.TryGetProperty("parameters", out var parameters) && parameters.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            chars += parameters.GetRawText().Length;
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
    /// Reorders a captured request into the canonical order a model provider caches against — the stable
    /// <em>system prompt + tool catalogue</em> first, then the conversation messages in order — so the
    /// prefix match reflects real prompt-cache reuse. The raw capture serializes <c>tools</c> <em>after</em>
    /// <c>messages</c>, so a mid-conversation change (e.g. a growing tool result during the agent loop) would
    /// otherwise break the prefix before reaching the byte-identical tool catalogue and badly understate the
    /// match. Falls back to the raw payload when it cannot be parsed.
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

            if (root.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Array)
            {
                foreach (var tool in tools.EnumerateArray())
                {
                    sb.Append(tool.GetRawText());
                }
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
