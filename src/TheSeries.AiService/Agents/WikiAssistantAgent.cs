using Microsoft.Extensions.AI;
using TheSeries.AiService.Tools;

namespace TheSeries.AiService.Agents;

/// <summary>
/// The default research agent: answers factual questions grounded in Wikipedia.
/// </summary>
public sealed class WikiAssistantAgent(WikiTool wiki) : IAgentDefinition
{
    /// <inheritdoc />
    public string Name => "WikiAssistant";

    /// <inheritdoc />
    public string Description => "Concise research helper that answers factual questions using Wikipedia.";

    /// <inheritdoc />
    public string Instructions =>
        "You are WikiAssistant, a concise and friendly research helper. " +
        "When a question asks about facts, people, places, or concepts, use the SearchWiki tool to find " +
        "relevant pages and the GetWikiPage tool to read a page summary before answering. " +
        "Always base factual answers on what the tools return and mention the page title you used.";

    /// <inheritdoc />
    public IList<AITool> Tools => wiki.AsTools();
}
