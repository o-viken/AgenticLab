namespace AgenticLab.Web.Learning;

internal sealed record HostingOption(
    string Id, string Label, string Fit, string Runtime, string Model, string Access,
    string Owner, string Tradeoff, string Availability);

internal sealed record HostingReadiness(string Id, string Label, string Identity, string State, string Reliability, string Control);

internal sealed record HostingExample(string Name, string Vendor, string Kind, string Detail, string Url, IReadOnlyList<string> OptionIds);

internal sealed class HostingStory
{
    internal static IReadOnlyList<HostingExample> Examples { get; } = Array.AsReadOnly<HostingExample>(
    [
        new("Microsoft Agent Framework", "Microsoft", "Framework",
            "Build agent and workflow code that runs locally or in your own service. The framework is not a hosting service; Foundry is a separate managed hosting option.",
            "https://learn.microsoft.com/en-us/agent-framework/overview/", ["local", "service"]),
        new("OpenAI Agents SDK", "OpenAI", "SDK",
            "Run an agent loop in your application, locally or on compute you operate. The SDK and model API are not a deployment destination or a ChatGPT customization.",
            "https://openai.github.io/openai-agents-python/", ["local", "service"]),
        new("Claude Agent SDK", "Anthropic", "SDK",
            "Embed Claude's agent loop and tools in your own application runtime. Claude Managed Agents is a separate hosted offering, not the same SDK deployed automatically.",
            "https://code.claude.com/docs/en/agent-sdk/overview", ["local", "service"]),
        new("Agent Development Kit (ADK)", "Google", "Framework",
            "Build and test agent code locally, then choose your runtime. Google's Agent Runtime is one managed option; Cloud Run or Kubernetes are options for operating your own service.",
            "https://adk.dev/", ["local", "service"]),
        new("Strands Agents", "AWS / open-source community", "SDK",
            "An agent loop that runs in your process with supported model providers. Amazon Bedrock AgentCore Runtime is a separate hosting option, not a requirement for using the SDK.",
            "https://github.com/strands-agents/harness-sdk", ["local", "service"]),
        new("LangGraph", "LangChain", "Orchestration framework",
            "Build stateful agent and workflow code for a local process or your service. LangSmith Deployment adds deployment infrastructure; tracing with LangSmith alone does not host your agent.",
            "https://docs.langchain.com/oss/python/langgraph/overview", ["local", "service"]),
        new("n8n (self-hosted)", "n8n", "Workflow platform",
            "Run n8n locally for experiments or on your own infrastructure. You operate its deployment and credentials. Workflows can include AI steps; not every workflow is an agent. Edition and license limits apply.",
            "https://docs.n8n.io/choose-how-to-use-n8n.md", ["local", "service"]),
        new("Microsoft 365 Copilot", "Microsoft", "Workplace product",
            "Extend the Microsoft 365 experience with agents and approved work-data connections. Publishing an externally hosted agent here does not move its runtime into the product.",
            "https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/overview", ["product"]),
        new("Copilot Studio", "Microsoft", "Low-code agent platform",
            "Build and publish agents and workflows through a managed authoring product, including Microsoft 365 extensions and other channels. This is not Microsoft Agent Framework or unrestricted hosting for arbitrary code.",
            "https://learn.microsoft.com/en-us/microsoft-copilot-studio/fundamentals-what-is-copilot-studio", ["product"]),
        new("ChatGPT GPTs", "OpenAI", "Product customization",
            "Customize a ChatGPT experience with instructions, knowledge and supported capabilities. A GPT is not an OpenAI Agents SDK application that you deploy to your own server.",
            "https://openai.com/index/introducing-gpts/", ["product"]),
        new("ChatGPT workspace agents", "OpenAI", "Workplace product (research preview)",
            "Build and share workflow-oriented agents within ChatGPT, with connected tools and administrator controls. Check eligible plans and preview availability; this is distinct from a custom SDK service.",
            "https://openai.com/business/workspace-agents/", ["product"]),
        new("Gemini Gems", "Google", "Product customization",
            "Configure a Gemini Apps experience for repeat tasks and guidance. Gems are not ADK applications or a general-purpose cloud host; tool use and autonomy depend on the product's supported capabilities.",
            "https://support.google.com/gemini/answer/15236321", ["product"]),
        new("n8n Cloud", "n8n", "Hosted workflow product",
            "Author workflows in n8n while n8n operates the infrastructure. You still manage connections, credentials and workflow behavior. Self-hosted n8n moves those infrastructure duties to your team.",
            "https://docs.n8n.io/choose-how-to-use-n8n.md", ["product"]),
        new("Microsoft Foundry Agent Service", "Microsoft", "Managed agent runtime",
            "Configure prompt agents or deploy supported hosted agent code. Agent Framework is one way to build that code; model access alone is not agent hosting.",
            "https://learn.microsoft.com/en-us/azure/foundry/agents/overview", ["managed"]),
        new("Gemini Enterprise Agent Platform - Agent Runtime", "Google Cloud", "Managed agent runtime",
            "Managed deployment and operation for supported agent applications, including ADK. The former Vertex AI Agent Engine documentation now points to this runtime; Gemini Gems are a separate product experience.",
            "https://docs.cloud.google.com/gemini-enterprise-agent-platform/scale", ["managed"]),
        new("Amazon Bedrock AgentCore Runtime", "AWS", "Managed agent runtime",
            "Deploy supported agent and tool code to a managed runtime. Strands is one framework option, alongside others. Bedrock model inference alone does not deploy an agent.",
            "https://docs.aws.amazon.com/bedrock-agentcore/latest/devguide/what-is-bedrock-agentcore.html", ["managed"]),
        new("Claude Managed Agents", "Anthropic", "Managed harness (beta)",
            "A hosted API for a configurable agent harness and sessions, separate from Claude Agent SDK. Cloud and self-hosted sandbox options have different responsibilities; check beta behavior and retention requirements.",
            "https://platform.claude.com/docs/en/managed-agents/overview", ["managed"]),
        new("LangSmith Deployment", "LangChain", "Deployment platform",
            "Cloud provides managed agent hosting; self-hosted and hybrid options place some or all runtime duties on your infrastructure. Check plan requirements. LangGraph is an authoring option, not the hosting service itself.",
            "https://docs.langchain.com/langsmith/deployments", ["service", "managed"]),
    ]);

