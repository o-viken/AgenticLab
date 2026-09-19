# Execution explorer and breakpoints

Part of the [Agentic Lab architecture notes](../AGENTS.md). The Execution dock at the bottom of the [flow page](web-flow-page.md): replaying a captured run stage by stage without re-running anything, the bounded per-page archive and its resource baseline, and the server-side execution breakpoints that hold a real run at model/tool boundaries.

## Execution explorer (replay of a captured run)

Custom telemetry meter names now use the `AgenticLab.*` prefix, including
`AgenticLab.AiService.Conversations`, `AgenticLab.AiService.Flow`, `AgenticLab.Web.Flow` and
`AgenticLab.Web.Replay`. Update any external meter-name filters when upgrading; instrument names,
dimensions and retention behavior are unchanged.

**Bounded archives.** [Flow/ReplayHistory.cs](../src/AgenticLab.Web/Flow/ReplayHistory.cs) owns the Web
page's archived `ConversationTurn`s, evicting whole oldest exchanges on Send under configured count
and estimated UTF-16 payload budgets. FlowRunController invalidates derived caches so evicted captures
are released, resets retention on New conversation, and releases metric totals on disposal.
ExecutionReplayBuilder accepts a numbering offset; the explorer announces eviction rather than silently
presenting retained history as complete. Current-exchange capture is exempt until the next Send;
backend conversation memory is unchanged. The `AgenticLab.Web.Replay` meter reports aggregate archived
exchange/event/payload totals and cumulative evictions without content or IDs. Options bind and validate
at Web startup. See [Replay retention](#replay-retention) for settings, measurement semantics and the
remaining load-test gate. Existing ExecutionReplayTests cover limits, fake-SSE controller lifecycle,
stable numbering, reset/disposal metrics and a synthetic archive plateau.

**Resource baseline.** The Web flow publishes aggregate `AgenticLab.Web.Flow` instruments for active page
controllers, retained event count and retained estimated payload bytes. The AiService publishes an
aggregate `AgenticLab.AiService.Flow` gauge for active `FlowControlRegistry` sessions. These metrics have
no user, conversation or session-id tags and should be read with runtime heap/allocation and latency
when comparing Interactive Server with a client-rendered build. A load test still needs to exercise idle
tabs, active and paused runs, long tool-heavy exchanges, reset, and closed tabs.

The repeatable browser profile lives in [tools/flow-loadtest.mjs](../tools/flow-loadtest.mjs), with setup and
interpretation in [tools/README.md](../tools/README.md). It uses configurable concurrent Playwright browser
contexts and reports browser-side timing/heap only; correlate it with Aspire/OpenTelemetry process
working set, managed heap, allocation rate and the aggregate flow metrics. It is deliberately not part of
the solution test suite and must not be treated as a CI pass/fail capacity claim.

The bottom **Execution** dock ([Components/Pages/FlowParts/ExecutionExplorer.razor](../src/AgenticLab.Web/Components/Pages/FlowParts/ExecutionExplorer.razor) + scoped css) replaced the old Steps/Reply `FlowOutput` and the `ConversationTranscript` dock. It reuses the same bottom `SidePanel` (collapse to a rail, drag the top edge to resize, state persisted in the existing six-field `theseries-panels` `PanelState`), but is **always rendered** so the rail is discoverable before the first run, and adds a **maximize** toggle (`FlowViewState.Layout.BottomPanelMaximized` → the `.main-panel-body.bottom-max` class; deliberately **not** persisted, so `PanelState` needs no migration). `MaxPanelHeight` and `panels.js`'s `MAX_H` were raised 600 → 900 and the default height is 380. See [the reference below](#execution-panel) for the user-facing behaviour.

The dock holds three panes: the conversation's **exchanges**, the selected exchange's **model turns and their stages**, and a **stage inspector** (a **Data** view of readable blocks, or **Raw**). The grouping is pure and client-side: [Flow/ExecutionModels.cs](../src/AgenticLab.Web/Flow/ExecutionModels.cs) holds `ExecutionExchange`/`ExecutionTurn`/`ExchangeStatus`, [Flow/ExecutionReplayBuilder.cs](../src/AgenticLab.Web/Flow/ExecutionReplayBuilder.cs) groups a run's `FlowEvent`s (`final`/`error` → the exchange's `Outcome`, `Turn <= 0` → `Intake`, the rest by `Turn`) and orders each turn **causally** — `llm-request`, `llm-response`, then the tool calls/results by sequence, because the capture emits a tool call while the response is still streaming — and [Flow/ExecutionStageReader.cs](../src/AgenticLab.Web/Flow/ExecutionStageReader.cs) turns a captured payload into `StageSection`s (system prompt, each re-sent message, tools offered, a call's arguments, a tool's result), reusing `PromptSignatureBuilder.ToStrictJson` because the captured `Data` is display-formatted rather than strict JSON. Anything the capture does not hold is reported as *not captured* rather than reconstructed.

