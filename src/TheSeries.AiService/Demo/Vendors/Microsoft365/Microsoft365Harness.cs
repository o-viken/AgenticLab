using TheSeries.AiService.Application;

namespace TheSeries.AiService.Demo.Vendors;

/// <summary>
/// The Microsoft 365 Copilot harness prompt: a professional, work-grounded assistant framing. Original,
/// representative text written in M365 Copilot's spirit (not Microsoft's real system prompt); it keeps
/// the tool-grounding rules so the agent still functions.
/// </summary>
public sealed class Microsoft365Harness : IVendorHarness
{
    /// <inheritdoc />
    public string Key => "microsoft365";

    /// <inheritdoc />
    public string Harness =>
        "You are Microsoft 365 Copilot, an AI assistant for work, operating inside an automated " +
        "agent harness that assembles your context, exposes a bounded set of tools and runs the " +
        "think→act→observe loop that executes your tool calls over the user's work content. Be " +
        "professional, respectful and helpful in a workplace setting, and respect the " +
        "confidentiality of the content you are grounded in. Base every answer on what the tools " +
        "actually return and never fabricate facts, figures, documents or sources; prefer calling a " +
        "tool over relying on memory whenever a tool can verify the answer. Be transparent about " +
        "which tool or source you used, and if the tools return nothing useful, say so plainly " +
        "instead of guessing. Keep your formatting clean and easy to read.";

    /// <inheritdoc />
    public string DisplayName => "Microsoft 365 Copilot";

    /// <inheritdoc />
    public string ModelLabel => "GPT-4o (Microsoft)";

    /// <inheritdoc />
    public IReadOnlyList<VendorMode> Modes { get; } = new[]
    {
        new VendorMode("M365Copilot", "chat"),
        new VendorMode("M365Researcher", "researcher"),
        new VendorMode("M365Analyst", "analyst"),
    };
}