    internal static IReadOnlyList<HostingOption> Options { get; } = Array.AsReadOnly<HostingOption>(
    [
        new("local", "Personal runtime",
            "Try a report-review agent against a local report, with you inspecting its recommendations.",
            "Your computer runs the agent host. It stops when the process stops or the machine sleeps.",
            "A remote model API, or a compatible local model. A local agent host does not keep data local if it sends context to a remote model.",
            "Selected local files and permitted services, using explicitly granted access. Do not assume every local file is safe to expose.",
            "You maintain the runtime, credentials and dependencies, inspect failures and control spending.",
            "Fast experimentation and local access; availability depends on your machine. Staying local can be appropriate for personal work.",
            "Permission to install a runtime, an approved model, and approval for any data sent off the device."),
        new("product", "Existing product",
            "Configure a report-review assistant inside a product your team already uses, where supported.",
            "The product controls the agent runtime. You configure or extend it within supported features, not an arbitrary hosting contract.",
            "The product offers a supported set of models or model choices. Your preferred model may not be available.",
            "Product connectors and extensions provide access, subject to user identity, tenant policy and configuration.",
            "The vendor operates the product; your organization owns configuration, access, content governance and acceptance of results.",
            "Less infrastructure to maintain; customization, distribution and execution are bounded by the product. An agent definition is not automatically portable.",
            "Licenses, tenant administrator approval, supported connectors and a permitted way to share the assistant."),
        new("service", "Your own service",
            "Expose the report-review agent through an internal application or worker shared by a team.",
            "Your agent host runs on company infrastructure or cloud compute: for example, a container, application service or virtual machine.",
            "Your code calls an approved model endpoint, or separately operated inference. Hosting the agent host does not host the model.",
            "Service or delegated user identity reaches approved APIs and storage. A laptop path and personal login do not transfer automatically.",
            "Your team owns application releases, access isolation, persistent state, monitoring, recovery and budgets; infrastructure duties depend on the compute service.",
            "Control over code and integration, with operational work to match. Choose a runtime that supports your run duration, concurrency and networking.",
            "Approved compute, deployment access, model quota, private connectivity where needed, and a team that can operate the service."),
        new("managed", "Managed agent platform",
            "Run report-review tasks using an agent platform whose supported runtime and tools fit the workload.",
            "The platform runs a supported agent definition or hosted code. Packaging, execution limits and supported frameworks vary.",
            "Inference is a separate capability with its own supported models, regional availability, quotas and cost.",
            "Configured tools, connectors and execution identities reach approved data. Private access and per-user authorization still need design.",
            "The provider operates the layers promised by its service; your team still owns agent behavior, data access, evaluations, incident response and cost limits.",
            "Managed capabilities can reduce runtime work, but do not remove accountability. Verify state retention, isolation and recovery guarantees rather than assuming them.",
            "Platform access, supported region and runtime, approved tools and models, sufficient quota, and acceptable retention and networking policies."),
    ]);

