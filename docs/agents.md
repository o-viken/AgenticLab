# Agents, personas and the layered harness prompt

Part of the [Agentic Lab architecture notes](../AGENTS.md). How agents are defined and run: conversation memory, the layered harness + persona system prompt and the per-vendor harnesses that replace it, the built-in agent roster, the human-in-the-loop question tool, per-run tool toggles and per-agent model deployments. Workspace-defined agents are covered under [workspace](workspace.md#workspace-defined-agents-the-agents-folder).

## Chat API

Use the **aiservice** endpoint shown in the Aspire dashboard for direct API calls. These routes
are implemented in [AgentEndpoints](../src/AgenticLab.AiService/Endpoints/AgentEndpoints.cs) and
[ChatEndpoints](../src/AgenticLab.AiService/Endpoints/ChatEndpoints.cs).

### `GET /agents`

Returns an `agents` array describing the available built-in agents and a `default` agent name
(`ChatAgent`). Entries include the name, description, tools, workspace requirements, skills
support, model configuration, risk level and guardrails. Use discovery rather than assuming a
fixed roster; workspace-defined agents are listed separately by `POST /agents/workspace`.

Explicitly enabled [example modules](examples.md) contribute additional agents and hosts. Entries
may include `exampleId` and `requiresExampleUi` (null/false for existing agents). The shared definition
contracts live in `AgenticLab.Extensibility.Agents`; runtime construction remains in AiService.
`GET /examples` exposes public module manifests, not component types or remote prompts.
Model tools obtain authoritative conversation/agent identity through `IAgentRunContext` in both
chat paths. Example state and decision rules remain in their modules; `POST /chat/reset` only clears
conversation memory and does not imply a domain-state reset or approval.

### `POST /chat`

Send a message, optionally choosing an agent (otherwise the default is used):

```json
{ "message": "Who was Alan Turing?", "agent": "WikiAssistant" }
```

An example response:

```json
{
  "reply": "Alan Turing was a British mathematician and computer scientist...",
  "agent": "WikiAssistant",
  "conversationId": "example-conversation-id"
}
```

Include the returned `conversationId` in subsequent requests to continue the conversation.
An empty message, unknown agent, or missing/invalid workspace for an agent that requires one
returns `400 Bad Request`. Workspace agents and per-run tool, skill, instruction and vendor
options are described in this guide and the [workspace guide](workspace.md).

### `POST /chat/reset`

Clear remembered history with `{ "conversationId": "example-conversation-id" }`.
Returns `204 No Content`, or `400 Bad Request` for a blank ID. For interactive execution, see
the [streaming API](execution-explorer.md#post-chatstream) and
[control API](execution-explorer.md#post-chatcontrol).

## Conversation retention

History is held in memory, not durable storage. Configure the sliding inactivity window and
cleanup interval in [AiService settings](../src/AgenticLab.AiService/appsettings.json):

```json
"Conversations": {
  "InactiveTtl": "01:00:00",
  "CleanupInterval": "00:05:00"
}
```

Each use refreshes the expiry window. The `conversations.retained`, `conversations.expired` and
`conversations.reset` OpenTelemetry instruments report counts, never conversation content.
These settings are independent of the Web app's [replay retention](execution-explorer.md#replay-retention).

## Conversation memory and the layered prompt

**Vendor changes in the clients.** In Blazor and React, changing the Host (harness/vendor) starts a
fresh conversation and clears the transcript and execution history. Unsent drafts and view preferences
are preserved. Changing only the agent continues the current conversation. This is client behavior,
not an automatic server reset based on the request's vendor. React commits the new vendor only after
a successful reset; Blazor creates a fresh local conversation immediately and reports any server
cleanup failure without reusing the old ID.

**Conversation memory.** Agents stay stateless, but a run can continue a prior chat. Each request carries a client-generated `ConversationId`; [Application/Conversations/ConversationStore.cs](../src/AgenticLab.AiService/Application/Conversations/ConversationStore.cs) (a singleton `ConcurrentDictionary<string, ConversationEntry>`) holds one `AgentSession` per conversation, created lazily via `agent.CreateSessionAsync(…)` on first use. The endpoints pass that session into `agent.RunAsync(message, session, …)` / `RunStreamingAsync(message, session, …)`, so the model sees the earlier turns. Sessions are interchangeable across agents (they just carry chat messages), so a conversation may switch agents and keep its history. `POST /chat/reset` (`ConversationResetRequest { ConversationId }`) forgets a conversation immediately; the Console `/new` command and the Web **New conversation** button call it. Storage is in-memory and intended for sequential use within a conversation (no per-conversation locking). To bound abandoned history, access refreshes a sliding expiration window configured by `Conversations:InactiveTtl` (one hour by default), and a `TimeProvider` timer removes inactive entries at `Conversations:CleanupInterval` (five minutes by default). The store publishes count-only OpenTelemetry instruments on the `AgenticLab.AiService.Conversations` meter: `conversations.retained`, `conversations.expired` and `conversations.reset`.

**Layered system prompt.** Each agent's system prompt is composed from two parts via inheritance: a shared **harness** prompt and the agent's own **persona**. The framing follows the VS Code [agent harness model](https://code.visualstudio.com/blogs/2026/05/15/agent-harnesses-github-copilot-vscode) — an **agent is the model plus the harness**, where the harness assembles context, exposes a bounded toolset, runs the think→act→observe loop, executes the model's tool calls, and relays results (the user/client sit outside the agent). [Extensibility/Agents/AgentDefinitionBase.cs](../src/AgenticLab.Extensibility/Agents/AgentDefinitionBase.cs) is an abstract `IAgentDefinition` that defines the harness prompt (that model-plus-harness framing plus the cross-cutting operating rules every agent runs under — ground answers in tool results, don't fabricate, prefer tools over memory, be concise and transparent) and an abstract `Persona`. Its `Instructions` property returns the harness prompt (scoped in `<harnessMode>` tags) followed by the persona (scoped in `<agentMode>` tags), and that combined string is what `AgentCatalog` passes to each `ChatClientAgent` (and what the flow visualizer captures as the "system prompt"). Concrete agents inherit `AgentDefinitionBase` and supply only `Name`, `Description`, `Persona`, and `Tools`; override the virtual `Harness` only to replace the shared rules entirely.

**Per-vendor harness (vendor system prompt).** Selecting a host replaces the agent's shared
`<harnessMode>` prompt for the run while retaining its `<agentMode>` persona.
[VendorHarnessCatalog](../src/AgenticLab.AiService/Application/Agents/VendorHarnessCatalog.cs) is the
singleton infrastructure: `Resolve(vendor)` returns the registered prompt, or null for blank/unknown
keys and empty prompts. It owns no prompt content. Each injected
[IVendorHarness](../src/AgenticLab.Extensibility/Agents/IVendorHarness.cs) supplies `Key`, `Harness`,
`DisplayName`, `ModelLabel` and `Modes` (`VendorMode(Agent, Label)`). Only Default is registered in
[ServiceRegistration](../src/AgenticLab.AiService/Startup/ServiceRegistration.cs). Every non-default
host owns and registers its implementation in a separate opt-in [example module](examples.md), including
[ChatGPT](../src/AgenticLab.Examples.ChatGpt/README.md) and
[Copilot365](../src/AgenticLab.Examples.Copilot365/README.md). The
`Application` layer depends only on the shared contracts. All prompts are original, representative
text, not vendors' proprietary prompts, and retain the tool-grounding rules.

The only built-in key is `default`. Enable `copilot`, `claude-code`, `claude`, `chatgpt` or `gemini`
with `Examples:<key>:Enabled=true`; the API keys are unchanged. Copilot365 uses module ID `copilot365`
and retains host key `microsoft365`; Windfarm uses `windfarm` for both. The non-brand
[DefaultHarness](../src/AgenticLab.AiService/Demo/Vendors/Default/DefaultHarness.cs) supplies metadata
and `chat`/`wiki`/`time`/`orchestrator` modes but an empty prompt, so `Resolve("default")` preserves
the agent's own harness. Host selection does not change the configured model deployment.

`ChatClientAgent` instructions are fixed at construction, so the override is not a per-run append.
[AgentDefinitionBase](../src/AgenticLab.Extensibility/Agents/AgentDefinitionBase.cs) exposes
`InstructionsWith(harnessOverride)`, replacing only the harness portion.
`AgentCatalog.TryResolve(name, harnessOverride, ...)` creates a transient agent on the same chat
client when overridden, otherwise returning the cached agent. `WorkspaceAgentResolver.TryResolve`
accepts the same override. Both chat requests carry `Vendor`: `/chat` resolves it in
[ChatEndpoints](../src/AgenticLab.AiService/Endpoints/ChatEndpoints.cs), and `/chat/stream` forwards it
to [FlowTracer](../src/AgenticLab.AiService/Application/Flow/FlowTracer.cs). Web sends the selected
catalogue key through `FlowViewState.VendorKey` and `FlowRunController`.

The real overridden instructions are captured by `CapturingChatClient` in the expandable
**llm-request** step. The host's **System Prompt** box also previews the active prompt before a run,
with a full-text toggle (`FlowViewState.Harness.ShowFullPrompt`). `POST /harness`
(`HarnessRequest { Agent, Vendor }` to `HarnessResponse { Prompt }`) resolves the override or falls
back to `AgentCatalog.HarnessFor(agent)`, exposed as `IAgentDefinition.HarnessPrompt`.
`FlowRunController.Catalogs.RefreshHarnessPromptAsync` refreshes it when the host or agent changes.

`GET /vendors` returns the registered definitions' display names, simulated model labels and modes,
plus optional example metadata. Web loads them via `AiServiceClient.GetVendorsAsync` and
`FlowViewState.Roster.SetVendors`. Locally enabled example manifests supply optional logo assets,
ordering, product concept links and legacy selection aliases. Metadata never makes a disabled host
available. Web starts with Default and falls back to it when a saved example is unavailable; there
is no branding enum. Register `IVendorHarness` through `AddExample`; see [example modules](examples.md).
Use an owned agent's `AgentName` constant in modes, or
[SharedAgentNames](../src/AgenticLab.Extensibility/Agents/SharedAgentNames.cs) for the reusable core
`ChatAgent`, `Ask`, `Plan` and `Coder` identities. Harness-only examples do not own or duplicate them.

## Agents

| Agent | Persona | Tools |
|-------|---------|-------|
| `WikiAssistant` | Concise research helper grounded in Wikipedia. | `SearchWiki`, `GetWikiPage` |
| `MathTutor` | Patient tutor that solves and explains arithmetic. | `Calculate` |
| `TriviaMaster` | Playful trivia host that researches facts and crunches numbers. | `SearchWiki`, `GetWikiPage`, `Calculate` |
| `ChatAgent` (default) | Friendly conversational companion that chats from its own knowledge. | _(none)_ |
| `ChatGpt` (opt-in `chatgpt` example) | Conversational assistant used by the ChatGPT demo's chat mode, with Wikipedia grounding and arithmetic. | `SearchWiki`, `GetWikiPage`, `Calculate` |
| `Ask` | Read-only assistant that answers questions and explains code in the workspace without changing anything. **Requires a workspace.** | `ReadFile`, `ListFiles` |
| `Plan` | Read-only planner that investigates the workspace and proposes an implementation plan without changing anything. **Requires a workspace.** | `ReadFile`, `ListFiles`, `AskQuestion` |
| `Coder` | Workspace-scoped coding agent that generates and edits code and runs allowlisted commands. **Requires a workspace.** | `ReadFile`, `ListFiles`, `WriteFile`, `DeleteFile`, `RunCommand`, `ReadSkill` |
| `TimeKeeper` | Tells the current time using a tool **discovered from the MCP server** (real MCP client). | `GetCurrentTime` (MCP) |
| `Orchestrator` | Solves arithmetic itself, but delegates general-knowledge/research/creative questions to **specialist agents over the A2A protocol** (real A2A client), routing by name. | `Calculate`, `DelegateToAgent` (A2A) |

The first definition registered in [Startup/ServiceRegistration.cs](../src/AgenticLab.AiService/Startup/ServiceRegistration.cs) (`AddDemoAgents`) is the default. `GET /agents` lists them (each entry includes a `RequiresWorkspace` flag); `POST /chat` selects one by name (case-insensitive) and falls back to the default when none is given. `Ask` and `Plan` are **read-only** workspace agents: they share the read-only subset of the file tools (`FileSystemTool.AsReadOnlyTools()` → `ReadFile`, `ListFiles`) and never write, delete or run commands. `Plan` additionally carries the `AskQuestion` tool (see [asking the user a question](#asking-the-user-a-question-human-in-the-loop)), which pauses a run to ask the user a clarifying question but changes nothing in the workspace.

### Optional Copilot365 example

`M365Copilot`, `M365Researcher` and `M365Analyst` now belong to the opt-in
[Copilot 365 module](../src/AgenticLab.Examples.Copilot365/README.md), not the built-in roster.
Enable `Examples:copilot365:Enabled=true` to expose its **Copilot 365** host and all three modes.
The API host key remains `microsoft365`; agent names and per-agent model configuration stay compatible.
The module owns the workplace tools, fixtures and host prompt, and reuses Wikipedia/calculator
capabilities through the bounded `IHostToolSource` contract. Its guide lists each exact tool subset.

Workplace content and `SendMail` are simulated, with no real Graph call or mail delivery. The
**Medium** risk classification illustrates sending on a user's behalf; the tool only validates inputs
and returns a fictional receipt. Confirmation is a persona instruction, not an enforced approval gate.
The researcher still makes public Wikipedia requests when those tools run. None of the three agents
requires a workspace or supports skills.

### ChatGPT lookup and calculation demo

Enable `Examples:chatgpt:Enabled=true` to register the self-contained
[ChatGPT example](../src/AgenticLab.Examples.ChatGpt/README.md). Its **chat** mode selects `ChatGpt`,
a dedicated agent requesting `SearchWiki`, `GetWikiPage` and `Calculate` through `IHostToolSource`.
AppHost enables this example in Development; elsewhere it requires explicit configuration.
When disabled, neither the host nor its dedicated agent is registered.
`ChatAgent` remains the tool-free default and other vendors' modes
are unchanged. No extra API keys or services are needed: Wikipedia requests use its public API,
and calculations run locally in the AI service. The model backend remains Azure OpenAI; this is
a representative demo, not OpenAI's internal ChatGPT toolset.

Try: "Find the height of the Eiffel Tower on Wikipedia, then calculate how much taller it is
than a 250-metre building." The model chooses the tool calls; the host executes them and the
existing flow capture shows the results. Individual tools can still be disabled per run.
`GetWikiPage` returns a short summary, not the full article, and Wikipedia lookup is not general
web search or a guaranteed source of live sports results. Retrieved text is treated as source
material rather than instructions in the agent's prompt.

Direct API callers select `Agent: "ChatGpt"` and `Vendor: "chatgpt"`. The vendor alone only
changes the harness prompt; it does not select an agent or add tools.

## Asking the user a question (human-in-the-loop)

An agent can **pause a streaming run to ask the user a clarifying question and resume with their answer** via the `AskQuestion` tool ([Application/Tools/AskQuestionTool.cs](../src/AgenticLab.AiService/Application/Tools/AskQuestionTool.cs)). Only the `Plan` agent carries it today (the persona tells the model to ask sparingly, only for a genuinely blocking ambiguity). The tool blocks the agent loop by awaiting the run's [Application/Flow/UserInputScope.cs](../src/AgenticLab.AiService/Application/Flow/UserInputScope.cs) — an `AsyncLocal` per-run scope (same pattern as `ToolFilterScope`/`WorkspaceScope`) holding a `TaskCompletionSource<string>` that is re-armed on each call so an agent may ask several questions per run. Blocking the tool naturally suspends `RunStreamingAsync` exactly like `FlowSession.WaitForStepAsync` does for stepping.

The scope is opened only on the **streaming** path: [Application/Flow/FlowTracer.cs](../src/AgenticLab.AiService/Application/Flow/FlowTracer.cs) calls `UserInputScope.Begin()`, stores it on the run's `FlowSession.UserInput`, and re-`Activate()`s it before each agent advance (the `AsyncLocal` resets on `yield`, like the other scopes). When it sees an `AskQuestion` `FunctionCallContent`, it emits an **`ask-question`** `FlowEvent` carrying the question text instead of the usual `tool-call`. The Web flow page ([Components/Pages/Flow.razor](../src/AgenticLab.Web/Components/Pages/Flow.razor)) shows an **inline answer box near the message input** when that event arrives; submitting it posts `POST /chat/control` with the new **`answer`** action (`FlowControlRequest.Answer`), which calls `session.UserInput?.ProvideAnswer(...)`. That releases the tool, whose return value becomes the tool result — so the answer flows into the model's context and is persisted in the conversation session with no extra wiring. Under the non-interactive `POST /chat` endpoint no scope is active, so `AskQuestion` returns a fallback string telling the model to proceed with stated assumptions instead of blocking. There is no timeout: an unanswered question waits until answered or the run is stopped/cancelled (which cancels the `TaskCompletionSource`).

## Toggling an agent's tools (per run)

Each agent declares a fixed tool subset, but a caller can **disable a subset of those tools for a single run** — both chat requests accept an optional `DisabledTools` (a list of tool names). This can only ever *narrow* an agent's tools, never add to them, because the agent framework only ever **unions** per-run `ChatOptions.Tools` with the agent's tools (it never subtracts), so restriction can't go through run options. Instead it is enforced by [Application/Flow/ToolFilteringChatClient.cs](../src/AgenticLab.AiService/Application/Flow/ToolFilteringChatClient.cs), a `DelegatingChatClient` inserted as the **outermost** step of the shared pipeline (before `UseFunctionInvocation`, see [Application/Agents/ChatClientProvider.cs](../src/AgenticLab.AiService/Application/Agents/ChatClientProvider.cs)) so the removed tools are invisible to both the model and the function-invocation loop. Like the capture and workspace scopes it reads a per-run [Application/Flow/ToolFilterScope.cs](../src/AgenticLab.AiService/Application/Flow/ToolFilterScope.cs) (`AsyncLocal`); the endpoints open it for the run (`/chat` in [Endpoints/ChatEndpoints.cs](../src/AgenticLab.AiService/Endpoints/ChatEndpoints.cs), `/chat/stream` in [Application/Flow/FlowTracer.cs](../src/AgenticLab.AiService/Application/Flow/FlowTracer.cs), re-`Activate()`ing it before each agent advance since the `AsyncLocal` is reset on `yield`), and when no scope is active (or it disables nothing) the client is a transparent pass-through. Filtering matches `AIFunction.Name` case-insensitively. The Web flow page shows a **Tools** checkbox per tool of the selected agent (unchecked → disabled; resets when the agent changes); the Console toggles them with `/tools [name]`.

## Per-agent models

Each agent can run on its **own Azure OpenAI deployment**, so e.g. the `Coder` can use a coding-tuned model while the chat agents use a cheaper one. [Application/Agents/ChatClientProvider.cs](../src/AgenticLab.AiService/Application/Agents/ChatClientProvider.cs) builds and caches one `IChatClient` per distinct deployment (all sharing the same endpoint, credential and pipeline — tool filtering, function invocation, capture, OpenTelemetry); the deployment is baked into the Azure client at construction (Azure routes by it), so this can't be a per-call override. `AgentCatalog` asks the provider to resolve each agent's deployment and builds its `ChatClientAgent` on the matching client. Resolution precedence (`ChatClientProvider.ResolveDeployment`): the **`Agents:{Name}:Deployment`** config value first, then the agent's own `IAgentDefinition.ModelId` code default (null by default), then the global `AzureOpenAI:Deployment`. Deployment names are not secrets, so set them in the AiService config (e.g. [appsettings.json](../src/AgenticLab.AiService/appsettings.json)) rather than user-secrets:

```json
"Agents": {
  "Coder": { "Deployment": "gpt-5.3-codex" }
}
```

The resolved deployment is surfaced over `GET /agents` as `AgentInfo.ModelId`, and the Web flow page shows it on the LLM node (see the [web flow page](web-flow-page.md)). Workspace-defined agents resolve their deployment the same way — a `model:` field in the `*.agent.yaml` (overridable by `Agents:{Name}:Deployment`), else the global default.

**Display vs execution model (`ForceDefaultModel`).** The deployment an agent *declares* (its display model, from `ChatClientProvider.ResolveDeployment` → surfaced as `AgentInfo.ModelId` and shown on the LLM node) is decoupled from the deployment it actually *runs* on (`ChatClientProvider.ExecutionDeployment`, which the catalog builds the `ChatClientAgent` on). When **`AzureOpenAI:ForceDefaultModel`** is `true`, every agent executes on the global `AzureOpenAI:Deployment` regardless of its declared model — so a declared model (e.g. `gpt-5.3-codex`) is shown in the UI for that agent while a single real model (e.g. `gpt-5.3-chat`) answers. This keeps the per-agent model picture **purely visual** (handy while real per-deployment routing is paused, or to demo a model that the backend can't actually serve). Set it to `false` (the default) to run each agent on its own declared deployment for real:

```json
"AzureOpenAI": {
  "Deployment": "gpt-5.3-chat",
  "ForceDefaultModel": true
},
"Agents": {
  "Coder": { "Deployment": "gpt-5.3-codex" }
}
```
