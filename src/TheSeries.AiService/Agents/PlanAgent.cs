using Microsoft.Extensions.AI;
using TheSeries.AiService.Tools;

namespace TheSeries.AiService.Agents;

/// <summary>
/// A read-only "plan" mode agent: it investigates the user's workspace and produces a concrete
/// implementation plan without making any changes. It uses the read-only file tools
/// (<see cref="FileSystemTool.ReadFile"/> and <see cref="FileSystemTool.ListFiles"/>) plus
/// <see cref="AskQuestionTool.AskQuestion"/> to clarify a genuinely blocking ambiguity, and declares
/// <see cref="RequiresWorkspace"/>, but never writes, deletes or runs commands.
/// </summary>
public sealed class PlanAgent(FileSystemTool files, AskQuestionTool ask) : AgentDefinitionBase
{
    /// <inheritdoc />
    public override string Name => "Plan";

    /// <inheritdoc />
    public override string Description => "Read-only planner that investigates the workspace and proposes an implementation plan without changing anything.";

    /// <inheritdoc />
    public override bool RequiresWorkspace => true;

    /// <inheritdoc />
    protected override string Persona =>
        "You are Plan, a careful software architect operating in read-only mode. " +
        "Your job is to turn a request into a clear, actionable implementation plan — not to make the change. " +
        "Use the ListFiles tool to map the project and the ReadFile tool to study the relevant files before " +
        "you plan, and ground every step in what those tools reveal. " +
        "Produce an ordered list of concrete steps that name the specific files to create or edit and what " +
        "each change should do, call out risks or open questions, and keep it focused on the request. " +
        "You cannot write, delete or run anything — describe the steps for someone (or another agent) to carry out. " +
        "If a genuinely blocking ambiguity prevents you from planning, call the AskQuestion tool to ask the user — " +
        "ask sparingly and only when you cannot resolve it from the workspace; otherwise proceed and state your assumptions.";

    /// <inheritdoc />
    public override IList<AITool> Tools => [.. files.AsReadOnlyTools(), .. ask.AsTools()];
}
