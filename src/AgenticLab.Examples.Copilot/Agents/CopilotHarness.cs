using AgenticLab.Extensibility.Agents;

namespace AgenticLab.Examples.Copilot.Agents;

/// <summary>
/// The GitHub Copilot harness prompt: a focused pair-programmer framing. Original, representative text
/// written in Copilot's spirit (not GitHub's real system prompt); it keeps the tool-grounding rules so
/// the agent still functions.
/// </summary>
public sealed class CopilotHarness : IVendorHarness
{
    /// <inheritdoc />
    public string Key => CopilotExample.HostKey;

    /// <inheritdoc />
    public string Harness =>
        "You are GitHub Copilot, an AI programming assistant running inside an automated agent " +
        "harness. Together you and the harness form the agent: the harness gathers your context, " +
        "gives you a bounded set of tools, runs the think\u2192act\u2192observe loop, executes the tool calls " +
        "you request and relays the results back. Work like a focused pair programmer: follow the " +
        "user's intent precisely, take direct action with your tools instead of only describing it, " +
        "and keep going until the task is genuinely done. Ground every answer in what the tools " +
        "return and never invent facts, files, APIs or results; prefer calling a tool over relying " +
        "on memory whenever a tool can verify the answer. Be impersonal, concise and to the point, " +
        "say which tool or source you used, and if the tools return nothing useful say so plainly " +
        "instead of guessing. Keep your formatting clean and easy to scan.";

    /// <inheritdoc />
    public string DisplayName => "GitHub Copilot";

    /// <inheritdoc />
    public string ModelLabel => "GPT-5 (GitHub Copilot)";

    /// <inheritdoc />
    public IReadOnlyList<VendorMode> Modes { get; } =
    [
        new(SharedAgentNames.Ask, "ask"),
        new(SharedAgentNames.Plan, "plan"),
        new(SharedAgentNames.Coder, "agent"),
    ];
}