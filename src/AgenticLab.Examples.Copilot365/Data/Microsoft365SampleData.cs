using AgenticLab.Examples.Copilot365.Tools;

namespace AgenticLab.Examples.Copilot365.Data;

/// <summary>
/// The canned, in-memory "tenant" the fake <see cref="Microsoft365Tool"/> searches over: a handful of
/// emails, files, Teams messages, meetings and people that cross-reference each other (the Q3 launch, the
/// SSO blocker, the FY26 budget) so multi-step research questions have something to find. Illustrative
/// sample content only — never real tenant data.
/// </summary>
internal static class Microsoft365SampleData
{
    internal sealed record Email(string Subject, string From, string Date, string Snippet);

    internal sealed record WorkFile(string Name, string Location, string Modified, string Description, string Summary);

    internal sealed record TeamsMessage(string Author, string Channel, string When, string Text);

    internal sealed record Meeting(string When, string Subject, string Attendees);

    internal sealed record Person(string Name, string Role, string Team, string Email);

    internal static readonly IReadOnlyList<Email> Emails =
    [
        new("Q3 launch — final go/no-go", "Priya Shah", "Jun 16", "Confirming we ship Thursday. Marketing assets are signed off; legal review is the last open item."),
        new("Re: Budget review FY26", "Marcus Lindqvist", "Jun 15", "Attached the revised forecast. We're 4% under plan after the cloud savings."),
        new("Notes from the customer call", "Aisha Okoro", "Jun 14", "The client wants SSO before rollout. I logged it as a blocker for the launch plan."),
        new("Team offsite logistics", "Tom Becker", "Jun 12", "Booked the venue for July 3. Please add your dietary preferences to the form."),
        new("Security training due", "IT Service Desk", "Jun 10", "Reminder: complete the annual security module by end of month."),
    ];

    internal static readonly IReadOnlyList<WorkFile> Files =
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

    internal static readonly IReadOnlyList<TeamsMessage> Chats =
    [
        new("Priya Shah", "Marketing › Launch", "today 09:14", "Reminder: go/no-go at 2pm. Bring the latest risk list."),
        new("Marcus Lindqvist", "Finance", "yesterday 16:40", "Cloud savings landed — we're officially under budget for the quarter."),
        new("Aisha Okoro", "Product › Customers", "yesterday 11:05", "Client confirmed SSO is a hard requirement before they expand the rollout."),
        new("Tom Becker", "Team", "Mon 13:20", "Offsite is locked for July 3, fill in the form when you get a sec."),
    ];

    internal static readonly IReadOnlyList<Meeting> Meetings =
    [
        new("Today 14:00", "Q3 launch go/no-go", "Priya Shah, Aisha Okoro, you"),
        new("Today 16:30", "Finance forecast sync", "Marcus Lindqvist, you"),
        new("Tomorrow 10:00", "Product roadmap review", "Aisha Okoro, Tom Becker, you"),
        new("Fri 09:30", "1:1 with manager", "Marcus Lindqvist, you"),
    ];

    internal static readonly IReadOnlyList<Person> People =
    [
        new("Priya Shah", "Launch Program Manager", "Marketing", "priya.shah@contoso.com"),
        new("Marcus Lindqvist", "Finance Lead", "Finance", "marcus.lindqvist@contoso.com"),
        new("Aisha Okoro", "Senior Product Manager", "Product", "aisha.okoro@contoso.com"),
        new("Tom Becker", "Engineering Manager", "Platform", "tom.becker@contoso.com"),
    ];
}