using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;

namespace TheSeries.AiService.Tools;

/// <summary>
/// A <em>fake</em> Microsoft 365 / Microsoft Graph tool set: searching work email, files, Teams chats,
/// the calendar and the people directory, plus summarizing a document. It models the kind of grounding
/// Microsoft 365 Copilot does over your organization's content, but is entirely self-contained — every
/// method matches over small canned, in-memory datasets and never calls a real Graph endpoint or network.
/// The data below is illustrative sample content, not real tenant data.
/// </summary>
public sealed class Microsoft365Tool
{
    /// <summary>Searches the user's work email (Outlook) for messages matching a query.</summary>
    /// <param name="query">The words to look for in the subject, body or sender.</param>
    /// <returns>A formatted list of matching emails, or a message when nothing matches.</returns>
    [Description("Search the user's work email (Outlook) for messages matching a query. Returns matching emails with sender, date and a short snippet.")]
    public string SearchEmail(
        [Description("The words to look for, e.g. 'Q3 launch'.")] string query)
    {
        var hits = Filter(Emails, query, e => $"{e.Subject} {e.From} {e.Snippet}");
        if (hits.Count == 0)
        {
            return $"No work emails matched '{query}'.";
        }

        var sb = new StringBuilder();
        foreach (var e in hits)
        {
            sb.AppendLine($"- \"{e.Subject}\" — from {e.From} on {e.Date}: {e.Snippet}");
        }

        return sb.ToString();
    }

    /// <summary>Searches the user's files in OneDrive and SharePoint for documents matching a query.</summary>
    /// <param name="query">The words to look for in the file name or its description.</param>
    /// <returns>A formatted list of matching files, or a message when nothing matches.</returns>
    [Description("Search the user's OneDrive and SharePoint files for documents matching a query. Returns matching files with their location and a short description.")]
    public string SearchFiles(
        [Description("The words to look for, e.g. 'budget' or 'launch plan'.")] string query)
    {
        var hits = Filter(Files, query, f => $"{f.Name} {f.Location} {f.Description}");
        if (hits.Count == 0)
        {
            return $"No files matched '{query}'.";
        }

        var sb = new StringBuilder();
        foreach (var f in hits)
        {
            sb.AppendLine($"- {f.Name} ({f.Location}, modified {f.Modified}): {f.Description}");
        }

        return sb.ToString();
    }

    /// <summary>Searches the user's Teams chats and channel messages for messages matching a query.</summary>
    /// <param name="query">The words to look for in the message text or author.</param>
    /// <returns>A formatted list of matching messages, or a message when nothing matches.</returns>
    [Description("Search the user's Microsoft Teams chats and channel messages matching a query. Returns matching messages with author, channel and text.")]
    public string SearchChats(
        [Description("The words to look for, e.g. 'deployment' or a person's name.")] string query)
    {
        var hits = Filter(Chats, query, c => $"{c.Text} {c.Author} {c.Channel}");
        if (hits.Count == 0)
        {
            return $"No Teams messages matched '{query}'.";
        }

        var sb = new StringBuilder();
        foreach (var c in hits)
        {
            sb.AppendLine($"- {c.Author} in {c.Channel} ({c.When}): {c.Text}");
        }

        return sb.ToString();
    }

    /// <summary>Lists the user's upcoming calendar meetings, optionally filtered by a keyword.</summary>
    /// <param name="filter">Optional words to filter meetings by subject or attendee; empty returns all.</param>
    /// <returns>A formatted list of meetings, or a message when nothing matches.</returns>
    [Description("List the user's upcoming calendar meetings. Optionally pass a keyword to filter by subject or attendee; pass an empty string for everything.")]
    public string GetCalendar(
        [Description("Optional keyword to filter by, e.g. 'launch'. Pass an empty string for all meetings.")] string filter = "")
    {
        var hits = string.IsNullOrWhiteSpace(filter)
            ? Meetings
            : Filter(Meetings, filter, m => $"{m.Subject} {m.Attendees}");
        if (hits.Count == 0)
        {
            return $"No meetings matched '{filter}'.";
        }

        var sb = new StringBuilder();
        foreach (var m in hits)
        {
            sb.AppendLine($"- {m.When}: {m.Subject} (with {m.Attendees})");
        }

        return sb.ToString();
    }

