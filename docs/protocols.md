# MCP, A2A and discovery

Part of the [Agentic Lab architecture notes](../AGENTS.md). The two real cross-service integrations — the MCP server whose tools the `TimeKeeper` agent calls and the A2A server whose agents the `Orchestrator` delegates to — plus the observable discovery process and the Discovery page that visualizes it.

## MCP server (the TimeKeeper agent)

A real Model Context Protocol integration: [src/AgenticLab.McpServer](../src/AgenticLab.McpServer/Program.cs) is a minimal ASP.NET Core MCP server (`ModelContextProtocol.AspNetCore`, `AddMcpServer().WithHttpTransport().WithToolsFromAssembly()`, `MapMcp()`) exposing one `[McpServerTool]` — `GetCurrentTime` ([Tools/TimeTools.cs](../src/AgenticLab.McpServer/Tools/TimeTools.cs)). The AppHost runs it as the `mcpserver` resource and the AiService references it. At startup [Application/Discovery/McpToolProvider.cs](../src/AgenticLab.AiService/Application/Discovery/McpToolProvider.cs) connects an MCP client over HTTP (endpoint resolved via service discovery, `services:mcpserver:http:0`), lists the tools, and caches them as `AITool`s; failures degrade gracefully to an empty list. The `TimeKeeper` agent ([Demo/Agents/TimeKeeperAgent.cs](../src/AgenticLab.AiService/Demo/Agents/TimeKeeperAgent.cs)) sets `SupportsMcp => true` and exposes those discovered tools. Discovery is surfaced over `GET /mcp` (`McpResponse { Servers: [{ Name, Tools: [{ Name, Description }] }] }`) and as the new `AgentInfo.SupportsMcp` flag; the Web flow page shows an **MCP servers** box in the harness (mirroring the Skills box). It can also be listed in a `.vscode/mcp.json` so VS Code uses it directly.

MCP uses the v2 SDK's default **stateless HTTP** transport. The time tool needs no transport session
or unsolicited server-to-client requests, so no stateful opt-in is required. This is independent of
the AiService's conversation memory and the Web application's chat SSE stream.

## A2A server (the Orchestrator agent)

A real Agent2Agent (A2A) integration — the agent-to-agent analogue of the MCP one above (where MCP standardizes model-to-**tool** calls, A2A standardizes agent-to-**agent** calls; there is no protocol distinction between "an agent" and "a sub-agent" — that only names the caller→callee relationship). [src/AgenticLab.A2AServer](../src/AgenticLab.A2AServer/Program.cs) is a minimal ASP.NET Core service that hosts **one or more persona-only agents declared in configuration** (`A2A:Agents` — each an entry of `{ Name, Path?, Description, Instructions }`; ships a `research` and a `poet` agent) via Microsoft Agent Framework: for each it calls `builder.AddAIAgent(name, instructions)` (backed by an Azure OpenAI `IChatClient`), `agent.AddA2AServer()`, and `app.MapA2AJsonRpc(agent, path)` (path defaults to `/a2a/{name}`) from `Microsoft.Agents.AI.Hosting.A2A.AspNetCore`. It also exposes a plain `GET /agents` **discovery** endpoint (`A2AAgentsResponse { Agents: [{ Name, Path, Description }] }`) so a caller can learn the roster without sharing config — adding an agent to `A2A:Agents` surfaces it to callers automatically. Because it hosts LLM agents, the AppHost injects the same `AzureOpenAI__*` settings it gives the AiService (unlike `mcpserver`, which needs none). The AppHost runs it as the `a2aserver` resource and the AiService references it. At startup [Application/Discovery/A2AAgentProvider.cs](../src/AgenticLab.AiService/Application/Discovery/A2AAgentProvider.cs) resolves the server endpoint (service discovery `services:a2aserver:http:0`, falling back to the `A2A:Endpoint` config value for a standalone run), calls its `GET /agents` to **discover** the hosted agents, and builds one `A2AClient` (from the standalone `A2A` package) per agent keyed by name. It exposes a single generic `DelegateToAgent(agentName, question)` `AITool` (whose description lists the discovered roster) that routes a question to the named agent, sends an A2A message and returns the reply; an unknown name returns the list of valid agents, and any failure degrades gracefully to an empty tool list. The `Orchestrator` agent ([Demo/Agents/OrchestratorAgent.cs](../src/AgenticLab.AiService/Demo/Agents/OrchestratorAgent.cs)) sets `SupportsA2A => true`, carries `Calculate` plus the delegation tool, and is instructed to answer arithmetic itself but delegate specialist questions by name — so a run contrasts a locally-answered turn with a delegated one. Discovery is surfaced over `GET /a2a` (`A2AResponse { Agents: [{ Name, Description }] }`) and as the new `AgentInfo.SupportsA2A` flag; the Web flow page shows an **A2A agents** box in the harness (mirroring the Skills/MCP boxes) listing every discovered agent, and a **Sub-agent (A2A)** resource node that lights up when the delegation tool runs.

