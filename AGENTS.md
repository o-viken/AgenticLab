# TheSeries

A .NET 10 [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) sample: a set of AI agents backed by Azure OpenAI, each with its own persona and toolset, that answer questions using Wikipedia and a calculator as tools.

See [README.md](README.md) for a user-facing overview, prerequisites, and the `POST /chat` / `GET /agents` API.

## Architecture

Five projects, orchestrated by Aspire (solution: [TheSeries.slnx](TheSeries.slnx)):

| Project | Role |
|---------|------|
| [src/TheSeries.AppHost](src/TheSeries.AppHost/AppHost.cs) | Aspire orchestrator. Wires up resources, injects Azure OpenAI config, sets service references. |
| [src/TheSeries.AiService](src/TheSeries.AiService/Program.cs) | ASP.NET Core minimal-API service exposing `POST /chat`, `POST /chat/stream`, `POST /chat/control`, `POST /chat/reset` and `GET /agents`. Hosts the agent catalog. |
| [src/TheSeries.Console](src/TheSeries.Console/Program.cs) | Interactive console client that calls the AI service via service discovery. |
| [src/TheSeries.Web](src/TheSeries.Web/Program.cs) | Blazor Server app that visualizes the live data flow (User → Client → Harness → Tools → LLM) by consuming the `/chat/stream` Server-Sent Events. |
| [src/TheSeries.ServiceDefaults](src/TheSeries.ServiceDefaults/Extensions.cs) | Shared OpenTelemetry, health checks, resilience, and service discovery. Referenced by every service. |

Key flow: Console → `POST /chat` (with an optional agent name and conversation id) on AiService → `AgentCatalog` resolves the selected `ChatClientAgent` (Azure OpenAI) → the agent calls its tool subset → answers. The shared chat client is built in [AgentService.cs](src/TheSeries.AiService/AgentService.cs); the catalog in [Agents/AgentCatalog.cs](src/TheSeries.AiService/Agents/AgentCatalog.cs). Each agent is an `IAgentDefinition` under [src/TheSeries.AiService/Agents](src/TheSeries.AiService/Agents); tools live in [WikiTool.cs](src/TheSeries.AiService/Tools/WikiTool.cs) and [CalculatorTool.cs](src/TheSeries.AiService/Tools/CalculatorTool.cs).

**Conversation memory.** Agents stay stateless, but a run can continue a prior chat. Each request carries a client-generated `ConversationId`; [Agents/ConversationStore.cs](src/TheSeries.AiService/Agents/ConversationStore.cs) (a singleton `ConcurrentDictionary<string, AgentSession>`) holds one `AgentSession` per conversation, created lazily via `agent.CreateSessionAsync(…)` on first use. The endpoints pass that session into `agent.RunAsync(message, session, …)` / `RunStreamingAsync(message, session, …)`, so the model sees the earlier turns. Sessions are interchangeable across agents (they just carry chat messages), so a conversation may switch agents and keep its history. `POST /chat/reset` (`ConversationResetRequest { ConversationId }`) forgets a conversation; the Console `/new` command and the Web **New conversation** button call it. Storage is in-memory and intended for sequential use within a conversation (no eviction, no per-conversation locking).

