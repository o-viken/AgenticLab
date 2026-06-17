# TheSeries

A .NET 10 [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) sample: a set of AI agents backed by Azure OpenAI, each with its own persona and toolset, that answer questions using Wikipedia and a calculator as tools.

See [README.md](README.md) for a user-facing overview, prerequisites, and the `POST /chat` / `GET /agents` API.

## Architecture

Five projects, orchestrated by Aspire (solution: [TheSeries.slnx](TheSeries.slnx)):

| Project | Role |
|---------|------|
| [src/TheSeries.AppHost](src/TheSeries.AppHost/AppHost.cs) | Aspire orchestrator. Wires up resources, injects Azure OpenAI config, sets service references. |
| [src/TheSeries.AiService](src/TheSeries.AiService/Program.cs) | ASP.NET Core minimal-API service exposing `POST /chat`, `POST /chat/stream`, `POST /chat/control` and `GET /agents`. Hosts the agent catalog. |
| [src/TheSeries.Console](src/TheSeries.Console/Program.cs) | Interactive console client that calls the AI service via service discovery. |
| [src/TheSeries.Web](src/TheSeries.Web/Program.cs) | Blazor Server app that visualizes the live data flow (User → Client → Harness → Tools → LLM) by consuming the `/chat/stream` Server-Sent Events. |
| [src/TheSeries.ServiceDefaults](src/TheSeries.ServiceDefaults/Extensions.cs) | Shared OpenTelemetry, health checks, resilience, and service discovery. Referenced by every service. |

Key flow: Console → `POST /chat` (with an optional agent name) on AiService → `AgentCatalog` resolves the selected `ChatClientAgent` (Azure OpenAI) → the agent calls its tool subset → answers. The shared chat client is built in [AgentService.cs](src/TheSeries.AiService/AgentService.cs); the catalog in [Agents/AgentCatalog.cs](src/TheSeries.AiService/Agents/AgentCatalog.cs). Each agent is an `IAgentDefinition` under [src/TheSeries.AiService/Agents](src/TheSeries.AiService/Agents); tools live in [WikiTool.cs](src/TheSeries.AiService/Tools/WikiTool.cs) and [CalculatorTool.cs](src/TheSeries.AiService/Tools/CalculatorTool.cs).

### Live flow visualization

The Blazor web app animates a real agent run. [src/TheSeries.Web](src/TheSeries.Web/Program.cs) calls `POST /chat/stream` on the AI service; [Agents/FlowTracer.cs](src/TheSeries.AiService/Agents/FlowTracer.cs) runs the resolved agent with `RunStreamingAsync` and projects its execution into ordered `FlowEvent`s (`received`, `llm-request`, `tool-call`, `tool-result`, `llm-response`, `final`, `error`). Each event also carries a `Turn` (the 1-based LLM round-trip it belongs to) and an optional `Data` payload (the full, untruncated data for that step). The endpoint returns them as Server-Sent Events via `TypedResults.ServerSentEvents`. The UI ([Components/Pages/Flow.razor](src/TheSeries.Web/Components/Pages/Flow.razor)) consumes the stream with `System.Net.ServerSentEvents.SseParser` (in [Services/AiServiceClient.cs](src/TheSeries.Web/Services/AiServiceClient.cs)) and lights up each node/arrow as events arrive. The diagram draws **separate send and receive arrows** for both the Client↔Harness and Harness↔LLM links (so requests and responses animate independently), and a **loop badge** on the Harness↔LLM link shows the live turn / total round-trip count. The LLM node is labelled "LLM (MCP)" for the concept diagram, but the backend is Azure OpenAI with local function tools — there is no real MCP server.

