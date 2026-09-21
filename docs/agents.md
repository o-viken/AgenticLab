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

**Layered system prompt.** Each agent's system prompt is composed from two parts via inheritance: a shared **harness** prompt and the agent's own **persona**. The framing follows the VS Code [agent harness model](https://code.visualstudio.com/blogs/2026/05/15/agent-harnesses-github-copilot-vscode) — an **agent is the model plus the harness**, where the harness assembles context, exposes a bounded toolset, runs the think→act→observe loop, executes the model's tool calls, and relays results (the user/client sit outside the agent). [Application/Agents/AgentDefinitionBase.cs](../src/AgenticLab.AiService/Application/Agents/AgentDefinitionBase.cs) is an abstract `IAgentDefinition` that defines the harness prompt (that model-plus-harness framing plus the cross-cutting operating rules every agent runs under — ground answers in tool results, don't fabricate, prefer tools over memory, be concise and transparent) and an abstract `Persona`. Its `Instructions` property returns the harness prompt (scoped in `<harnessMode>` tags) followed by the persona (scoped in `<agentMode>` tags), and that combined string is what `AgentCatalog` passes to each `ChatClientAgent` (and what the flow visualizer captures as the "system prompt"). Concrete agents inherit `AgentDefinitionBase` and supply only `Name`, `Description`, `Persona`, and `Tools`; override the virtual `Harness` only to replace the shared rules entirely.

**Per-vendor harness (vendor system prompt).** When a **brand vendor** is selected in the Web flow page, the agent's shared **harness** layer is swapped for a **vendor-flavoured system prompt** for the run, while the agent's own persona (`<agentMode>`) is kept — so the same agent can be shown behaving under GitHub Copilot's, Claude's, ChatGPT's, etc. framing. The mechanism is split across the layers like the rest of the app: [Application/Agents/VendorHarnessCatalog.cs](../src/AgenticLab.AiService/Application/Agents/VendorHarnessCatalog.cs) is the **infrastructure** (a singleton that builds a `vendor key → harness string` map and resolves it via `Resolve(vendor)`, returning `null` for a blank/unknown key or for a vendor whose harness is empty — the non-brand **Default** vendor — so the agent keeps its own harness), but it holds **no prompt content itself**. Each vendor's prompt is supplied by an injected [Application/Agents/IVendorHarness.cs](../src/AgenticLab.AiService/Application/Agents/IVendorHarness.cs) (`Key` + `Harness` plus the vendor's own metadata — `DisplayName`, `ModelLabel` and a list of `Modes`, each a `VendorMode(Agent, Label)`) implementation, and the **content** lives in the Demo layer under [Demo/Vendors/&lt;Vendor&gt;/](../src/AgenticLab.AiService/Demo/Vendors) (one folder + class per brand: `CopilotHarness`, `ClaudeCodeHarness`, `ClaudeHarness`, `ChatGptHarness`, `GeminiHarness`, `Microsoft365Harness`, plus the non-brand `DefaultHarness`), registered as `IVendorHarness` singletons in [Startup/ServiceRegistration.cs](../src/AgenticLab.AiService/Startup/ServiceRegistration.cs). This keeps the `Application` layer free of any `Demo` dependency (the interface is the seam) while the representative prompts sit beside the demo agents. The strings are **original, representative** text written in each vendor's spirit — not the vendors' real proprietary prompts — and each retains the essential tool-grounding rules so agents keep functioning. Distinct keys exist per brand (`copilot`, `claude-code`, `claude`, `chatgpt`, `gemini`, `microsoft365`), plus the non-brand `default` key whose harness is empty (no override); Claude and Claude Code get separate harnesses. Because each `ChatClientAgent` bakes its `Instructions` once at start-up, the override can't be a per-run instruction append (those only *add*, like skills) — instead [AgentDefinitionBase.cs](../src/AgenticLab.AiService/Application/Agents/AgentDefinitionBase.cs) exposes `InstructionsWith(harnessOverride)` (which substitutes the `<harnessMode>` content while keeping the persona), and `AgentCatalog.TryResolve(name, harnessOverride, …)` builds a **transient** `ChatClientAgent` on the same chat client with the overridden instructions for that run (the cached agent is returned when the override is null). `WorkspaceAgentResolver.TryResolve` takes the same optional `harnessOverride` so workspace-defined agents pick it up too. The vendor flows in as a `Vendor` key on both chat requests (`ChatRequest`/`FlowChatRequest`): `POST /chat` resolves it in [Endpoints/ChatEndpoints.cs](../src/AgenticLab.AiService/Endpoints/ChatEndpoints.cs) and `/chat/stream` forwards it through [Application/Flow/FlowTracer.cs](../src/AgenticLab.AiService/Application/Flow/FlowTracer.cs) (`StreamAsync`'s `vendor` parameter). Only the Web client populates it today — it maps the selected `Vendor` to a key via `VendorCatalog.HarnessKey` (surfaced as `FlowViewState.VendorKey`) and sends it from `FlowRunController`. Because the transient agent's real `Instructions` carry the vendor harness, `CapturingChatClient` records it, so the swapped prompt shows in the expandable **llm-request** step, and the harness anatomy's **System Prompt** box shows the **actual active harness prompt** (`FlowViewState.Harness.PromptDisplay`): a short preview with a **Click to full prompt** toggle (`FlowViewState.Harness.ShowFullPrompt`) that expands the complete text. That text is fetched up front (before any run) from `POST /harness` (`HarnessRequest { Agent, Vendor }` → `HarnessResponse { Prompt }`), which resolves `VendorHarnessCatalog.Resolve(vendor)` and falls back to the selected agent's own harness via `AgentCatalog.HarnessFor(agent)` (the bare `<harnessMode>` text, exposed as `IAgentDefinition.HarnessPrompt`); the Web client calls it via `FlowRunController.Catalogs.RefreshHarnessPromptAsync` whenever the vendor or agent changes (`SetHarnessPrompt`). The brand vendors' **display metadata** (display name, simulated model label and the agent modes each offers) also lives on these backend definitions, not in the Web app: it is aggregated by `VendorHarnessCatalog.Vendors` (the non-blank-key vendors) and exposed read-only over `GET /vendors` (`VendorsResponse { Vendors: [{ Key, DisplayName, ModelLabel, Modes: [{ Agent, Label }] }] }`); the Web client loads it once on init (`AiServiceClient.GetVendorsAsync` → `FlowViewState.Roster.SetVendors`) and looks each vendor up by its `HarnessKey`. The non-brand **Default** vendor is itself a backend definition ([Demo/Vendors/Default/DefaultHarness.cs](../src/AgenticLab.AiService/Demo/Vendors/Default/DefaultHarness.cs)) so it follows the same pattern — it carries Default's metadata (display name + the `wiki`/`chat` modes) but supplies an **empty** `Harness`, which `VendorHarnessCatalog` treats as *no override* (`Resolve("default")` → `null`), so the agent keeps its own harness. Only the Web-side concerns stay in [Flow/VendorCatalog.cs](../src/AgenticLab.Web/Flow/VendorCatalog.cs): the enum→backend-key mapping (`HarnessKey`, where **Default** maps to the `default` key). To add a vendor harness, drop a new `Demo/Vendors/<Vendor>/<Vendor>Harness.cs` implementing `IVendorHarness` (supplying `Key`, `Harness`, `DisplayName`, `ModelLabel` and its `Modes`) and register it in `AddVendorHarnesses` ([Startup/ServiceRegistration.cs](../src/AgenticLab.AiService/Startup/ServiceRegistration.cs)); the Web picks up its metadata automatically via `GET /vendors` (add the matching `Vendor` enum member + `HarnessKey`/`DisplayOrder` entries and a `VendorIcon` logo). A `VendorMode`'s backend agent name must come from that agent's `public const string AgentName` (e.g. `new VendorMode(CoderAgent.AgentName, "agent")`) rather than a string literal, so a renamed or removed agent breaks the build instead of silently dropping the mode.

## Agents

| Agent | Persona | Tools |
|-------|---------|-------|
| `WikiAssistant` | Concise research helper grounded in Wikipedia. | `SearchWiki`, `GetWikiPage` |
| `MathTutor` | Patient tutor that solves and explains arithmetic. | `Calculate` |
| `TriviaMaster` | Playful trivia host that researches facts and crunches numbers. | `SearchWiki`, `GetWikiPage`, `Calculate` |
| `ChatAgent` (default) | Friendly conversational companion that chats from its own knowledge. | _(none)_ |
| `ChatGpt` | Conversational assistant used by the ChatGPT demo's chat mode, with Wikipedia grounding and arithmetic. | `SearchWiki`, `GetWikiPage`, `Calculate` |
| `Ask` | Read-only assistant that answers questions and explains code in the workspace without changing anything. **Requires a workspace.** | `ReadFile`, `ListFiles` |
| `Plan` | Read-only planner that investigates the workspace and proposes an implementation plan without changing anything. **Requires a workspace.** | `ReadFile`, `ListFiles`, `AskQuestion` |
| `Coder` | Workspace-scoped coding agent that generates and edits code and runs allowlisted commands. **Requires a workspace.** | `ReadFile`, `ListFiles`, `WriteFile`, `DeleteFile`, `RunCommand`, `ReadSkill` |
| `M365Copilot` | Microsoft 365 Copilot "Copilot Chat": a workplace assistant grounded in your work content via the **fake** Microsoft 365 / Graph tools, and able to send email on your behalf. | `SearchEmail`, `SearchFiles`, `SearchChats`, `GetCalendar`, `FindPeople`, `SummarizeDocument`, `SendMail` |
| `M365Researcher` | Microsoft 365 Copilot "Researcher": deep, multi-source research over work content plus public web. | `SearchEmail`, `SearchFiles`, `SearchChats`, `FindPeople`, `SummarizeDocument`, `SearchWiki`, `GetWikiPage` |
| `M365Analyst` | Microsoft 365 Copilot "Analyst": reads figures from your documents and crunches the numbers. | `SearchFiles`, `SummarizeDocument`, `Calculate` |
| `TimeKeeper` | Tells the current time using a tool **discovered from the MCP server** (real MCP client). | `GetCurrentTime` (MCP) |
| `Orchestrator` | Solves arithmetic itself, but delegates general-knowledge/research/creative questions to **specialist agents over the A2A protocol** (real A2A client), routing by name. | `Calculate`, `DelegateToAgent` (A2A) |

The first definition registered in [Startup/ServiceRegistration.cs](../src/AgenticLab.AiService/Startup/ServiceRegistration.cs) (`AddDemoAgents`) is the default. `GET /agents` lists them (each entry includes a `RequiresWorkspace` flag); `POST /chat` selects one by name (case-insensitive) and falls back to the default when none is given. `Ask` and `Plan` are **read-only** workspace agents: they share the read-only subset of the file tools (`FileSystemTool.AsReadOnlyTools()` → `ReadFile`, `ListFiles`) and never write, delete or run commands. `Plan` additionally carries the `AskQuestion` tool (see [asking the user a question](#asking-the-user-a-question-human-in-the-loop)), which pauses a run to ask the user a clarifying question but changes nothing in the workspace.

The three `M365*` agents power the **Microsoft 365 Copilot** vendor. They are grounded in a **fake** Microsoft 365 / Microsoft Graph tool set ([Demo/Tools/Microsoft365Tool.cs](../src/AgenticLab.AiService/Demo/Tools/Microsoft365Tool.cs)) — `SearchEmail`, `SearchFiles`, `SearchChats`, `GetCalendar`, `FindPeople`, `SummarizeDocument` (read-only grounding) plus `SendMail` (a **write/side-effecting** action that simulates sending a work email) — that matches over small canned, in-memory sample datasets and makes **no real Graph or network call** (it models the kind of work-content grounding M365 Copilot does, conceptually like the existing Wikipedia/calculator tools). `SendMail` is the reason `M365Copilot` is rated **Medium** risk rather than Low: unlike the read-only tools it *acts in the world* on the user's behalf (a hard-to-reverse communication that could leak data or impersonate the user if the model is wrong or steered by prompt injection in the content it reads), so the agent's persona is instructed to confirm the recipient/subject/body before calling it, the tool validates the address and refuses an empty message, and it can be toggled off per run to make the agent read-only. `Microsoft365Tool` exposes three subsets: `AsTools()` (all seven, for `M365Copilot`), `AsResearchTools()` (search subset, no calendar or send — `M365Researcher` combines it with `WikiTool` for public-web grounding) and `AsAnalystTools()` (files + summaries — `M365Analyst` combines it with `CalculatorTool`). None of the M365 agents require a workspace or support skills.

### ChatGPT lookup and calculation demo

The ChatGPT vendor's **chat** mode selects `ChatGpt`, a dedicated agent reusing the existing
Wikipedia and calculator tools. `ChatAgent` remains the tool-free default and other vendors' modes
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