**Separate A2A agent flow.** The **A2A agents** display option renders [A2AAgents.razor](../src/AgenticLab.Web/Components/Pages/FlowParts/A2AAgents.razor)
below the primary agent row: one harness/model composition per discovered remote agent. Research and Poet
share a separate A2A server process, not separate hosts; model nodes remain outside that hosting region.
The parent agent/environment overlays do not enclose the remote row. Styling stays in FlowDiagram.razor.css
with `::deep`, including container-based stacking. Only observed delegation boundaries highlight/animate;
remote internal model links stay static and their deployment/prompt details are not claimed as captured.

`FlowEvent.ToolCall` is an optional `FlowToolCall(Name, Arguments)` snapshot (immutable `JsonElement`),
captured from `FunctionCallContent` while retaining existing event kinds, display payloads and CallId.
FullCall remains display text, not JSON; routing must not parse it. The pure
[A2AFlowBuilder](../src/AgenticLab.Web/Flow/A2AFlowBuilder.cs) uses structured arguments and CallId pairing.
`FlowRunController.Replay.DisplayA2A` caches live/replay state; `ConversationTurn.A2AAgents` and
`ExecutionExchange.A2AAgents` retain the send-time roster. Replay uses the same causal prefix as Context,
so later results/agents never leak into an earlier stage. Unknown targets/old captures retain the generic
resource fallback. Initial load and vendor changes refresh A2A discovery as agent-picker changes already do.
See [the reference below](#remote-a2a-agents) for visible states and limitations. Focused capture/replay tests
live in the existing AiService and Web test projects.

## Protocol integration tests

[ProtocolIntegrationTests.cs](../tests/AgenticLab.AiService.Tests/ProtocolIntegrationTests.cs)
starts local Kestrel servers on ephemeral loopback ports using the same MCP/A2A registration APIs
as the production servers. It exercises the production `McpToolProvider` and `A2AAgentProvider`,
including rediscovery and calls after reconnecting. MCP exposes the real `TimeTools` implementation;
the A2A server hosts a deterministic fake-model agent and a test discovery roster. No Azure
credentials or external model calls are needed. Server entry points, Azure client configuration,
and Aspire service discovery still need a separate full-app smoke test.

The neighboring `FlowExecutionTests` also runs an actual `ChatClientAgent` through the tool-filtering,
function-invocation and capture middleware. It verifies streamed replies and tool results survive
into the second conversation turn, and that a model request for a disabled tool cannot execute it.

## Discovery visualization (MCP + A2A)

Both the MCP tool discovery ([Application/Discovery/McpToolProvider.cs](../src/AgenticLab.AiService/Application/Discovery/McpToolProvider.cs)) and the A2A agent discovery ([Application/Discovery/A2AAgentProvider.cs](../src/AgenticLab.AiService/Application/Discovery/A2AAgentProvider.cs)) are surfaced as an **observable, re-runnable, step-pable process** with its own Web page. Each provider exposes a `RediscoverAsync(...)` that yields an ordered stream of `DiscoveryEvent`s (`Start`, `Cleanup`, `Endpoint`/`NoEndpoint`, `Connecting`, `Listing`, one `Item` per discovered tool/agent, then `Done`/`Error`) — it first **cleans up** any existing connection (the MCP client is disposed; the A2A clients are cleared) and cached tools, then discovers again. The old `ConnectAsync` used at startup is now a thin wrapper that drains that stream, and each provider also tracks a `DiscoverySourceStatus` (endpoint, state, last-run time, discovered items). The models live in [Application/Discovery/DiscoveryModels.cs](../src/AgenticLab.AiService/Application/Discovery/DiscoveryModels.cs) (the `DiscoveryEventKind`/`DiscoveryState` enums are annotated to serialize as strings). [Application/Discovery/DiscoveryTracer.cs](../src/AgenticLab.AiService/Application/Discovery/DiscoveryTracer.cs) (a singleton) orchestrates a run for a **requested source** — `"mcp"`, `"a2a"`, or `"all"`/null for both, so MCP and A2A can be re-discovered **independently** — and, crucially, calls `AgentCatalog.RefreshDiscoveryAgents()` afterwards: because each `ChatClientAgent` bakes its `Tools` at construction and the `TimeKeeper`/`Orchestrator` agents expose the discovered tools by reference, a re-discovery would otherwise not reach the already-built agents. `RefreshDiscoveryAgents` rebuilds just the agents whose `SupportsMcp`/`SupportsA2A` is set, from the catalog's retained per-agent build (chat client + definition). Concurrent runs are serialized with a `SemaphoreSlim`.

**Backend-gated stepping.** Discovery **reuses the agent flow's stepping mechanism**: `DiscoveryTracer.StreamAsync(source, session, …)` takes a [Application/Flow/FlowSession.cs](../src/AgenticLab.AiService/Application/Flow/FlowSession.cs) and `await`s `session.WaitForStepAsync(…)` before emitting each event, so the run can be paced (auto with a per-step delay) or advanced one step at a time (manual). It is driven by the **same `POST /chat/control`** endpoint as a chat run (matched by the client-supplied `SessionId`): `next`, `pause`, `resume`, `stop` (and live `Manual`/`DelayMs` changes). The session is created in the `/discovery/stream` endpoint via the shared `FlowControlRegistry` and removed when the run ends.

**Discover on startup flag.** Whether discovery runs automatically at startup is gated by the **`Discovery:OnStartup`** config flag (default `true`, in [appsettings.json](../src/AgenticLab.AiService/appsettings.json)); when `false`, the two startup `ConnectAsync` calls in [Program.cs](../src/AgenticLab.AiService/Program.cs) are skipped and discovery is left to a manual run (after which the agents are refreshed, so `TimeKeeper`/`Orchestrator` work without a service restart). Two endpoints back the UI: `GET /discovery` returns a `DiscoverySnapshot { DiscoverOnStartup, Sources: [DiscoverySourceStatus] }`, and `POST /discovery/stream` (`DiscoveryStreamRequest { SessionId, Source?, Manual, StepDelayMs }`) runs a re-discovery for the requested source and streams the `DiscoveryEvent`s as Server-Sent Events (POST because it has side effects). The existing `GET /mcp` and `GET /a2a` endpoints are unchanged.

**Web view.** [Components/Pages/Discovery.razor](../src/AgenticLab.Web/Components/Pages/Discovery.razor)
is shared by the Flow header's modal overlay and the standalone `/discovery` route through
`DiscoveryPage.razor`. Learn has no Discovery navigation entry. The overlay leaves Flow mounted,
preserving conversation state and live streaming. It loads only the last-known snapshot when opened;
re-discovery remains an explicit command and is disabled while the parent chat runs. This is a local
UI guard, not cross-client locking. Closing cancels Discovery's own request/stream and waits for its
reader to exit, without sending controls to the chat session or resetting its conversation. Snapshot
and stream completions after disposal do not update the removed view.

The shared view shows one card per source (MCP tools, A2A agents), each with a client/server flow
diagram, directional send/receive arrows, state badge, expandable discovered definitions and step log.
Its toolbar retains Auto/Step mode, Delay, Next / Pause / Resume / Stop and Re-discover all; each card
also has an independent Re-discover command. Controls use Discovery's own session ID with
`POST /chat/control`. Runs use `AiServiceClient.StreamDiscoveryAsync`; snapshot loading and post-run
reconciliation use `GetDiscoveryAsync`. `Discovery:OnStartup` remains read-only. After a modal that
started re-discovery closes, Flow refreshes live agent/MCP/A2A catalogs without changing historical
exchange rosters, conversation identity, draft or replay selection.

## Remote A2A agents

Select **Orchestrator** and **Expert** to see each discovered remote agent (Research and Poet by
default) as its own **Agent host + Model** composition below the main flow. Both agent hosts run in the same
separate **A2A service** process; the cloud-model nodes show each agent's model role, not separate
deployments. **Agent** outlines each composition, while **Environment & risk** distinguishes the
shared server process from the cloud models. Narrow panes stack each harness above its model.
The remote area has a grid-free background, with alternating blue/green bands grouping each agent's
harness and model. This visual separation remains visible when boundary overlays are off.

Request/result arrows highlight the targeted agent. The display distinguishes **Delegation requested**
from **Result returned**: the former is a captured tool request, not confirmation of a network send,
and a result can contain an error. Remote model calls, prompts and token counts are **not captured**;
internal model links stay static. Generic model labels do not borrow the Orchestrator's deployment.

Execution playback uses the roster saved with that exchange and pairs calls/results by call ID.
Selecting a request never reveals its future reply. Missing structured metadata or unknown targets
fall back to the generic resource display. Replay and held/stopped runs do not animate remote links.

## Dependency upgrade checks

Run the deterministic agent and protocol integration tests without Azure credentials:

```sh
dotnet test tests/AgenticLab.AiService.Tests/AgenticLab.AiService.Tests.csproj --filter "FullyQualifiedName~FlowExecutionTests|FullyQualifiedName~ProtocolIntegrationTests"
```

These cover streamed agent tool execution, disabled tools and second-turn conversation history,
plus MCP discovery/tool calls and A2A discovery/delegation over loopback HTTP. The model is a fake;
the protocol servers use ephemeral ports and are disposed after each test. These checks do not
replace a live Azure OpenAI or full AppHost startup smoke test.
