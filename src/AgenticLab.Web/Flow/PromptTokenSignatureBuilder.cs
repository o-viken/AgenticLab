using System.Text.Json;
using Microsoft.ML.Tokenizers;

namespace AgenticLab.Web.Flow;

/// <summary>Counts captured input content with an explicit reference encoding, independently of provider usage and character views.</summary>
internal static class PromptTokenSignatureBuilder
{
    private static readonly Lazy<TiktokenTokenizer> O200k = new(() => TiktokenTokenizer.CreateForEncoding("o200k_base"));
    private static readonly Lazy<TiktokenTokenizer> Cl100k = new(() => TiktokenTokenizer.CreateForEncoding("cl100k_base"));

    public static PromptTokenSignatureView Build(
        IReadOnlyList<(string Label, IReadOnlyList<FlowEvent> Events)> exchanges, string encoding = "o200k_base")
    {
        var tokenizer = encoding switch
        {
            "o200k_base" => O200k.Value,
            "cl100k_base" => Cl100k.Value,
            _ => throw new ArgumentException("Unsupported reference encoding.", nameof(encoding)),
        };
        var requests = new List<PromptTokenSignatureRequest>();
        foreach (var (label, events) in exchanges)
        {
            var request = events.LastOrDefault(step => step.Kind == "llm-request");
            if (request is null) continue;
            var response = events.FirstOrDefault(step => step.Kind == "llm-response" && step.Turn == request.Turn);
            var categories = Categorize(request.Data, tokenizer);
            requests.Add(new(requests.Count + 1, label, request.Turn, categories,
                categories is null ? null : categories.Sum(category => category.Tokens),
                response is null ? null : TokenUsageBuilder.Build([response]),
                response is null && !events.Any(step => step.Kind is "error" or "final")));
        }
        return new(requests, encoding);
    }

    private static IReadOnlyList<PromptTokenCategory>? Categorize(string? payload, TiktokenTokenizer tokenizer)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        var counts = new Dictionary<string, int>
        {
            ["system"] = 0, ["user"] = 0, ["assistant"] = 0, ["tool"] = 0, ["definitions"] = 0, ["other"] = 0,
        };
        try
        {
            using var document = JsonDocument.Parse(PromptSignatureBuilder.ToStrictJson(payload));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("messages", out var messages)
                || messages.ValueKind != JsonValueKind.Array) return null;
            if (root.TryGetProperty("instructions", out var instructions) && instructions.ValueKind == JsonValueKind.String)
                counts["system"] += tokenizer.CountTokens(instructions.GetString()!);
            if (messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    var role = message.TryGetProperty("role", out var roleElement) ? roleElement.GetString() : null;
                    var bucket = role switch { "system" or "developer" => "system", "user" => "user", "assistant" => "assistant", "tool" => "tool", _ => "other" };
                    if (!message.TryGetProperty("contents", out var contents) || contents.ValueKind != JsonValueKind.Array) return null;
                    foreach (var content in contents.EnumerateArray())
                    {
                        var recognized = false;
                        foreach (var field in new[] { "text", "name", "arguments", "result" })
                        {
                            if (!content.TryGetProperty(field, out var value)) continue;
                            recognized = true;
                            counts[bucket] += Count(value);
                        }
                        if (!recognized) return null;
                    }
                }
            }
            if (root.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Array)
            {
                foreach (var tool in tools.EnumerateArray()) counts["definitions"] += Count(tool);
            }
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }

        return counts.Where(pair => pair.Key != "other" || pair.Value > 0).Select(pair => new PromptTokenCategory(
            pair.Key, pair.Key switch
            {
                "system" => "System", "user" => "User", "assistant" => "Assistant history",
                "tool" => "Tool results", "definitions" => "Tool definitions", _ => "Other",
            }, pair.Value)).ToArray();

        int Count(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.Null => 0,
            JsonValueKind.String => tokenizer.CountTokens(value.GetString()!),
            _ => tokenizer.CountTokens(JsonSerializer.Serialize(value)),
        };
    }
}

/// <summary>A content-only estimate; provider message framing and hidden transformations are not attributed to categories.</summary>
internal sealed record PromptTokenCategory(string Key, string Label, int Tokens);

/// <summary>The last captured input of an exchange and only its matching response's usage, when visible.</summary>
internal sealed record PromptTokenSignatureRequest(int Index, string Label, int Turn,
    IReadOnlyList<PromptTokenCategory>? Categories, int? EstimatedTokens, TokenUsageSummary? Usage, bool UsagePending)
{
    public string ActualInputLabel => UsagePending ? "pending" : Usage?.InputLabel ?? "not reported";
    public string CachedInputLabel => UsagePending ? "pending" : Usage?.CachedInputLabel ?? "not reported";
}

/// <summary>A reference-tokenizer comparison across representative inputs, never an exchange-wide usage total.</summary>
internal sealed record PromptTokenSignatureView(IReadOnlyList<PromptTokenSignatureRequest> Requests, string Encoding)
{
    public PromptTokenSignatureRequest? Current => Requests.LastOrDefault();
    public PromptTokenSignatureRequest? Previous => Requests.Count > 1 ? Requests[^2] : null;

    /// <summary>Only the adjacent visible exchanges participate in the displayed comparison.</summary>
    public IEnumerable<PromptTokenSignatureRequest> ComparedRequests => Requests.TakeLast(2);

    /// <summary>The common Delta scale excludes older exchanges and treats unavailable estimates as absent.</summary>
    public int MaxComparedTokens => Math.Max(Current?.EstimatedTokens ?? 0, Previous?.EstimatedTokens ?? 0);

    /// <summary>Signed input-size change, unavailable without both estimates; never a content-reuse measurement.</summary>
    public int? EstimatedChange => Current?.EstimatedTokens - Previous?.EstimatedTokens;
}