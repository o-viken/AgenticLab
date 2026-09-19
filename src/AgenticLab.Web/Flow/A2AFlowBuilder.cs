using System.Text.Json;

namespace AgenticLab.Web.Flow;

/// <summary>Projects only observed delegation boundaries; never reconstructs a remote agent's internals.</summary>
internal static class A2AFlowBuilder
{
    /// <summary>Builds agent states from a roster and an already bounded live or causal replay prefix.</summary>
    public static A2AFlowView Build(IReadOnlyList<A2AChip> roster, IReadOnlyList<FlowEvent> prefix, bool ended)
    {
        var calls = prefix.Where(stage => stage.Kind == "tool-call" &&
            string.Equals(stage.ToolCall?.Name, "DelegateToAgent", StringComparison.OrdinalIgnoreCase)).ToArray();
        var current = prefix.LastOrDefault();
        var selectedCall = current?.Kind == "tool-call" ? calls.FirstOrDefault(call => call == current)
            : current is { Kind: "tool-result", CallId: not null }
                ? calls.FirstOrDefault(call => call.CallId == current.CallId) : null;
        var target = Text(selectedCall?.ToolCall, "agentName")?.Trim();
        var active = roster.FirstOrDefault(agent => string.Equals(agent.Name, target, StringComparison.OrdinalIgnoreCase));
        var agents = new List<A2AAgentView>();
        foreach (var agent in roster)
        {
            var call = active == agent ? selectedCall : calls.LastOrDefault(candidate =>
                string.Equals(Text(candidate.ToolCall, "agentName")?.Trim(), agent.Name, StringComparison.OrdinalIgnoreCase));
            var result = call?.CallId is { } id
                ? prefix.FirstOrDefault(stage => stage.Kind == "tool-result" && stage.CallId == id) : null;
            agents.Add(new(agent,
                call is null ? "Available" : result is not null ? "Result returned"
                    : ended ? "No result captured" : "Delegation requested",
                Text(call?.ToolCall, "question"), result?.Data ?? result?.Detail));
        }

        return new(agents, active?.Name, active is null ? null : current?.Kind == "tool-result" ? "recv" : "send");
    }

    private static string? Text(FlowToolCall? call, string property) =>
        call?.Arguments is { ValueKind: JsonValueKind.Object } arguments &&
        arguments.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
}