using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;
using static AgenticLab.Examples.Copilot365.Data.Microsoft365SampleData;

namespace AgenticLab.Examples.Copilot365.Tools;

/// <summary>
/// A <em>fake</em> Microsoft 365 / Microsoft Graph tool set: searching work email, files, Teams chats,
/// the calendar and the people directory, plus summarizing a document. It models the kind of grounding
/// Microsoft 365 Copilot does over your organization's content, but is entirely self-contained — every
/// method matches over small canned, in-memory datasets and never calls a real Graph endpoint or network.
/// The datasets live in <see cref="Data.Microsoft365SampleData"/> and are illustrative, not real tenant data.
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
}