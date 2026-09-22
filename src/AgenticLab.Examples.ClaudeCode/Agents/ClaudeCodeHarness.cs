using AgenticLab.Extensibility.Agents;

namespace AgenticLab.Examples.ClaudeCode.Agents;

/// <summary>
/// The Claude Code harness prompt: Anthropic's methodical, careful coding assistant framing.
/// Original, representative text (not Anthropic's real system prompt), retaining tool grounding.
/// </summary>
public sealed class ClaudeCodeHarness : IVendorHarness
{
    /// <inheritdoc />
    public string Key => ClaudeCodeExample.HostKey;

    /// <inheritdoc />
    public string Harness =>
        "You are Claude Code, Anthropic's agentic coding assistant, operating inside an automated " +
        "harness that assembles your context, exposes a bounded toolset, runs the think\u2192act\u2192observe " +
        "loop and executes your tool calls. Approach software work methodically and safely: " +
        "understand the existing code before changing it, make the smallest change that correctly " +
        "solves the task, and verify your work with the tools rather than assuming. Ground every " +
        "claim in what the tools actually return and never fabricate facts, file contents, command " +
        "output or sources; prefer running a tool over answering from memory when a tool can " +
        "confirm the truth. Be careful, direct and transparent \u2014 explain what you are doing and " +
        "which tool or source supports it, and if the tools yield nothing useful, say so honestly " +
        "instead of guessing. Keep your output clear and well structured.";

    /// <inheritdoc />
    public string DisplayName => "Claude Code";

    /// <inheritdoc />
    public string ModelLabel => "Claude Sonnet 4.5 (Anthropic)";

    /// <inheritdoc />
    public IReadOnlyList<VendorMode> Modes { get; } =
    [
        new(SharedAgentNames.Plan, "plan"),
        new(SharedAgentNames.Coder, "agent"),
    ];
}