namespace TheSeries.Web.Learning;

internal sealed record LearningNode(string Id, string Title, string Detail);

internal sealed record LearningStage(
    string Id,
    string Title,
    string Summary,
    string Takeaway,
    string RealityLabel,
    string RealityDetail,
    bool PlatformMap,
    IReadOnlyList<string> ConceptIds,
    IReadOnlyList<string> HighlightedNodes,
    string? ActionLabel = null,
    string? ActionHref = null,
    bool Hidden = false);

internal static class AgentLearningJourney
{
    internal static IReadOnlyList<LearningNode> Nodes { get; } = Array.AsReadOnly<LearningNode>(
    [
        new("harness", "Harness", "Assembles context and runs the loop"),
        new("model", "Model", "Chooses an answer or a tool call"),
        new("instructions", "Instructions", "System rules, persona and task-specific guidance"),
        new("harness-context", "Context", "Conversation history and observations"),
        new("available-tools", "Tools", "Capabilities made available to the model"),
        new("execution-controls", "Execution controls", "Enforced permissions, limits and approvals"),
        new("anatomy-persona", "Agent persona", "Purpose and approach for the selected example"),
        new("anatomy-selected-tools", "Selected tools", "The configured subset exposed to this agent"),
        new("anatomy-settings", "Settings", "Model choice and enforced controls"),
        new("anatomy-task", "Task prompt", "The user's request for this turn"),
        new("anatomy-custom-instructions", "Custom instructions", "Applicable project guidance added to context"),
        new("anatomy-skills", "Skills", "Playbook descriptions, with a body loaded when needed"),
        new("loop-context", "Context", "The current instructions, messages and observations"),
        new("loop-decision", "Model decision", "Answer now or request a tool"),
        new("loop-execute", "Harness executes", "Runs the requested tool within its controls"),
        new("loop-observe", "Observation", "The result becomes context for the next decision"),
        new("loop-answer", "Final answer", "Ends this turn, even without a tool call"),
        new("connected-agent", "Agent", "Uses external capabilities when needed"),
        new("mcp-tools", "Tool server", "Exposes tools the agent can call"),
        new("a2a-agent", "Another agent", "Accepts delegated tasks and returns results"),
        new("foundry-runtime", "Agent runtime", "Runs a prompt agent or your hosted agent code"),
        new("foundry-model", "Model deployment", "Provides the model used for inference"),
        new("foundry-tools", "Connected tools", "Built-in capabilities, custom functions or MCP servers"),
        new("lifecycle-run", "Run", "Invoke a version on real or representative tasks"),
        new("lifecycle-observe", "Observe", "Inspect traces, results and failures"),
        new("lifecycle-evaluate", "Evaluate", "Check quality and safety against repeatable tests"),
        new("lifecycle-improve", "Improve", "Adjust instructions, tools or controls; test the next version"),
    ]);

