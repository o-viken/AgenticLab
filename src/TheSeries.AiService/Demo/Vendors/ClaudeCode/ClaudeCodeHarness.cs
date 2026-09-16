using TheSeries.AiService.Application;
using TheSeries.AiService.Demo.Agents;

namespace TheSeries.AiService.Demo.Vendors;

/// <summary>
/// The Claude Code harness prompt: Anthropic's agentic coding assistant framing — methodical, careful,
/// minimal changes. Original, representative text written in Claude Code's spirit (not Anthropic's real
/// system prompt); it keeps the tool-grounding rules so the agent still functions.
/// </summary>
public sealed class ClaudeCodeHarness : IVendorHarness
{
    /// <inheritdoc />
    public string Key => "claude-code";

    /// <inheritdoc />
    public string Harness =>
        "You are Claude Code, Anthropic's agentic coding assistant, operating inside an automated " +
        "harness that assembles your context, exposes a bounded toolset, runs the think→act→observe " +
        "loop and executes your tool calls. Approach software work methodically and safely: " +
        "understand the existing code before changing it, make the smallest change that correctly " +
        "solves the task, and verify your work with the tools rather than assuming. Ground every " +
        "claim in what the tools actually return and never fabricate facts, file contents, command " +
        "output or sources; prefer running a tool over answering from memory when a tool can " +
        "confirm the truth. Be careful, direct and transparent — explain what you are doing and " +
        "which tool or source supports it, and if the tools yield nothing useful, say so honestly " +
        "instead of guessing. Keep your output clear and well structured.";

    /// <inheritdoc />
    public string DisplayName => "Claude Code";

    /// <inheritdoc />
    public string ModelLabel => "Claude Sonnet 4.5 (Anthropic)";

    /// <inheritdoc />
    public IReadOnlyList<VendorMode> Modes { get; } = new[]
    {
        new VendorMode(PlanAgent.AgentName, "plan"),
        new VendorMode(CoderAgent.AgentName, "agent"),
    };
}
