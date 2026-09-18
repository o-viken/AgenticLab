using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace TheSeries.AiService.Application.Flow;

/// <summary>
/// The per-run ambient scopes both chat endpoints open for an agent run — the workspace, the disabled
/// tools, the disabled skills, the enabled custom instructions and (for a streaming run) the user-input
/// channel — bundled so they are begun, re-activated and disposed together. Every scope is an
/// <see cref="AsyncLocal{T}"/> that is reset whenever an async iterator resumes after a <c>yield</c>, so
/// a streaming caller must call <see cref="Activate"/> before each agent advance.
/// </summary>
public sealed class RunScopeSet : IDisposable
{
    private readonly WorkspaceScope? _workspace;
    private readonly ToolFilterScope? _tools;
    private readonly SkillFilterScope? _skills;
    private readonly InstructionFilterScope _instructions;

    private RunScopeSet(WorkspaceScope? workspace, ToolFilterScope? tools, SkillFilterScope? skills, InstructionFilterScope instructions, UserInputScope? userInput)
    {
        _workspace = workspace;
        _tools = tools;
        _skills = skills;
        _instructions = instructions;
        UserInput = userInput;
    }

    /// <summary>The scope an <c>AskQuestion</c> tool blocks on; null for a non-interactive run.</summary>
    public UserInputScope? UserInput { get; }

    /// <summary>
    /// Begins the run's scopes on the current async context. The workspace scope (if any) is expected to be
    /// already open — it is only tracked here so <see cref="Activate"/> can re-assert it.
    /// </summary>
    /// <param name="workspace">The already-opened workspace scope, or null when the agent needs none.</param>
    /// <param name="disabledTools">Tool names hidden from the model for this run.</param>
    /// <param name="disabledSkills">Skill names neither listed nor loadable this run (skills default on).</param>
    /// <param name="enabledInstructions">Custom instructions injected this run (instructions default off).</param>
    /// <param name="interactive">Whether to open a <see cref="UserInputScope"/> so a tool can ask the user a question.</param>
    public static RunScopeSet Begin(
        WorkspaceScope? workspace,
        IReadOnlyList<string>? disabledTools,
        IReadOnlyList<string>? disabledSkills,
        IReadOnlyList<string>? enabledInstructions,
        bool interactive)
    {
        var tools = disabledTools is { Count: > 0 } ? ToolFilterScope.Begin(disabledTools) : null;
        var skills = disabledSkills is { Count: > 0 } ? SkillFilterScope.Begin(disabledSkills) : null;
        var instructions = InstructionFilterScope.Begin(enabledInstructions ?? Array.Empty<string>());
        var userInput = interactive ? UserInputScope.Begin() : null;
        return new RunScopeSet(workspace, tools, skills, instructions, userInput);
    }

    /// <summary>Re-asserts every scope on the current async context.</summary>
    public void Activate()
    {
        _workspace?.Activate();
        _tools?.Activate();
        _skills?.Activate();
        _instructions.Activate();
        UserInput?.Activate();
    }

    /// <summary>
    /// Builds run options that append the active workspace's enabled custom instructions and (when the
    /// agent supports skills) its skill catalogue to the agent's instructions for this run only, or null
    /// when neither is present. The scopes must be active when this is called.
    /// </summary>
    public static AgentRunOptions? BuildRunOptions(bool supportsSkills, SkillLoader skills, InstructionLoader instructions)
    {
        var instructionBlock = instructions.BuildContextBlock();
        var skillBlock = supportsSkills ? skills.BuildContextBlock() : null;
        var combined = string.Join("\n", new[] { instructionBlock, skillBlock }.Where(b => !string.IsNullOrEmpty(b)));
        return string.IsNullOrEmpty(combined)
            ? null
            : new ChatClientAgentRunOptions(new ChatOptions { Instructions = combined });
    }

    public void Dispose()
    {
        UserInput?.Dispose();
        _instructions.Dispose();
        _skills?.Dispose();
        _tools?.Dispose();
    }
}
