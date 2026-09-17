namespace TheSeries.Web.Learning;

internal sealed record AgentExample(
    string Id, string Title, string Task, string Context,
    string LocalTools, string CloudTools, string LocalControl, string CloudControl,
    string ApplicationExamples);

internal sealed record AnatomyCapability(string Id, string Title, string Connection);
internal sealed record AnatomySkill(string Name, string Description, IReadOnlyList<string> Steps);
internal sealed record AnatomyExample(
    string Id, string Title, string Persona, string Task,
    IReadOnlyList<string> ToolIds, IReadOnlyList<AnatomySkill> Skills);
internal sealed record AnatomyPurpose(
    string Id, string Title, string ContextLabel, string SystemPrompt, string Model,
    string Controls, string Instructions, IReadOnlyList<AnatomyCapability> Capabilities,
    IReadOnlyList<AnatomyExample> Examples);

internal sealed class FoundationStory
{
    internal static IReadOnlyList<AnatomyCapability> AnatomyCapabilities { get; } = Array.AsReadOnly<AnatomyCapability>(
    [
        new("read", "Read files", "Local function"),
        new("search", "Search workspace", "Local function"),
        new("skill", "Read skill", "Local function"),
        new("ask", "Ask question", "Human input"),
        new("docs", "Documentation lookup", "MCP tool"),
        new("write", "Write files", "Local function"),
        new("terminal", "Terminal", "Local function"),
        new("delegate", "Delegate to specialist", "A2A agent"),
    ]);

    internal static IReadOnlyList<AnatomyExample> AnatomyExamples { get; } = Array.AsReadOnly<AnatomyExample>(
    [
        new("ask", "Ask", "Explain existing code from evidence. State what is known and what remains uncertain. Do not change files.",
            "Explain how this project handles a cancelled request.", ["read", "search", "skill"],
            [new("trace-request", "Follow a request from entry point to outcome.",
                ["Find the request entry point.", "Read the cancellation path and its callers.", "Explain the outcome with file references."]),
             new("explain-tests", "Use existing tests to explain expected behavior.",
                ["Locate tests for the behavior.", "Read their setup and assertions.", "Distinguish tested behavior from assumptions."])]),
        new("plan", "Plan", "Investigate before proposing a change. Ask about blocking ambiguity, then outline implementation and verification. Do not change files.",
            "Plan how to add request cancellation to this project.", ["read", "search", "skill", "ask"],
            [new("plan-change", "Build a bounded implementation and verification plan.",
                ["Read the owning code and nearby tests.", "Resolve any blocking ambiguity with the user.", "List the smallest changes and acceptance checks."]),
             new("check-dependencies", "Identify callers and dependencies affected by a change.",
                ["Locate callers of the affected code.", "Check their contracts and assumptions.", "Record compatibility constraints."])]),
        new("review", "Review", "Inspect correctness and regression risks. Ground findings in code and documentation, ordered by severity. Do not change files.",
            "Review the cancellation changes for race conditions.", ["read", "search", "skill", "docs"],
            [new("review-cancellation", "Inspect cancellation, cleanup and race conditions.",
                ["Read the changed path and its callers.", "Check cancellation and disposal against documentation.", "Report evidenced risks and missing tests."]),
             new("check-tests", "Assess whether tests cover the failure boundaries.",
                ["Identify expected failure boundaries.", "Compare them with existing test assertions.", "List missing regression scenarios."])]),
    ]);

