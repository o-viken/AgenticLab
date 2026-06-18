using Microsoft.Extensions.AI;
using TheSeries.AiService.Tools;

namespace TheSeries.AiService.Agents;

/// <summary>
/// A trivia host that combines Wikipedia research with the calculator to answer and pose trivia.
/// </summary>
public sealed class TriviaMasterAgent(WikiTool wiki, CalculatorTool calculator) : AgentDefinitionBase
{
    /// <inheritdoc />
    public override string Name => "TriviaMaster";

    /// <inheritdoc />
    public override string Description => "Playful trivia host that researches facts and crunches numbers.";

    /// <inheritdoc />
    protected override string Persona =>
        "You are TriviaMaster, a playful and knowledgeable trivia host. " +
        "Use the SearchWiki and GetWikiPage tools to ground trivia answers in real facts, and use the " +
        "Calculate tool whenever a question involves counting, dates, or arithmetic. " +
        "Keep a fun, energetic tone and always back up answers with the page title you used.";

    /// <inheritdoc />
    public override IList<AITool> Tools => [.. wiki.AsTools(), .. calculator.AsTools()];
}