`ConversationTurn` gained a stable `Id` (assigned at send, surviving archiving so a selection stays put) plus the `Vendor`/`Workspace` the run actually used, and `FlowRunController` records them at `SendAsync`; `ArchiveCurrentRun` now archives **by exchange, not by event count**, so a run stopped before anything arrived is still listed. The controller exposes the cached `Exchanges` (recomputed in `EnsureComputed`, including the live run from the moment a message is sent) plus `SelectedExchange`/`SelectedStage`/`Replaying`/`CanStepBack`/`CanStepForward`/`StepBack`/`StepForward`/`CallFor`. The cursor itself is view state (`FlowViewState.Cursor.ExchangeId`/`CursorSequence`/`FollowingLive` + `SelectStage`/`SelectExchange`/`FollowLive`), matching the existing split. Replay is **read-only**: none of it can reach `SendAsync`/`SendControlAsync`, so stepping through history makes no model or tool call and the live run keeps recording while a past stage is pinned.

While a stage is pinned the diagram follows **it** instead of the live run: `NodeClass`/`ArrowClass`/`ActiveArrow`/`ResponseHint`/`ActiveToolName`/`ActiveToolArgs`/`ActiveToolResult`/`LoopLabel`/`LoopActive` switch on `Replaying` and derive from the cursor. Crucially a tool **call** stage shows its arguments but **not** the result that came back afterwards (`ReplayToolResult` only fills on a `tool-result` stage), so an earlier stage never leaks a later one. `FlowEventMapping.StageToolName` was added for the explorer because the existing `ToolNameFor` deliberately ignores a `tool-call` (the live diagram only lights a resource once it has actually been contacted).

## Chat execution breakpoints

