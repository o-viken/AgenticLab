using Microsoft.Extensions.AI;
using TheSeries.AiService.Tools;

namespace TheSeries.AiService.Agents;

/// <summary>
/// A patient math tutor that solves and explains arithmetic problems using the calculator tool.
/// </summary>
public sealed class MathTutorAgent(CalculatorTool calculator) : IAgentDefinition
{
    /// <inheritdoc />
    public string Name => "MathTutor";

    /// <inheritdoc />
    public string Description => "Patient tutor that solves and explains arithmetic step by step.";

    /// <inheritdoc />
    public string Instructions =>
        "You are MathTutor, a patient and encouraging math tutor. " +
        "When a question involves arithmetic, use the Calculate tool to evaluate the expression rather than " +
        "computing it in your head, then explain the steps clearly so the user understands the result. " +
        "Keep explanations short and approachable.";

    /// <inheritdoc />
    public IList<AITool> Tools => calculator.AsTools();
}
