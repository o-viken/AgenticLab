using Microsoft.Extensions.AI;

namespace AgenticLab.AiService.Application.Agents;

/// <summary>
/// Base class for agent definitions. It supplies the shared <em>harness</em> system prompt
/// (the common operating rules every agent runs under) and composes it with each agent's own
/// <see cref="Persona"/> to form the final <see cref="Instructions"/>. Concrete agents inherit
/// from this type and only provide their name, description, persona and tool subset.
/// </summary>
public abstract class AgentDefinitionBase : IAgentDefinition
{
    /// <summary>
    /// The shared harness system prompt prepended to every agent's persona. These are the
    /// cross-cutting rules that apply regardless of which agent is selected. Override only when
    /// an agent needs to replace the shared rules entirely.
    /// </summary>
    protected virtual string Harness =>
        "You are the model inside an automated agent harness: together you and the harness form the agent. " +
        "The harness assembles your context, exposes a bounded set of tools, runs the think→act→observe " +
        "loop on your behalf, executes the tool calls you request, and relays the results back to you and " +
        "your replies back to the user. Follow these rules on every turn: " +
        "ground your answers in what the tools return and never fabricate facts, figures, or sources; " +
        "prefer calling a tool over answering from memory when a tool can verify the answer; " +
        "be accurate and concise, and be transparent about which tool or source you used; " +
        "if the tools return nothing useful, say so plainly instead of guessing; " +
        "keep your formatting clean and easy to read.";

    /// <summary>
    /// The agent-specific persona and behavioural instructions, layered on top of <see cref="Harness"/>.
    /// </summary>
    protected abstract string Persona { get; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <inheritdoc />
    /// <remarks>Defaults to <c>null</c> (the global default deployment); agents override this to declare a preferred model.</remarks>
    public virtual string? ModelId => null;

    /// <inheritdoc />
    /// <remarks>Defaults to <c>false</c>; agents that need a workspace override this to return <c>true</c>.</remarks>
    public virtual bool RequiresWorkspace => false;

    /// <inheritdoc />
    /// <remarks>Defaults to <c>false</c>; workspace agents that want skills override this to return <c>true</c>.</remarks>
    public virtual bool SupportsSkills => false;

    /// <inheritdoc />
    /// <remarks>Defaults to <c>false</c>; agents that use MCP-discovered tools override this to return <c>true</c>.</remarks>
    public virtual bool SupportsMcp => false;

    /// <inheritdoc />
    /// <remarks>Defaults to <c>false</c>; agents that delegate to another agent over A2A override this to return <c>true</c>.</remarks>
    public virtual bool SupportsA2A => false;

    /// <inheritdoc />
    /// <remarks>Defaults to <see cref="AgentRiskLevel.None"/>; agents with tools that have side effects override this.</remarks>
    public virtual AgentRiskLevel RiskLevel => AgentRiskLevel.None;

    /// <inheritdoc />
    /// <remarks>Defaults to an empty list; agents override this to surface the guardrails enforced for them.</remarks>
    public virtual IReadOnlyList<string> Guardrails => Array.Empty<string>();

    /// <inheritdoc />
    public abstract IList<AITool> Tools { get; }

    /// <summary>
    /// The full system instructions supplied to the agent: the shared <see cref="Harness"/> prompt
    /// scoped in <c>&lt;harnessMode&gt;</c> tags, followed by this agent's <see cref="Persona"/>
    /// scoped in <c>&lt;agentMode&gt;</c> tags.
    /// </summary>
    public string Instructions => InstructionsWith(null);

    /// <inheritdoc />
    public string InstructionsWith(string? harnessOverride)
    {
        var harness = string.IsNullOrWhiteSpace(harnessOverride) ? Harness : harnessOverride;
        return $"<harnessMode>\n{harness}\n</harnessMode>\n\n<agentMode>\n{Persona}\n</agentMode>";
    }

    /// <inheritdoc />
    public string HarnessPrompt => Harness;
}