    internal static IReadOnlyList<AnatomyPurpose> AnatomyPurposes { get; } = Array.AsReadOnly<AnatomyPurpose>(
    [
        new("chat", "Chat", "Conversational assistant",
            "Answer clearly using the conversation and permitted sources. Distinguish evidence from inference. Do not invent citations or tool results.",
            "General-purpose conversational model", "Only user-provided content and permitted sources. No publishing or file changes.",
            "Use plain language. Cite retrieved sources. Separate facts from assumptions.",
            [new("web", "Search public sources", "Search tool"), new("documents", "Read attachments", "Document tool"),
             new("calculate", "Calculate", "Local function"), new("skill", "Read skill", "Playbook reader"),
             new("publish", "Publish answer", "External action")],
            [new("chat-agent", "Chat agent", "Discuss questions, compare evidence and explain findings. Ask for clarification when the question is ambiguous.",
                "Compare heat pumps and electric heaters for a small home.", ["web", "documents", "calculate", "skill"],
                [new("compare-options", "Compare alternatives using the same criteria.",
                    ["Identify the user's constraints and criteria.", "Retrieve permitted sources for each alternative.", "Compare trade-offs and cite the supporting evidence."]),
                 new("check-sources", "Evaluate the relevance and reliability of sources.",
                    ["Check source dates and authorship.", "Compare claims across independent sources.", "Flag uncertainty and missing evidence."])])]),
        new("office", "Office", "Microsoft 365 Copilot-style assistant",
            "Help with work using content the signed-in user is allowed to access. Cite work sources, respect confidentiality, and request confirmation before sending communications.",
            "Work-grounded language model", "Access scoped to the signed-in user. Sending mail requires recipient, subject and body confirmation.",
            "Use the team's terminology. Cite meeting and document sources. Keep confidential details within the intended audience.",
            [new("mail", "Search email", "Work connector"), new("calendar", "Read calendar", "Work connector"),
             new("documents", "Search documents", "Work connector"), new("people", "Find people", "Work connector"),
             new("send", "Send email", "Confirmed action"), new("skill", "Read skill", "Playbook reader"),
             new("delete", "Delete documents", "External action")],
            [new("office-agent", "Office agent", "Prepare meetings and draft work communications from accessible mail, calendar and documents. Confirm the final message before sending it.",
                "Prepare a briefing for my project meeting and send the agreed summary after I confirm it.",
                ["mail", "calendar", "documents", "people", "send", "skill"],
                [new("meeting-brief", "Assemble a sourced briefing with decisions and open questions.",
                    ["Read the meeting agenda and relevant accessible work content.", "Summarize decisions, owners and unresolved questions with sources.", "Draft the briefing; confirm recipients and content before any send."]),
                 new("follow-up", "Turn meeting notes into an action-oriented follow-up.",
                    ["Identify actions and owners from the notes.", "Check names and dates against accessible sources.", "Draft a follow-up for user review."])])]),
        new("coding", "Coding", "Workspace coding assistant",
            "Ground answers in available evidence. Do not invent tool results. Be clear about uncertainty and report what was actually done.",
            "General-purpose code-capable model", "Read-only workspace access. No writes or shell execution in these modes.",
            "Cite file paths. Separate observations from suggestions. Use the project's terminology.",
            AnatomyCapabilities, AnatomyExamples),
        new("custom", "Custom", "Equipment monitoring assistant",
            "Investigate equipment alerts using approved telemetry and operating guidance. Cite measurements and times. Escalate uncertainty; never invent readings or control equipment.",
            "Domain-guided language model", "Read-only telemetry and approved manuals. No equipment control; escalate decisions to a human operator.",
            "Include equipment IDs, timestamps and units. Distinguish measured values from hypotheses. Follow site escalation guidance.",
            [new("telemetry", "Query measurements", "Telemetry API"), new("manuals", "Search manuals", "MCP tool"),
             new("calculate", "Calculate", "Local function"), new("skill", "Read skill", "Playbook reader"),
             new("delegate", "Consult specialist", "A2A agent"), new("control", "Change setpoint", "Equipment action")],
            [new("custom-agent", "Custom agent", "Investigate equipment alerts, compare measurements with approved limits, and provide a sourced assessment for the operator. Do not operate equipment.",
                "Investigate the high-temperature alert on pump P-204 and suggest checks for the operator.",
                ["telemetry", "manuals", "calculate", "skill"],
                [new("triage-alert", "Assess an alert using measurements and approved limits.",
                    ["Read recent measurements with timestamps and units.", "Compare observations with the approved operating guidance.", "Report evidence, uncertainty and recommended human checks."]),
                 new("validate-readings", "Check measurement quality before drawing conclusions.",
                    ["Check timestamps, units and missing samples.", "Compare the reading with nearby measurements.", "Flag suspect data for operator review."])])]),
    ]);

    internal static IReadOnlyList<AgentExample> Examples { get; } = Array.AsReadOnly<AgentExample>(
    [
        new("chat", "Chat", "Research a question", "Question, conversation and retrieved sources",
            "Search selected documents", "Search a connected knowledge base",
            "Only selected local documents", "Only sources allowed by the signed-in identity",
            "ChatGPT, Gemini, Claude"),
        new("coding", "Coding", "Write code and build applications", "Requirements, design, code, tests and delivery guidance across the software development lifecycle (SDLC)",
            "Read and edit project files; build and test locally", "Edit a repository checkout; run builds and tests in a sandbox",
            "Workspace access and command approval", "Scoped repository access and sandbox limits",
            "GitHub Copilot, Claude Code, Gemini Code Assist"),
        new("office", "Office", "Prepare for my next meeting", "Request, calendar details and relevant documents",
            "Read an exported agenda and selected notes", "Read a connected calendar and shared documents",
            "Only files explicitly provided", "Calendar and document access granted by the user",
            "Microsoft 365 Copilot, Gemini for Google Workspace"),
        new("custom", "Custom", "Investigate an equipment alert", "Alert, operating guidance and recent measurements",
            "Read a local measurement export", "Query a connected telemetry service",
            "Read-only access to the supplied dataset", "Read-only service identity; no equipment control",
            "An in-house equipment monitoring agent or a custom business application"),
    ]);

