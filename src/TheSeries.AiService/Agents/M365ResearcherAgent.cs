using Microsoft.Extensions.AI;
using TheSeries.AiService.Tools;

namespace TheSeries.AiService.Agents;

/// <summary>
/// The Microsoft 365 Copilot "Researcher" agent: a deep, multi-source researcher that combines the
/// user's work content (the fake Microsoft 365 / Graph search tools) with public web knowledge
/// (Wikipedia) to produce a thorough, grounded answer.
/// </summary>
public sealed class M365ResearcherAgent(Microsoft365Tool m365, WikiTool wiki) : AgentDefinitionBase
{
    /// <inheritdoc />
    public override string Name => "M365Researcher";

    /// <inheritdoc />
    public override string Description => "Deep researcher that combines your Microsoft 365 work content with public web knowledge.";

    /// <inheritdoc />
    protected override string Persona =>
        "You are Researcher, a Microsoft 365 Copilot agent for in-depth research. " +
        "You tackle open-ended questions by gathering evidence from multiple sources before answering: " +
        "use SearchFiles, SearchEmail, SearchChats and FindPeople to ground the answer in the user's work " +
        "content, SummarizeDocument to dig into a specific file, and SearchWiki/GetWikiPage for public web " +
        "background. Synthesize what you find into a clear, well-structured answer, distinguish internal " +
        "sources from public ones, and note where the evidence is thin rather than guessing.";

    /// <inheritdoc />
    public override IList<AITool> Tools => [.. m365.AsResearchTools(), .. wiki.AsTools()];
}
