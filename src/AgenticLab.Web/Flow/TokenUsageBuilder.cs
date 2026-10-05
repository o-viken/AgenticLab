namespace AgenticLab.Web.Flow;

/// <summary>Aggregates only completed local model responses within one exchange, preserving missing-field coverage.</summary>
internal static class TokenUsageBuilder
{
    public static TokenUsageSummary Build(IReadOnlyList<FlowEvent> events, bool running = false)
    {
        var responses = events.Where(step => step.Kind == "llm-response").DistinctBy(step => step.Turn).ToArray();
        var calls = events.Where(step => step.Kind is "llm-request" or "llm-response")
            .Select(step => step.Turn).Distinct().Count();
        var input = Count(usage => usage.InputTokenCount);
        var output = Count(usage => usage.OutputTokenCount);
        var total = Count(usage => usage.TotalTokenCount);
        var cached = Count(usage => usage.CachedInputTokenCount);
        decimal? cachedPercent = null;
        if (calls > 0 && input.ReportedCalls == calls && cached.ReportedCalls == calls && input.Value > 0 &&
            responses.All(step => step.Usage!.CachedInputTokenCount <= step.Usage.InputTokenCount))
        {
            cachedPercent = 100m * cached.Value / input.Value;
        }

        return new(input, output, total, cached, calls, running, cachedPercent);

        TokenUsageCount Count(Func<FlowTokenUsage, long?> select)
        {
            long? sum = null;
            var reported = 0;
            foreach (var response in responses)
            {
                if (response.Usage is { } usage && select(usage) is >= 0 and var value)
                {
                    sum = (sum ?? 0) + value;
                    reported++;
                }
            }
            return new(sum, reported);
        }
    }
}