    internal string StageId { get; private set; } = "agent-landscape";
    internal int Beat { get; private set; }
    internal AgentExample Example { get; private set; } = Examples[1];
    internal AnatomyExample Anatomy { get; private set; } = AnatomyExamples[0];
    internal AnatomyPurpose AnatomyProfile { get; private set; } = AnatomyPurposes[2];
    internal AnatomySkill LoadedSkill => Anatomy.Skills[0];
    internal string Trigger { get; private set; } = "User request";
    internal static IReadOnlyList<string> Triggers { get; } = Array.AsReadOnly<string>(["User request", "Schedule", "Event"]);

    internal IReadOnlyList<string> Captions => StageId switch
    {
        "model-to-agent" => CompositionCaptions,
        "agents-everywhere" => EnvironmentCaptions,
        "anatomy-of-agent" => AnatomyCaptions,
        _ => LandscapeCaptions,
    };

    internal bool CanPrevious => Beat > 0;
    internal bool CanNext => Beat < Captions.Count - 1;
    internal string Caption => Captions[Beat];

    internal void SetStage(string stageId)
    {
        if (StageId == stageId) return;
        StageId = stageId;
        Beat = 0;
        if (stageId == "anatomy-of-agent") SelectAnatomyPurpose("coding");
    }

    internal void Move(int offset) => Beat = (int)Math.Clamp((long)Beat + offset, 0, Captions.Count - 1);
    internal void Complete() => Beat = Captions.Count - 1;
    internal void Restart() => Beat = 0;
    internal void SelectExample(string id) => Example = Examples.FirstOrDefault(example => example.Id == id) ?? Example;
    internal void SelectAnatomy(string id) => Anatomy = AnatomyProfile.Examples.FirstOrDefault(example => example.Id == id) ?? Anatomy;
    internal void SelectAnatomyPurpose(string id)
    {
        var purpose = AnatomyPurposes.FirstOrDefault(profile => profile.Id == id);
        if (purpose is null) return;
        AnatomyProfile = purpose;
        Anatomy = purpose.Examples[0];
    }
    internal void SelectTrigger(string trigger)
    {
        if (Triggers.Contains(trigger)) Trigger = trigger;
    }

    private static readonly IReadOnlyList<string> AnatomyCaptions = Array.AsReadOnly<string>(
    [
        "The system prompt supplies shared operating guidance. A prompt guides the model; it is not a security boundary.",
        "The harness has a catalogue of capabilities. Local functions and MCP tools perform actions; A2A delegates a task to another agent. Not every capability is exposed to every agent.",
        "The persona defines the agent's purpose and approach. Chat, office, coding and custom agents need different context and capabilities. Coding also offers Ask, Plan and Review modes.",
        "A configuration selects a tool subset and a model. The harness exposes only that subset and enforces permissions. A persona does not grant access by itself.",
        "The task prompt says what is wanted this time. It is a message, separate from the agent's standing instructions.",
        "Custom instructions add project guidance when applicable and enabled. Their text enters context directly; it is not a tool call.",
        "Skills are reusable playbooks. Initially, only names and descriptions enter context, so the model can choose relevant guidance without loading every body.",
        "An allowed Read skill call can load a relevant playbook into context. Its instructions guide the next decision; they do not execute actions or grant new tools.",
    ]);

    private static readonly IReadOnlyList<string> LandscapeCaptions = Array.AsReadOnly<string>(
    [
        "Agents serve many purposes. Chat, coding, office and custom agents are overlapping examples, not fixed categories.",
        "Across these examples, the same foundation appears: an application working with a model.",
        "The agent loop: the application prepares context, the model decides, and the application executes allowed tool requests. Observations return as context; an answer ends the turn.",
    ]);

    private static readonly IReadOnlyList<string> CompositionCaptions = Array.AsReadOnly<string>(
    [
        "Start with the two parts: Application + Model. Together, they form the agent.",
        "The application's harness assembles instructions and context, exposes tools, checks permissions and returns observations to context.",
        "The application executes tools such as search, calendar and file access. Requested actions are subject to permissions, approvals and limits.",
        "A model generates a response from the context it receives. It does not execute application tools itself.",
        "The application sends context, available tool definitions and any previous tool results to the model.",
        "The model returns an answer or a tool request. An answer can end the turn; a tool request goes back to the application for permission checks and execution.",
        "Application plus model forms the agent. A trigger starts a turn; an answer, enforced limit or cancellation can end it. A tool call is not required.",
    ]);

    private static readonly IReadOnlyList<string> EnvironmentCaptions = Array.AsReadOnly<string>(
    [
        "The shared foundation stays recognizable across different tasks. Purpose changes the context and capabilities an agent needs.",
        "An application can run locally or in the cloud while using a remote model. Tools and permissions depend on its environment; portability is not automatic.",
        "A user, schedule or event can start the work. The trigger is separate from the agent's purpose and where it runs. These are illustrative configurations, not product guarantees.",
    ]);
}