**Layered system prompt.** Each agent's system prompt is composed from two parts via inheritance: a shared **harness** prompt and the agent's own **persona**. The framing follows the VS Code [agent harness model](https://code.visualstudio.com/blogs/2026/05/15/agent-harnesses-github-copilot-vscode) — an **agent is the model plus the harness**, where the harness assembles context, exposes a bounded toolset, runs the think→act→observe loop, executes the model's tool calls, and relays results (the user/client sit outside the agent). [Agents/AgentDefinitionBase.cs](src/TheSeries.AiService/Agents/AgentDefinitionBase.cs) is an abstract `IAgentDefinition` that defines the harness prompt (that model-plus-harness framing plus the cross-cutting operating rules every agent runs under — ground answers in tool results, don't fabricate, prefer tools over memory, be concise and transparent) and an abstract `Persona`. Its `Instructions` property returns the harness prompt (scoped in `<harnessMode>` tags) followed by the persona (scoped in `<agentMode>` tags), and that combined string is what `AgentCatalog` passes to each `ChatClientAgent` (and what the flow visualizer captures as the "system prompt"). Concrete agents inherit `AgentDefinitionBase` and supply only `Name`, `Description`, `Persona`, and `Tools`; override the virtual `Harness` only to replace the shared rules entirely.

### Live flow visualization

The Blazor web app animates a real agent run. [src/TheSeries.Web](src/TheSeries.Web/Program.cs) calls `POST /chat/stream` on the AI service; [Agents/FlowTracer.cs](src/TheSeries.AiService/Agents/FlowTracer.cs) runs the resolved agent with `RunStreamingAsync` and projects its execution into ordered `FlowEvent`s (`received`, `llm-request`, `tool-call`, `tool-result`, `llm-response`, `final`, `error`). Each event also carries a `Turn` (the 1-based LLM round-trip it belongs to) and an optional `Data` payload (the full, untruncated data for that step). The endpoint returns them as Server-Sent Events via `TypedResults.ServerSentEvents`. The UI ([Components/Pages/Flow.razor](src/TheSeries.Web/Components/Pages/Flow.razor)) consumes the stream with `System.Net.ServerSentEvents.SseParser` (in [Services/AiServiceClient.cs](src/TheSeries.Web/Services/AiServiceClient.cs)) and lights up each node/arrow as events arrive. The diagram draws **separate send and receive arrows** for both the Client↔Harness and Harness↔LLM links (so requests and responses animate independently), and a **loop badge** on the Harness↔LLM link shows the live turn / total round-trip count. The LLM node is labelled "LLM (MCP)" for the concept diagram, but the backend is Azure OpenAI with local function tools — there is no real MCP server. Two optional **Boundaries** toggles draw dashed overlay regions that make the [agent harness model](https://code.visualstudio.com/blogs/2026/05/15/agent-harnesses-github-copilot-vscode) explicit: the **Harness** region wraps the Harness node (tools, context, system prompt) and the agent loop, while the **Agent** region wraps the harness *plus* the LLM (**Agent = Model + Harness**). The User and Client deliberately fall outside both.

**Real LLM request/response capture.** The Steps list rows are expandable: clicking a step with captured `Data` reveals the actual payload — the messages and tool definitions sent to the model for an `llm-request`, the model's response for an `llm-response`/`final`, or the raw tool arguments/result for a `tool-call`/`tool-result`. The request/response data is captured at full fidelity by [Agents/CapturingChatClient.cs](src/TheSeries.AiService/Agents/CapturingChatClient.cs), a `DelegatingChatClient` inserted into the shared pipeline (after `UseFunctionInvocation`, see [AgentService.cs](src/TheSeries.AiService/AgentService.cs)). Because the chat client is a singleton, capture is scoped per run via [Agents/FlowCaptureScope.cs](src/TheSeries.AiService/Agents/FlowCaptureScope.cs), an `AsyncLocal` sink that `FlowTracer` opens for the duration of a traced run; when no scope is active (e.g. `POST /chat`) the capturing client is a transparent pass-through. Only messages and tool schemas are rendered — never the Azure OpenAI endpoint or API key.

**Backend-gated stepping (telemetry-synced).** Pacing happens on the *server* so the animation lines up with the real agent execution (and its OpenTelemetry spans), not just a client-side replay. Each run gets a `FlowSession` tracked in a `FlowControlRegistry` ([Agents/FlowSession.cs](src/TheSeries.AiService/Agents/FlowSession.cs)); `FlowTracer.StreamAsync` awaits `FlowSession.WaitForStepAsync` *before emitting each event*, so the next real step does not start until the session is allowed to advance. The session is created synchronously inside the `/chat/stream` endpoint (keyed by a client-supplied `SessionId`) so that control calls cannot race ahead of it. The UI drives it via `POST /chat/control` (`FlowControlRequest { SessionId, Action, Manual?, DelayMs? }`) with actions `next`, `pause`, `resume`, `stop`. **Auto** mode paces with a server-side `DelayMs`/`stepDelayMs` between steps (adjustable live, plus pause/resume); **Manual** mode blocks each step until the user clicks *Next*. Two implementation notes keep long pauses alive: the Web `AiServiceClient` registration calls `.RemoveAllResilienceHandlers()` (otherwise the shared resilience handler's ~30s timeout/retries would abort a paused stream), and the AiService disables Kestrel's `MinResponseDataRate` so an idle SSE response is not aborted.

### Agents

| Agent | Persona | Tools |
|-------|---------|-------|
| `WikiAssistant` | Concise research helper grounded in Wikipedia. | `SearchWiki`, `GetWikiPage` |
| `MathTutor` | Patient tutor that solves and explains arithmetic. | `Calculate` |
| `TriviaMaster` | Playful trivia host that researches facts and crunches numbers. | `SearchWiki`, `GetWikiPage`, `Calculate` |
| `ChatBot` (default) | Friendly conversational companion that chats from its own knowledge. | _(none)_ |
| `Coder` | Workspace-scoped coding agent that generates and edits code and runs allowlisted commands. **Requires a workspace.** | `ReadFile`, `ListFiles`, `WriteFile`, `DeleteFile`, `RunCommand`, `ReadSkill` |

The first-registered definition in [Program.cs](src/TheSeries.AiService/Program.cs) is the default. `GET /agents` lists them (each entry includes a `RequiresWorkspace` flag); `POST /chat` selects one by name (case-insensitive) and falls back to the default when none is given.

### Toggling an agent's tools (per run)

Each agent declares a fixed tool subset, but a caller can **disable a subset of those tools for a single run** — both chat requests accept an optional `DisabledTools` (a list of tool names). This can only ever *narrow* an agent's tools, never add to them, because the agent framework only ever **unions** per-run `ChatOptions.Tools` with the agent's tools (it never subtracts), so restriction can't go through run options. Instead it is enforced by [Agents/ToolFilteringChatClient.cs](src/TheSeries.AiService/Agents/ToolFilteringChatClient.cs), a `DelegatingChatClient` inserted as the **outermost** step of the shared pipeline (before `UseFunctionInvocation`, see [AgentService.cs](src/TheSeries.AiService/AgentService.cs)) so the removed tools are invisible to both the model and the function-invocation loop. Like the capture and workspace scopes it reads a per-run [Agents/ToolFilterScope.cs](src/TheSeries.AiService/Agents/ToolFilterScope.cs) (`AsyncLocal`); the endpoints open it for the run (`/chat` in [Program.cs](src/TheSeries.AiService/Program.cs), `/chat/stream` in [Agents/FlowTracer.cs](src/TheSeries.AiService/Agents/FlowTracer.cs), re-`Activate()`ing it before each agent advance since the `AsyncLocal` is reset on `yield`), and when no scope is active (or it disables nothing) the client is a transparent pass-through. Filtering matches `AIFunction.Name` case-insensitively. The Web flow page shows a **Tools** checkbox per tool of the selected agent (unchecked → disabled; resets when the agent changes); the Console toggles them with `/tools [name]`.

### Workspace skills (the Coder agent)

The `Coder` agent supports **skills**: small, named playbooks that live in the workspace and are loaded on demand, following a progressive-disclosure model. A skill is a folder under a top-level `skills/` directory containing a `SKILL.md` file whose YAML frontmatter declares a `name` and `description`, followed by a markdown body with the full instructions:

```
skills/get-date/SKILL.md
---
name: get-date
description: Get the current date and time on a Windows machine using the terminal.
---
(body with the steps the agent should follow)
```

The repo ships a sample [skills/get-date/SKILL.md](skills/get-date/SKILL.md) (run the Coder with the repo root as its workspace to try it). Three pieces under [src/TheSeries.AiService/Agents](src/TheSeries.AiService/Agents) make this work, plus one tool:

- [Agents/SkillDefinition.cs](src/TheSeries.AiService/Agents/SkillDefinition.cs) — the `name`/`description`/`relative path` record for a discovered skill.
- [Agents/SkillLoader.cs](src/TheSeries.AiService/Agents/SkillLoader.cs) — scans `skills/*/SKILL.md` in the active `WorkspaceScope`, parses the frontmatter (minimal, no YAML dependency), and via `BuildContextBlock()` renders the `<skills>` block (each skill's name + description) — or `null` when the workspace declares none.
- [Agents/SkillMatcher.cs](src/TheSeries.AiService/Agents/SkillMatcher.cs) — resolves a model-supplied skill name to a discovered skill (exact case-insensitive, then unambiguous partial).
- [Tools/SkillsTool.cs](src/TheSeries.AiService/Tools/SkillsTool.cs) — exposes `ReadSkill(name)`, which loads + matches and returns the full `SKILL.md` content (read through the `WorkspaceScope`, so it stays confined to the workspace).

**Opt-in and per-run injection.** Skills are opt-in per agent via `IAgentDefinition.SupportsSkills` ([IAgentDefinition.cs](src/TheSeries.AiService/Agents/IAgentDefinition.cs) / `AgentDefinitionBase` default `false`); the Coder overrides it to `true`. Because each agent's `Instructions` are built **once** at startup (the `ChatClientAgent` is a singleton) but skills live in the **per-request** workspace, the `<skills>` catalogue cannot be baked into the static harness like `TerminalTool.EnvironmentInfo` is. Instead the endpoints build it **per run** while the `WorkspaceScope` is active and pass it as run-scoped instructions: `new ChatClientAgentRunOptions(new ChatOptions { Instructions = block })`. `ChatClientAgent` **concatenates** these after the agent's base instructions for that call only (`$"{agent.Instructions}\n{runInstructions}"`) and does **not** persist them to the conversation session, so the list is re-derived fresh each turn and never accumulates. `POST /chat` does this in [Program.cs](src/TheSeries.AiService/Program.cs) (`BuildSkillRunOptions`); `/chat/stream` does the same in [Agents/FlowTracer.cs](src/TheSeries.AiService/Agents/FlowTracer.cs) (re-`Activate()`ing the scope first, since the AsyncLocal is reset on `yield`). The Coder's harness tells the model to call `ReadSkill` for any listed skill that fits the task rather than improvising. The sample skill uses `powershell` (added to `TerminalTool`'s default allowlist) to run `Get-Date`.

### Workspace-scoped tools (the Coder agent)

The `Coder` agent operates against a **workspace root** the caller must supply. Its tools live in [Tools/FileSystemTool.cs](src/TheSeries.AiService/Tools/FileSystemTool.cs) (`ReadFile`, `ListFiles`, `WriteFile` (create+overwrite), `DeleteFile` — files only) and [Tools/TerminalTool.cs](src/TheSeries.AiService/Tools/TerminalTool.cs) (`RunCommand`, restricted to an **allowlist** of executables — defaults to `dotnet, git, ls, dir, npm, node, python, pip, powershell, pwsh`, overridable via the `Coder:AllowedCommands` config array — with a 60s timeout). Commands are executed **through the OS shell** (`cmd.exe /c` on Windows, `/bin/sh -c` elsewhere) so built-ins like `dir`/`ls` and script commands like `npm.cmd` resolve as they would in a terminal; arguments containing shell operators (`& | ; < > \` $`, newlines) are rejected so the allowlist can't be bypassed by chaining. `TerminalTool.EnvironmentInfo` describes the live OS/shell and allowed commands and is injected into the Coder's harness as an `<environment>` block, so the model picks platform-appropriate commands (`dir` on Windows, `ls` on Unix). The agent declares `RequiresWorkspace => true` ([IAgentDefinition.cs](src/TheSeries.AiService/Agents/IAgentDefinition.cs) / [AgentDefinitionBase.cs](src/TheSeries.AiService/Agents/AgentDefinitionBase.cs) default `false`) and overrides the shared harness with stronger coding rules ([Agents/CoderAgent.cs](src/TheSeries.AiService/Agents/CoderAgent.cs)).

The workspace flows in **per request** (the tools are singletons, so it can't be constructor-injected): both chat requests carry an optional `Workspace` path, and the endpoints open an ambient [Agents/WorkspaceScope.cs](src/TheSeries.AiService/Agents/WorkspaceScope.cs) (an `AsyncLocal<WorkspaceScope>`, same pattern as `FlowCaptureScope`) for the run. The tools read `WorkspaceScope.Require()` and resolve every path through `WorkspaceScope.ResolvePath`, which **confines** access to the root and rejects path-traversal escapes (`..`, absolute paths) — the single security guard keeping the tools inside the chosen folder. In `/chat/stream` the scope is re-`Activate()`d before each agent advance (the `AsyncLocal` is reset on `yield`, exactly like the capture scope). **Enforcement:** when a workspace-requiring agent is selected without a (valid, existing) `Workspace`, `POST /chat` returns `400` and `/chat/stream` emits an `error` flow event and stops. The Console sets it with `/workspace <path>`; the Web flow page shows a **Workspace** input when the selected agent needs one.

## Build and Run

- Build: `dotnet build TheSeries.slnx`
- Run everything (launches the Aspire dashboard): `dotnet run --project src/TheSeries.AppHost`
- The Console is registered with `WithExplicitStart()`, so start it manually from the Aspire dashboard. It needs an attached terminal for stdin.
- The Web app (`web` resource) starts automatically and is exposed on an external HTTP endpoint; open it from the Aspire dashboard to use the flow visualizer.

## Configuration

Azure OpenAI settings are read from the **AppHost user-secrets** and injected into the AI service as environment variables. Set them on the AppHost project:

```
dotnet user-secrets set "AzureOpenAI:Endpoint" "<url>" --project src/TheSeries.AppHost
dotnet user-secrets set "AzureOpenAI:Deployment" "<deployment>" --project src/TheSeries.AppHost
dotnet user-secrets set "AzureOpenAI:ApiKey" "<key>" --project src/TheSeries.AppHost
```

Missing config throws at agent creation (`AgentService.Required`). Never commit secrets.

## Conventions

- Target framework `net10.0`; `Nullable` and `ImplicitUsings` enabled across all projects.
- Use top-level statements in `Program.cs` and minimal APIs (no controllers).
- DTOs are `internal sealed record` types declared at the bottom of the file that uses them.
- Agent capabilities are plain methods annotated with `[Description]` (on the method and each parameter) and exposed via `AIFunctionFactory.Create(...)` in each tool's `AsTools()` (see `WikiTool`, `CalculatorTool`, `FileSystemTool`, `TerminalTool`). Tools live under [src/TheSeries.AiService/Tools](src/TheSeries.AiService/Tools); add new ones there the same way. Tools that touch the file system or shell must stay confined to the active `WorkspaceScope` (resolve paths via `WorkspaceScope.ResolvePath`).
- Add a new agent by inheriting `AgentDefinitionBase` and supplying its name, description, `Persona` (its own system prompt, layered on top of the shared harness prompt), and tool subset under `src/TheSeries.AiService/Agents/`, then registering it as a singleton `IAgentDefinition` in [Program.cs](src/TheSeries.AiService/Program.cs). Override `RequiresWorkspace => true` when the agent's tools need a workspace root (the endpoints then insist on a `Workspace` path and open a `WorkspaceScope` for the run). Override `SupportsSkills => true` to opt into workspace skills (the endpoints then inject the `<skills>` catalogue per run; see the Workspace skills section).
- Services reach each other by Aspire resource name (e.g. `https+http://aiservice`) through service discovery, not hardcoded URLs.
- Agents are stateless; the shared `IChatClient` and the `AgentCatalog` are registered as singletons. Per-conversation history lives outside the agents in the singleton `ConversationStore` (keyed by `ConversationId`), not on the agents themselves.

## Documentation

- **Keep documentation present and up to date.** Whenever you change behavior, structure, or conventions, update the relevant docs in the same change so they never drift from the code.
- Update **this [AGENTS.md](AGENTS.md)** when you add/remove a project, change build/run commands, alter the architecture or dependency flow, or introduce a new convention.
- Add or update **XML doc comments** (`/// <summary>`) on public types and members — if no existing comment examples exist, add add replect example in here
- When adding a new endpoint, service, or cross-host component, document its purpose and any non-obvious behavior (e.g. that correct answers are never exposed).
- If you introduce project-level docs (README, `docs/**`), link to them from this file rather than duplicating content here.