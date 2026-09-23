# Agents, personas and the layered harness prompt

Agentic Lab's chat API, agent roster, conversation memory and prompt/model configuration.
See [workspace features](workspace.md) for user-defined agents, skills and instructions, and
[Execution](execution-explorer.md) for streaming and controls. These APIs assume a trusted local
environment, not authenticated multi-user access; see [SECURITY.md](../SECURITY.md).

## Chat API

Use the **aiservice** endpoint shown in the Aspire dashboard for direct API calls. These routes
are implemented in [AgentEndpoints](../src/AgenticLab.AiService/Endpoints/AgentEndpoints.cs) and
[ChatEndpoints](../src/AgenticLab.AiService/Endpoints/ChatEndpoints.cs).

### `GET /agents`

Returns `agents` and a `default` name (`ChatAgent`). Each entry describes tools, workspace/skills
support, model, risk and guardrails, plus optional `exampleId`/`requiresExampleUi` metadata.
Enabled [examples](examples.md) can add agents; discover the roster rather than hardcoding it.
`POST /agents/workspace` lists workspace-defined agents separately. `GET /examples` returns public
module manifests, not component types or remote prompts.

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

Reuse the returned `conversationId` to continue the conversation. Agent names are case-insensitive.
An empty message, unknown agent or missing/invalid required workspace returns `400 Bad Request`.

Both chat paths support these optional fields:

| Field | Effect |
| --- | --- |
| `agent` | Select an agent; omission uses the default. |
| `workspace` | Existing service-side folder, required by workspace agents. |
| `vendor` | Replace host guidance, not the agent or its tools. |
| `disabledTools` | Narrow the agent's tool set for this run. |
| `disabledSkills` | Disable named skills; available skills default on. |
| `enabledInstructions` | Inject selected workspace instructions; default off. |

### `POST /chat/reset`