**Real LLM request/response capture.** The Steps list rows are expandable: clicking a step with captured `Data` reveals the actual payload — the messages and tool definitions sent to the model for an `llm-request`, the model's response for an `llm-response`/`final`, or the raw tool arguments/result for a `tool-call`/`tool-result`. The request/response data is captured at full fidelity by [Agents/CapturingChatClient.cs](src/TheSeries.AiService/Agents/CapturingChatClient.cs), a `DelegatingChatClient` inserted into the shared pipeline (after `UseFunctionInvocation`, see [AgentService.cs](src/TheSeries.AiService/AgentService.cs)). Because the chat client is a singleton, capture is scoped per run via [Agents/FlowCaptureScope.cs](src/TheSeries.AiService/Agents/FlowCaptureScope.cs), an `AsyncLocal` sink that `FlowTracer` opens for the duration of a traced run; when no scope is active (e.g. `POST /chat`) the capturing client is a transparent pass-through. Only messages and tool schemas are rendered — never the Azure OpenAI endpoint or API key.

**Backend-gated stepping (telemetry-synced).** Pacing happens on the *server* so the animation lines up with the real agent execution (and its OpenTelemetry spans), not just a client-side replay. Each run gets a `FlowSession` tracked in a `FlowControlRegistry` ([Agents/FlowSession.cs](src/TheSeries.AiService/Agents/FlowSession.cs)); `FlowTracer.StreamAsync` awaits `FlowSession.WaitForStepAsync` *before emitting each event*, so the next real step does not start until the session is allowed to advance. The session is created synchronously inside the `/chat/stream` endpoint (keyed by a client-supplied `SessionId`) so that control calls cannot race ahead of it. The UI drives it via `POST /chat/control` (`FlowControlRequest { SessionId, Action, Manual?, DelayMs? }`) with actions `next`, `pause`, `resume`, `stop`. **Auto** mode paces with a server-side `DelayMs`/`stepDelayMs` between steps (adjustable live, plus pause/resume); **Manual** mode blocks each step until the user clicks *Next*. Two implementation notes keep long pauses alive: the Web `AiServiceClient` registration calls `.RemoveAllResilienceHandlers()` (otherwise the shared resilience handler's ~30s timeout/retries would abort a paused stream), and the AiService disables Kestrel's `MinResponseDataRate` so an idle SSE response is not aborted.

### Agents

| Agent | Persona | Tools |
|-------|---------|-------|
| `WikiAssistant` (default) | Concise research helper grounded in Wikipedia. | `SearchWiki`, `GetWikiPage` |
| `MathTutor` | Patient tutor that solves and explains arithmetic. | `Calculate` |
| `TriviaMaster` | Playful trivia host that researches facts and crunches numbers. | `SearchWiki`, `GetWikiPage`, `Calculate` |

The first-registered definition in [Program.cs](src/TheSeries.AiService/Program.cs) is the default. `GET /agents` lists them; `POST /chat` selects one by name (case-insensitive) and falls back to the default when none is given.

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
- Agent capabilities are plain methods annotated with `[Description]` (on the method and each parameter) and exposed via `AIFunctionFactory.Create(...)` in each tool's `AsTools()` (see `WikiTool`, `CalculatorTool`). Tools live under [src/TheSeries.AiService/Tools](src/TheSeries.AiService/Tools); add new ones there the same way.
- Add a new agent by implementing `IAgentDefinition` (name, description, instructions, tool subset) under `src/TheSeries.AiService/Agents/` and registering it as a singleton `IAgentDefinition` in [Program.cs](src/TheSeries.AiService/Program.cs).
- Services reach each other by Aspire resource name (e.g. `https+http://aiservice`) through service discovery, not hardcoded URLs.
- Agents are stateless; the shared `IChatClient` and the `AgentCatalog` are registered as singletons.

## Documentation

- **Keep documentation present and up to date.** Whenever you change behavior, structure, or conventions, update the relevant docs in the same change so they never drift from the code.
- Update **this [AGENTS.md](AGENTS.md)** when you add/remove a project, change build/run commands, alter the architecture or dependency flow, or introduce a new convention.
- Add or update **XML doc comments** (`/// <summary>`) on public types and members — if no existing comment examples exist, add add replect example in here
- When adding a new endpoint, service, or cross-host component, document its purpose and any non-obvious behavior (e.g. that correct answers are never exposed).
- If you introduce project-level docs (README, `docs/**`), link to them from this file rather than duplicating content here.