    /// <summary>Looks up colleagues in the organization's directory by name, role or team.</summary>
    /// <param name="query">The words to look for in a person's name, role or team.</param>
    /// <returns>A formatted list of matching people, or a message when nothing matches.</returns>
    [Description("Look up colleagues in the organization's people directory by name, role or team. Returns matching people with their role and team.")]
    public string FindPeople(
        [Description("The words to look for, e.g. a name like 'Priya' or a role like 'designer'.")] string query)
    {
        var hits = Filter(People, query, p => $"{p.Name} {p.Role} {p.Team}");
        if (hits.Count == 0)
        {
            return $"No people matched '{query}'.";
        }

        var sb = new StringBuilder();
        foreach (var p in hits)
        {
            sb.AppendLine($"- {p.Name} — {p.Role}, {p.Team} ({p.Email})");
        }

        return sb.ToString();
    }

    /// <summary>Returns a short summary of a named document in the user's files.</summary>
    /// <param name="name">The file name (or part of it) to summarize.</param>
    /// <returns>The document's canned summary, or a message when no such file is found.</returns>
    [Description("Summarize a named document (Word, PowerPoint or PDF) from the user's files. Pass the file name or part of it.")]
    public string SummarizeDocument(
        [Description("The file name or part of it, e.g. 'launch plan'.")] string name)
    {
        var file = Filter(Files, name, f => f.Name).FirstOrDefault();
        if (file is null)
        {
            return $"No document matched '{name}'.";
        }

        return $"Summary of {file.Name} ({file.Location}): {file.Summary}";
    }

    /// <summary>
    /// <em>Sends</em> a work email on the user's behalf. Unlike the other tools this is a write/side-effecting
    /// action: it acts in the world rather than just reading, so it crosses a trust boundary and is the reason
    /// the Copilot Chat agent is rated a higher risk. This sample implementation does not really send anything
    /// — it validates the inputs and returns a confirmation of what <em>would</em> have been sent.
    /// </summary>
    /// <param name="to">The recipient's email address.</param>
    /// <param name="subject">The subject line of the email.</param>
    /// <param name="body">The body text of the email.</param>
    /// <returns>A confirmation of the (simulated) send, or a validation error when the inputs are unusable.</returns>
    [Description("Send a work email (Outlook) on the user's behalf to a recipient. This actually sends the message — only call it after the user has confirmed the recipient, subject and body.")]
    public string SendMail(
        [Description("The recipient's email address, e.g. 'priya.shah@contoso.com'.")] string to,
        [Description("The subject line.")] string subject,
        [Description("The body text of the email.")] string body)
    {
        if (string.IsNullOrWhiteSpace(to) || !to.Contains('@', StringComparison.Ordinal))
        {
            return $"Refused to send: '{to}' is not a valid email address.";
        }

        if (string.IsNullOrWhiteSpace(subject) && string.IsNullOrWhiteSpace(body))
        {
            return "Refused to send: the email has neither a subject nor a body.";
        }

        return $"Sent email to {to} — subject: \"{subject}\". The recipient will receive it shortly.";
    }

    /// <summary>Exposes the full Microsoft 365 capability set as AI tools (used by the Copilot Chat agent).</summary>
    public IList<AITool> AsTools() =>
    [
        AIFunctionFactory.Create(SearchEmail),
        AIFunctionFactory.Create(SearchFiles),
        AIFunctionFactory.Create(SearchChats),
        AIFunctionFactory.Create(GetCalendar),
        AIFunctionFactory.Create(FindPeople),
        AIFunctionFactory.Create(SummarizeDocument),
        AIFunctionFactory.Create(SendMail),
    ];

    /// <summary>Exposes the search/grounding subset used by the Researcher agent (no calendar).</summary>
    public IList<AITool> AsResearchTools() =>
    [
        AIFunctionFactory.Create(SearchEmail),
        AIFunctionFactory.Create(SearchFiles),
        AIFunctionFactory.Create(SearchChats),
        AIFunctionFactory.Create(FindPeople),
        AIFunctionFactory.Create(SummarizeDocument),
    ];

    /// <summary>Exposes the document subset used by the Analyst agent (files + summaries).</summary>
    public IList<AITool> AsAnalystTools() =>
    [
        AIFunctionFactory.Create(SearchFiles),
        AIFunctionFactory.Create(SummarizeDocument),
    ];

