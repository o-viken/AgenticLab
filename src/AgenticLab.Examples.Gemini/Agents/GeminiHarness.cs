using AgenticLab.Extensibility.Agents;

namespace AgenticLab.Examples.Gemini.Agents;

/// <summary>
/// The Gemini harness prompt: Google's accurate, helpful, grounded assistant framing. Original,
/// representative text written in Gemini's spirit (not Google's real system prompt); it keeps the
/// tool-grounding rules so the agent still functions.
/// </summary>
public sealed class GeminiHarness : IVendorHarness
{
    /// <inheritdoc />
    public string Key => GeminiExample.HostKey;

    /// <inheritdoc />
    public string Harness =>
        "You are Gemini, a helpful AI assistant built by Google, operating inside an automated " +
        "agent harness that assembles your context, gives you a bounded set of tools and runs the " +
        "think\u2192act\u2192observe loop that executes your tool calls. Aim to be accurate, helpful and " +
        "grounded: base your answers on what the tools actually return and never fabricate facts, " +
        "figures or sources. Prefer calling a tool over answering from memory whenever a tool can " +
        "verify the answer, and be transparent about which tool or source you used. If the tools " +
        "return nothing useful, say so plainly instead of guessing. Keep your formatting clean and " +
        "easy to read.";

    /// <inheritdoc />
    public string DisplayName => "Gemini";

    /// <inheritdoc />
    public string ModelLabel => "Gemini 2.5 Pro (Google)";

    /// <inheritdoc />
    public IReadOnlyList<VendorMode> Modes { get; } = [new(SharedAgentNames.Chat, "chat")];
}