The Settings tab offers four independent execution breakpoints (`before-model`, `after-model`,
`before-tool`, `after-tool`), all off initially. They pause in Auto as well as Manual mode and remain
selected for the page lifetime only. See [the reference below](#post-chatcontrol) for the API contract
and user-facing Continue/Next/Stop behavior.

[FlowSession](../src/AgenticLab.AiService/Application/Flow/FlowSession.cs) owns a separate cancellable latch,
an occurrence ID and a notification channel. Selection changes affect future boundaries without
releasing the current latch. Releases require the current occurrence ID; stale releases are rejected.
[FlowExecutionScope](../src/AgenticLab.AiService/Application/Flow/FlowExecutionScope.cs) is an ambient per-run
scope, reactivated alongside the existing scopes before each agent advance. Model gates sit in
`CapturingChatClient` immediately before the inner request and after the complete response is captured.
`ChatClientProvider` configures the SDK's `FunctionInvoker` delegate to gate each actual function
invocation (serial, the SDK default), including MCP and local A2A delegation tools. No cached agent or
tool definitions are mutated. The non-streaming and discovery paths remain breakpoint-free.

`FlowExecutionScope.AdvanceAsync` multiplexes one pending agent `MoveNextAsync` with breakpoint
notifications. It does not prefetch further updates, preserving normal manual/auto event pacing.
The tracer sends `breakpoint` control events immediately, outside display-event gates, and cancellation
stops and awaits the pending advance before scopes and the session are disposed. The browser consumes
these notices separately from conversation events, preserving prompt/context totals, and renders the
shared `FlowBreakpointControls` in both Chat and Settings (its `ShowReason` parameter names the holding
boundary; the Conversation tab passes `false` because the agent's status note already does). `UserInputScope` preserves an answer submitted
before a breakpoint-delayed `AskQuestion` starts waiting, then re-arms after consumption.

The focused [test project](../tests/AgenticLab.AiService.Tests/AgenticLab.AiService.Tests.csproj) uses fake
model responses and counted tools to test real execution boundaries without credentials. Run it with
`dotnet test tests/AgenticLab.AiService.Tests/AgenticLab.AiService.Tests.csproj`.

## Replay retention

The Web app keeps the current exchange intact and bounds **archived** exchanges per Flow page.
Configure these initial budgets in [Web appsettings.json](../src/AgenticLab.Web/appsettings.json):

```json
"ReplayRetention": {
  "MaxArchivedExchanges": 20,
  "MaxArchivedPayloadBytes": 16777216
}
```

On the next Send, the previous exchange is archived and whole oldest exchanges are removed until
both limits are met. An oversized archive can be removed entirely; zero exchanges disables archiving.
Negative limits fail startup. These are tunable starting budgets, not load-tested capacity limits.
The payload estimate counts UTF-16 text (including captured data, structured tool arguments and exchange
metadata); it is **not** managed heap size and excludes object overhead and derived display allocations.
The current exchange is exempt, even after it finishes, until the next Send. A single long run can
therefore exceed the archive budget. AI service history and its inactivity TTL are independent.

The Web meter `AgenticLab.Web.Replay` exports process-wide sums `replay.archived.exchanges`,
`replay.archived.events`, `replay.archived.payload_bytes`, and a cumulative `replay.evicted.exchanges`
counter. Reset and page disposal subtract their retained totals. No identifiers or content are tagged.

For the CSR decision, compare these metrics with Web/AiService runtime heap, allocation rate and CPU
in Aspire: idle tabs, repeated sends, a long tool-heavy exchange, paused runs, New conversation, and
closed tabs after circuit retention has elapsed. Archives should plateau at the configured limits;
reset/disposal should return their totals to baseline. Also measure browser memory and event latency.
The synthetic retention test verifies bounded accounting, not real concurrent-user capacity.

The measurement baseline also includes `AgenticLab.Web.Flow` (`flow.active_pages`,
`flow.retained_events`, `flow.retained_payload_bytes`) and `AgenticLab.AiService.Flow`
(`flow.active_sessions`). These aggregate instruments have no user, conversation or session-id tags.
Use them with runtime heap, allocation rate and request latency when comparing Interactive Server with a
future client-rendered build.

## `POST /chat/stream`

Uses the [chat request fields](agents.md#post-chat), with a client-supplied `conversationId` and
`sessionId` (correlates control calls), a `manual` flag, and an
optional `stepDelayMs` (server-side delay between steps in Auto mode). An optional `breakpoints` array
accepts `before-model`, `after-model`, `before-tool`, and `after-tool`; omitted or empty means none.
Unknown breakpoint names return `400 Bad Request`. Returns a
`text/event-stream` of `flow` events describing the run as it happens — each event has a `sequence`,
`kind` (including `received`, `llm-request`, `tool-call`, `tool-result`, `llm-response`, `final`, `error`, `breakpoint`),
`label`, an optional `detail`, the `turn` (1-based LLM round-trip it belongs to), an optional
`data` payload with the full, untruncated request/response for that step, and — on tool steps — the
model's own `callId`, so a result can be paired with the call it answers even when the same tool is
called several times in one turn. An `llm-response` is emitted for **every** round-trip, not just the
last one, so the response that asked for a tool stays visible. The stream is gated on the
backend: each real step waits for the session to be allowed to advance, so it stays in sync with the
agent's execution and telemetry. The Blazor web UI consumes this to animate the data flow — with
separate send/receive arrows and a loop/turn counter — and to record the run in the Execution panel.

## Execution panel

The web UI's bottom **Execution** dock is where a run is read back. It pulls out from the bottom of
the main column (collapse it to a rail, drag its top edge to resize, or expand it to fill the column)
and holds three panes:

- **Exchanges** — retained messages you sent, with the agent that ran, how it ended (running / done /
  stopped / failed) and how many model round-trips it took. The current run appears as soon as you
  send, and a run that was stopped or failed stays inspectable rather than being reported as complete.
- **Stages** — the selected exchange broken into intake, each **model turn**, and delivery. A turn
  reads in causal order: the request sent, the model's response, the tool calls that response asked
  for, and their results.
- **Inspector** — the data captured at the selected stage. **Data** shows it in readable blocks (the
  system prompt, each message re-sent to the model, the tools offered, a call's arguments, a tool's
  result); **Raw** shows the captured payload verbatim. Anything the capture does not hold is marked
  as not captured rather than reconstructed.

**Previous** / **Next** step through the captured stages and cross turn boundaries; clicking any
exchange or stage jumps straight to it. Selecting a stage moves the diagram to it too, built only
from what was captured up to that point — standing on a tool call does not reveal the result that
came back afterwards. Navigating history never re-runs anything: it makes no model or tool calls, and
the live run keeps recording in the background. **Live** returns to the newest stage of the current
run. Captures cover the retained portion of the current conversation and are cleared by **New conversation**.
When the [replay budget](#replay-retention) removes older exchanges, the panel shows a notice and keeps
the original exchange numbers. Transcript, Context history, and Prompt signature Comparison/Delta
then cover retained exchanges only; captured requests can still contain older history re-sent by the
AI service. Starting the next exchange returns the cursor to Live, so it cannot stay on an evicted item.

The expanded harness's **Context** follows playback too: its flat, contributor-colored list shows
only earlier exchanges and content through the selected stage in the selected exchange. The header
identifies the replay exchange and stage; later tool results and answers stay hidden until reached.
The count and growth bar use the captured prompt available at that point (including its captured
response, using the Prompt signature calculation); before the first request, size is **not captured**.
**Prompt signature** follows the same cursor: Comparison uses the selected exchange's captured prefix
and the preceding captured exchange, while Delta includes only earlier exchanges and that prefix.
The current signature total matches Context at that stage. Before the selected exchange's first
request, the signature shows **No request captured at this stage**, rather than treating an earlier
exchange as current. Both panels identify the replay exchange and stage, and **Live** restores their
latest data. Inference, Embeddings and Neural network continue to show the live run. Browsing playback
never sends execution-control requests.

`breakpoint` events bypass normal pacing so the browser learns about a pause while execution is
blocked inside a model or tool call. Their `data` is JSON containing `Id`, `Kind`, `Tool`, `Paused`
and `Manual`. They are control notifications, not conversation content, and are excluded from the
Execution panel, prompt signature and context totals.

## `POST /chat/control`

Drives an in-progress `/chat/stream` run, keyed by its `sessionId`:

```json
{ "sessionId": "abc123", "action": "next", "manual": true, "delayMs": 600 }
```

`action` is one of `next` (advance one step in Manual mode), `pause`, `resume`, `stop`, or
`answer` (submit the `answer` field to a waiting [user question](agents.md#asking-the-user-a-question-human-in-the-loop)). `manual` and
`delayMs` are optional live adjustments. Returns `204 No Content`, or `404 Not Found` when the session is
unknown (e.g. already finished).

Send `breakpoints` to replace the enabled selection live (`[]` clears it; omission leaves it unchanged).
To release a breakpoint, include its current notification's `Id` as `breakpointId`:

```json
{ "sessionId": "abc123", "action": "resume", "breakpointId": "pause-occurrence-id" }
```

When releasing a breakpoint, `resume` selects Auto and `next` selects Manual.
A stale or already-released ID returns `409 Conflict`.
Ordinary pacing controls cannot bypass the breakpoint latch. Discovery and non-streaming `/chat`
do not use execution breakpoints.