Clear remembered history with `{ "conversationId": "example-conversation-id" }`.
Returns `204 No Content`, or `400 Bad Request` for a blank ID. It does not reset an example's
domain state, approve an action or undo tool side effects. For interactive execution, see
the [streaming API](execution-explorer.md#post-chatstream) and
[control API](execution-explorer.md#post-chatcontrol).

## Conversation retention

Agents are stateless; [ConversationStore](../src/AgenticLab.AiService/Application/Conversations/ConversationStore.cs)
keeps an in-memory session per conversation. History survives agent changes but not service restarts.
Use a conversation sequentially; there is no per-conversation locking. Configure expiry in
[AiService settings](../src/AgenticLab.AiService/appsettings.json):

```json
{
  "Conversations": {
    "InactiveTtl": "01:00:00",
    "CleanupInterval": "00:05:00"
  }
}
```

Each use refreshes the sliding expiry. The `AgenticLab.AiService.Conversations` meter reports
`conversations.retained`, `conversations.expired` and `conversations.reset` counts, never content.
Backend retention is independent of [Web replay retention](execution-explorer.md#replay-retention).

## Conversation memory and the layered prompt

**Host changes:** Blazor and React start a fresh conversation and clear captures when Host changes,
preserving drafts and view preferences. Agent-only changes keep history. This is client behavior;
the server does not reset based on `vendor`. React commits the host change after successful reset;
Blazor uses a fresh local ID immediately and reports cleanup failures.

**Prompt layers:** the agent host's shared guidance (`<harnessMode>`) precedes its persona
(`<agentMode>`). Harness is the technical name for the host's agent-running machinery. Selecting
a vendor replaces only the host guidance, retaining the persona and configured tool set. It does
not select a different model deployment. Prompts are original teaching examples, not proprietary
vendor prompts or integrations with those products.

`default` preserves the agent's own guidance. Blank/unknown vendor keys and empty overrides also
leave it unchanged. Other hosts require [enabled examples](examples.md#default-startup);
Copilot365 uses module ID `copilot365` but API host key `microsoft365`.

- `GET /vendors` lists registered host names, modes, simulated model labels and optional example metadata.
- `POST /harness` accepts `{ Agent, Vendor }` and returns `{ Prompt }` for the host guidance preview.
- Captured `llm-request` data shows the actual composed instructions sent for a run.

[AgentDefinitionBase](../src/AgenticLab.Extensibility/Agents/AgentDefinitionBase.cs) composes the
layers; [VendorHarnessCatalog](../src/AgenticLab.AiService/Application/Agents/VendorHarnessCatalog.cs)
resolves registered overrides. Since agent instructions are fixed at construction, overrides create
a transient agent on the same chat client instead of mutating the cached agent. For registration
and ownership, see [example modules](examples.md).

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

The first definition registered by `AddDemoAgents` is the default. `Ask` and `Plan` never write,
delete or run commands. For custom agents, see the
[workspace format](workspace.md#workspace-defined-agents-the-agents-folder).

### Optional Copilot365 example

Enable `Examples:copilot365:Enabled=true` for `M365Copilot`, `M365Researcher` and `M365Analyst`.
The [module guide](../src/AgenticLab.Examples.Copilot365/README.md) lists their tools. None requires
a workspace or supports skills. Workplace data and `SendMail` are simulated: no Graph calls or mail
delivery. The Medium risk label illustrates sending; confirmation is a persona instruction, not an
enforced approval gate. The researcher's Wikipedia tools still make real public requests.

### ChatGPT lookup and calculation demo

Enable `Examples:chatgpt:Enabled=true` for the [ChatGPT example](../src/AgenticLab.Examples.ChatGpt/README.md)
(already enabled in AppHost Development). Its **chat** mode uses `ChatGpt`; the tool-free default
remains `ChatAgent`. Direct callers select both `agent: "ChatGpt"` and `vendor: "chatgpt"`.

Try: "Find the height of the Eiffel Tower on Wikipedia, then calculate how much taller it is than
a 250-metre building." Wikipedia uses its public API; calculation runs locally, with no extra key.
`GetWikiPage` returns a summary, not a full article or general web search. Retrieved text is source
material, not instructions. The model chooses calls; the host executes permitted ones.

## Asking the user a question (human-in-the-loop)

`Plan` includes [AskQuestion](../src/AgenticLab.AiService/Application/Tools/AskQuestionTool.cs);
workspace agents can select it too. On `/chat/stream`, an `ask-question` event displays an answer
input. Submit `POST /chat/control` with action `answer` and the `answer` text to resume. The answer
becomes a tool result in conversation history. Several questions may occur in one run.

There is no answer timeout: the run waits until answered, stopped or cancelled. Non-interactive
`POST /chat` returns a tool fallback asking the model to proceed with stated assumptions instead
of blocking. [UserInputScope](../src/AgenticLab.AiService/Application/Flow/UserInputScope.cs)
owns the per-run wait.

## Toggling an agent's tools (per run)

Send `disabledTools` with case-insensitive tool names. This only narrows the declared set; it cannot
grant tools. Web exposes checkboxes in Settings (reset on agent change); Console uses `/tools [name]`.

[ToolFilteringChatClient](../src/AgenticLab.AiService/Application/Flow/ToolFilteringChatClient.cs)
filters before function invocation so disabled tools reach neither model nor invocation loop.
Do not implement subtraction through per-run `ChatOptions.Tools`: the framework unions that list
with agent tools. [RunScopeSet](../src/AgenticLab.AiService/Application/Flow/RunScopeSet.cs) keeps
per-run filters together in both chat paths.

## Per-agent models

Agents can use different Azure OpenAI deployments on the same endpoint and credentials.
[ChatClientProvider](../src/AgenticLab.AiService/Application/Agents/ChatClientProvider.cs) caches
one client per deployment, resolved in this order:

1. `Agents:{Name}:Deployment` in AiService configuration.
2. The agent's `ModelId`, or a workspace agent's `model` field.
3. Global `AzureOpenAI:Deployment`.

Set deployment names in [AiService settings](../src/AgenticLab.AiService/appsettings.json):

```json
{
  "Agents": {
    "Coder": { "Deployment": "gpt-5.3-codex" }
  }
}
```

Use your actual Azure deployment names, not just model product names. `GET /agents` exposes the
resolved declared deployment as `ModelId`.

**Display versus execution:** `AzureOpenAI:ForceDefaultModel` defaults to `false`. When `true`, all
agents execute on the global deployment even though the API/UI still show each declared model.
For example, this displays Coder's `gpt-5.3-codex` but runs it on `gpt-5.3-chat`:

```json
{
  "AzureOpenAI": {
    "Deployment": "gpt-5.3-chat",
    "ForceDefaultModel": true
  },
  "Agents": {
    "Coder": { "Deployment": "gpt-5.3-codex" }
  }
}
```
