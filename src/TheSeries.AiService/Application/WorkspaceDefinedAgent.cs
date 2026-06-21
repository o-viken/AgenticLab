using Microsoft.Extensions.AI;

namespace TheSeries.AiService.Application;

/// <summary>
/// Adapts a <see cref="WorkspaceAgentDefinition"/> (loaded from the workspace's <c>agents/</c> folder)
/// to an <see cref="IAgentDefinition"/> so it reuses the same layered system prompt as the built-in
/// agents: the shared <see cref="AgentDefinitionBase.Harness"/> followed by the YAML-supplied
/// <see cref="Persona"/>. Workspace agents always require a workspace, since their tools operate inside
/// it. Built on demand per request by the <see cref="WorkspaceAgentResolver"/>; never registered at
/// start-up.
/// </summary>
internal sealed class WorkspaceDefinedAgent(WorkspaceAgentDefinition definition, IList<AITool> tools) : AgentDefinitionBase
{
    /// <inheritdoc />
    public override string Name => definition.Name;

    /// <inheritdoc />
    public override string Description => definition.Description;

    /// <inheritdoc />
    public override bool RequiresWorkspace => true;

    /// <inheritdoc />
    public override bool SupportsSkills => definition.SupportsSkills;

    /// <inheritdoc />
    public override AgentRiskLevel RiskLevel => definition.RiskLevel;

    /// <inheritdoc />
    public override string? ModelId => definition.ModelId;

    /// <inheritdoc />
    public override IReadOnlyList<string> Guardrails => definition.Guardrails;

    /// <inheritdoc />
    protected override string Persona => definition.Persona;

    /// <inheritdoc />
    public override IList<AITool> Tools => tools;
}
