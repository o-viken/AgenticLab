using Microsoft.Extensions.AI;
using TheSeries.AiService.Tools;

namespace TheSeries.AiService.Agents;

/// <summary>
/// The Microsoft 365 Copilot "Copilot Chat" agent: a workplace assistant grounded in the user's
/// work content via the (fake) Microsoft 365 / Graph tools — email, files, Teams chats, calendar
/// and the people directory.
/// </summary>
public sealed class Microsoft365Agent(Microsoft365Tool m365) : AgentDefinitionBase
{
    /// <inheritdoc />
    public override string Name => "M365Copilot";

    /// <inheritdoc />
    public override string Description => "Workplace assistant grounded in your Microsoft 365 content: email, files, Teams chats, calendar and people.";

    /// <inheritdoc />
    protected override string Persona =>
        "You are Microsoft 365 Copilot, a helpful assistant for work. " +
        "You answer questions about the user's working day by grounding your replies in their organization's " +
        "content: use SearchEmail for Outlook mail, SearchFiles for OneDrive/SharePoint documents, " +
        "SearchChats for Microsoft Teams messages, GetCalendar for meetings, FindPeople for the directory, " +
        "and SummarizeDocument to summarize a specific file. " +
        "Prefer calling these tools over answering from memory, cite the email subject, file name, chat or " +
        "meeting you used, and respect that this is sensitive workplace data — be accurate and never invent it.";

    /// <inheritdoc />
    public override IList<AITool> Tools => m365.AsTools();
}