    private static IReadOnlyList<T> Filter<T>(IReadOnlyList<T> source, string query, Func<T, string> text)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return source
            .Where(item => terms.Any(t => text(item).Contains(t, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static readonly IReadOnlyList<Email> Emails =
    [
        new("Q3 launch — final go/no-go", "Priya Shah", "Jun 16", "Confirming we ship Thursday. Marketing assets are signed off; legal review is the last open item."),
        new("Re: Budget review FY26", "Marcus Lindqvist", "Jun 15", "Attached the revised forecast. We're 4% under plan after the cloud savings."),
        new("Notes from the customer call", "Aisha Okoro", "Jun 14", "The client wants SSO before rollout. I logged it as a blocker for the launch plan."),
        new("Team offsite logistics", "Tom Becker", "Jun 12", "Booked the venue for July 3. Please add your dietary preferences to the form."),
        new("Security training due", "IT Service Desk", "Jun 10", "Reminder: complete the annual security module by end of month."),
    ];

    private static readonly IReadOnlyList<WorkFile> Files =
    [
        new("Q3 Launch Plan.docx", "SharePoint › Marketing", "Jun 16", "The end-to-end plan for the Q3 product launch: timeline, owners and risks.",
            "A 9-page plan covering the launch timeline (key milestone: GA on Jun 25), workstream owners, the go-to-market message, and three open risks — SSO readiness, legal sign-off and supply of demo hardware."),
        new("FY26 Budget.xlsx", "OneDrive › Finance", "Jun 15", "The financial-year 2026 budget workbook with forecasts by team.",
            "A workbook tracking FY26 spend across eight teams. Total budget is $4.2M, currently 4% under plan, with the largest variance in cloud infrastructure (-12%)."),
        new("Customer Feedback Q2.pptx", "SharePoint › Product", "Jun 11", "A deck summarizing Q2 customer feedback themes.",
            "A 14-slide deck distilling Q2 feedback into four themes: onboarding friction, the SSO request, strong NPS for support (+62), and demand for a mobile app."),
        new("Onboarding Checklist.docx", "OneDrive › HR", "May 30", "The new-hire onboarding checklist.",
            "A one-page checklist of week-one tasks for new hires: accounts, hardware, buddy assignment and required training."),
    ];

    private static readonly IReadOnlyList<TeamsMessage> Chats =
    [
        new("Priya Shah", "Marketing › Launch", "today 09:14", "Reminder: go/no-go at 2pm. Bring the latest risk list."),
        new("Marcus Lindqvist", "Finance", "yesterday 16:40", "Cloud savings landed — we're officially under budget for the quarter."),
        new("Aisha Okoro", "Product › Customers", "yesterday 11:05", "Client confirmed SSO is a hard requirement before they expand the rollout."),
        new("Tom Becker", "Team", "Mon 13:20", "Offsite is locked for July 3, fill in the form when you get a sec."),
    ];

    private static readonly IReadOnlyList<Meeting> Meetings =
    [
        new("Today 14:00", "Q3 launch go/no-go", "Priya Shah, Aisha Okoro, you"),
        new("Today 16:30", "Finance forecast sync", "Marcus Lindqvist, you"),
        new("Tomorrow 10:00", "Product roadmap review", "Aisha Okoro, Tom Becker, you"),
        new("Fri 09:30", "1:1 with manager", "Marcus Lindqvist, you"),
    ];

    private static readonly IReadOnlyList<Person> People =
    [
        new("Priya Shah", "Launch Program Manager", "Marketing", "priya.shah@contoso.com"),
        new("Marcus Lindqvist", "Finance Lead", "Finance", "marcus.lindqvist@contoso.com"),
        new("Aisha Okoro", "Senior Product Manager", "Product", "aisha.okoro@contoso.com"),
        new("Tom Becker", "Engineering Manager", "Platform", "tom.becker@contoso.com"),
    ];

    private sealed record Email(string Subject, string From, string Date, string Snippet);

    private sealed record WorkFile(string Name, string Location, string Modified, string Description, string Summary);

    private sealed record TeamsMessage(string Author, string Channel, string When, string Text);

    private sealed record Meeting(string When, string Subject, string Attendees);

    private sealed record Person(string Name, string Role, string Team, string Email);
}