    private static IReadOnlyList<LearningStage> AllStages { get; } = Array.AsReadOnly<LearningStage>(
    [
        new("model-to-agent", "Agent",
            "Build the foundation: a model paired with an application that prepares context and controls execution. Its agent-running part is the harness.",
            "Agent = Application + Model. The model requests actions; the application's harness executes them.",
            "Available today", "TheSeries runs a harness around an Azure OpenAI model. This diagram describes the architecture, not a live execution.", false,
            ["agent", "llm", "harness"], ["harness", "model"]),
        new("agent-landscape", "The Agentic Landscape",
            "Chat, coding, office and custom agents serve different purposes. Underneath, they share a recognizable foundation.",
            "Different purposes and products. The same underlying agentic principles.",
            "Illustrative", "This guide compares conceptual patterns. The live workbench offers representative agent personas, not replicas of commercial products.", false,
            ["agent", "harness", "tools"], ["harness", "model"]),
        new("inside-the-harness", "Inside the harness",
            "The harness prepares what the model can see and controls what it can do.",
            "Instructions guide the model. Enforced controls determine which actions can actually happen.",
            "Available today", "The live workbench exposes the assembled prompt, tool selection, workspace skills and execution controls.", false,
            ["system-prompt", "context", "tools", "guardrails"],
            ["instructions", "harness-context", "available-tools", "execution-controls"]),
        new("anatomy-of-agent", "Anatomy of an agent",
            "Shared guidance, an agent-specific configuration, and the context for one task. Build up the parts that work with the model.",
            "Instructions guide behavior. Tools enable actions. The harness enforces permissions. Agent = model + harness.",
            "Illustrative", "Chat, Office, Coding and Custom are teaching configurations, not product replicas or the backend roster. Office is inspired by Microsoft 365 Copilot; its connectors and playbooks are illustrative, not claims about native product features. No model or tool call is made.", false,
            ["persona", "tools", "custom-instructions", "skills"],
            ["instructions", "available-tools", "anatomy-persona", "anatomy-selected-tools", "anatomy-settings", "anatomy-task", "anatomy-custom-instructions", "anatomy-skills"]),
        new("agent-loop", "The agent loop",
            "Decide, act, observe, repeat. A tool result feeds the next decision; a final answer ends the turn.",
            "The model requests actions; the harness executes them. Each observation becomes new context.",
            "Available today", "Flow shows real model and tool events. Stepping and breakpoints pause backend execution, not just the animation.", false,
            ["reasoning", "tools", "context"], ["loop-context", "loop-decision", "loop-execute", "loop-observe", "loop-answer"],
            "Open live flow", "/"),
        new("agents-everywhere", "Same foundation, different setting",
            "Purpose, hosting and triggers are separate choices. Compare illustrative configurations while the foundation stays the same.",
            "Tools and permissions change with the environment. Local application does not mean local model.",
            "Illustrative", "These comparisons do not move or invoke agents. TheSeries runs application services around a remote Azure OpenAI model; the guide needs no model connection.", false,
            ["where-agents-run", "environment", "tools", "guardrails"], ["harness", "model"]),
        new("wider-ecosystem", "The wider ecosystem",
            "Agents can call external tools or delegate work to other agents. MCP and A2A standardize these two kinds of connection.",
            "A protocol standardizes a connection. It does not, by itself, make that connection safe.",
            "Available today", "TheSeries discovers a real MCP tool server and A2A agents. Their execution is separate from the model service.", false,
            ["mcp", "a2a", "environment"],
            ["connected-agent", "mcp-tools", "a2a-agent"],
            "Open discovery", "/discovery"),
        new("map-to-foundry", "Map to Microsoft Foundry",
            "A Foundry project organizes agents and connected resources. The familiar concepts map to managed platform capabilities.",
            "Hosting an agent and providing its model are different responsibilities.",
            "Conceptual mapping", "TheSeries uses Azure OpenAI, but this application is not a Foundry-hosted agent. This grouping is a conceptual mapping, not a deployment or security diagram.", true,
            ["where-agents-run", "llm", "tools"],
            ["foundry-runtime", "foundry-model", "foundry-tools"], Hidden: true),
        new("run-and-improve", "Run and improve",
            "Use evidence from runs and repeatable tests to improve the next version. This cycle spans versions, not tool calls within a turn.",
            "A trace shows what happened. An evaluation checks how well it worked.",
            "Today + future integration", "Today: real execution traces in Flow. Foundry hosting and managed evaluations remain future integrations, not features of this guide.", false,
            ["guardrails", "securing-agents"],
            ["lifecycle-run", "lifecycle-observe", "lifecycle-evaluate", "lifecycle-improve"],
            "Open live flow", "/"),
    ]);

    internal static IReadOnlyList<LearningStage> Stages { get; } =
        Array.AsReadOnly(AllStages.Where(stage => !stage.Hidden).ToArray());

    internal static LearningNode Node(string id) => Nodes.First(node => node.Id == id);

    internal static LearningStage Resolve(string? id) => Stages[IndexOf(id)];

    internal static int IndexOf(string? id)
    {
        for (var index = 0; index < Stages.Count; index++)
        {
            if (string.Equals(Stages[index].Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return 0;
    }

    internal static LearningStage Move(string? id, int offset) =>
        Stages[(int)Math.Clamp((long)IndexOf(id) + offset, 0, Stages.Count - 1)];

    internal static string Href(string id) => $"/learn?stage={Uri.EscapeDataString(id)}";
}