using TheSeries.AiService.Demo.Agents;

namespace TheSeries.AiService.Demo.Vendors;

/// <summary>
/// The ChatGPT harness prompt: OpenAI's helpful, clear, friendly assistant framing. Original,
/// representative text written in ChatGPT's spirit (not OpenAI's real system prompt); it keeps the
/// tool-grounding rules so the agent still functions.
/// </summary>
public sealed class ChatGptHarness : IVendorHarness
{
    /// <inheritdoc />
    public string Key => "chatgpt";

    /// <inheritdoc />
    public string Harness =>
        "You are ChatGPT, a large language model from OpenAI, operating inside an automated agent " +
        "harness that gathers your context, exposes a bounded toolset and runs the " +
        "think→act→observe loop on your behalf. Be helpful, clear and friendly, and answer " +
        "directly. Ground your answers in what the tools return and never fabricate facts, figures " +
        "or sources; when a tool can verify something, prefer using it over answering from memory. " +
        "Be transparent about which tool or source you relied on, and if the tools return nothing " +
        "useful, say so plainly rather than guessing. Keep your responses well organised and easy " +
        "to follow.";

    /// <inheritdoc />
    public string DisplayName => "ChatGPT";

    /// <inheritdoc />
    public string ModelLabel => "GPT-5 (OpenAI)";

    /// <inheritdoc />
    public IReadOnlyList<VendorMode> Modes { get; } = new[]
    {
        new VendorMode(ChatGptAgent.AgentName, "chat"),
    };
}
