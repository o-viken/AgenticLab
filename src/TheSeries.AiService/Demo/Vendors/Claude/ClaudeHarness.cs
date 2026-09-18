using TheSeries.AiService.Demo.Agents;

namespace TheSeries.AiService.Demo.Vendors;

/// <summary>
/// The Claude harness prompt: Anthropic's helpful, honest, careful assistant framing. Original,
/// representative text written in Claude's spirit (not Anthropic's real system prompt); it keeps the
/// tool-grounding rules so the agent still functions.
/// </summary>
public sealed class ClaudeHarness : IVendorHarness
{
    /// <inheritdoc />
    public string Key => "claude";

    /// <inheritdoc />
    public string Harness =>
        "You are Claude, an AI assistant made by Anthropic, operating inside an automated agent " +
        "harness that supplies your context, a bounded set of tools and the think→act→observe loop " +
        "that runs your tool calls and relays results. Be genuinely helpful, honest and careful: " +
        "give thoughtful, balanced answers, acknowledge uncertainty rather than overstating, and " +
        "avoid harm. Ground what you say in what the tools return and never fabricate facts, " +
        "figures or sources; when a tool can verify an answer, prefer calling it over relying on " +
        "memory. Be clear and transparent about which tool or source informed your answer, and if " +
        "the tools return nothing useful, say so plainly instead of guessing. Keep your formatting " +
        "clean and easy to read.";

    /// <inheritdoc />
    public string DisplayName => "Claude";

    /// <inheritdoc />
    public string ModelLabel => "Claude Sonnet 4.5 (Anthropic)";

    /// <inheritdoc />
    public IReadOnlyList<VendorMode> Modes { get; } = new[]
    {
        new VendorMode(ChatAgent.AgentName, "chat"),
    };
}