    internal static IReadOnlyList<HostingReadiness> ReadinessLevels { get; } = Array.AsReadOnly<HostingReadiness>(
    [
        new("try", "Try it yourself",
            "Use a development identity and the minimum read access to a representative report.",
            "Keep test inputs and outputs separate from production. Temporary state may be sufficient for a disposable experiment.",
            "Inspect a trace, test incorrect answers and tool failures, and set a small run budget.",
            "You review the recommendations. No automatic publishing or sending."),
        new("share", "Share with a team",
            "Authenticate each user. Choose delegated or service identity deliberately and enforce each user's data authorization.",
            "Isolate users and conversations; use durable storage where continuity matters. Define retention and deletion.",
            "Test concurrent requests, cancellation and failed dependencies. Add monitoring, spending limits and a named support owner.",
            "Keep review before publication. Manage credentials outside code and release a tested, versioned configuration."),
        new("operate", "Run operationally",
            "Give scheduled or event-driven workers a scoped identity. Review access regularly and retain an audit trail.",
            "Persist job status and checkpoints when needed. Plan recovery and prevent a retried task from publishing twice.",
            "Bound retries, time and spend; alert an accountable owner. Test restart recovery and rollback before rollout.",
            "Route recommendations to a durable approval step before publication, including timeout, rejection and escalation paths."),
    ]);

    internal static IReadOnlyList<string> Captions { get; } = Array.AsReadOnly<string>(
    [
        "One task: review a report and prepare recommendations. Compare where its parts run and who owns them.",
        "Location, trigger and supervision are separate choices. Cloud does not mean autonomous; local does not mean human-operated.",
        "Local development is one route, not a requirement. Start in a product or platform when that fits; test in the target environment before rollout.",
    ]);

    internal static IReadOnlyList<string> Triggers { get; } = Array.AsReadOnly<string>(["User request", "Schedule", "Event"]);

    internal HostingOption Option { get; private set; } = Options[0];
    internal IEnumerable<HostingExample> CurrentExamples => Examples.Where(example => example.OptionIds.Contains(Option.Id));
    internal HostingReadiness Readiness { get; private set; } = ReadinessLevels[0];
    internal string Trigger { get; private set; } = Triggers[0];
    internal int Beat { get; private set; }
    internal bool CanPrevious => Beat > 0;
    internal bool CanNext => Beat < Captions.Count - 1;
    internal string Caption => Captions[Beat];
    internal string TriggerDetail => Trigger switch
    {
        "Schedule" => "A scheduler starts the review at a chosen time. It needs a running host, scoped credentials and a plan for missed or overlapping runs.",
        "Event" => "An incoming report event starts the review. Authenticate the event and handle duplicate deliveries without duplicate actions.",
        _ => "A person requests the review. The runtime may be local or remote; a request does not grant new permissions.",
    };

    internal void SelectOption(string? id) => Option = Options.FirstOrDefault(option => option.Id == id) ?? Option;
    internal void SelectReadiness(string? id) => Readiness = ReadinessLevels.FirstOrDefault(level => level.Id == id) ?? Readiness;
    internal void SelectTrigger(string? trigger) => Trigger = Triggers.Contains(trigger) ? trigger! : Trigger;
    internal void Move(int offset) => Beat = (int)Math.Clamp((long)Beat + offset, 0, Captions.Count - 1);
    internal void Complete() => Beat = Captions.Count - 1;
    internal void Restart() => Beat = 0;
}