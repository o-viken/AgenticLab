# Execution explorer and breakpoints

Part of the [TheSeries architecture notes](../AGENTS.md). The Execution dock at the bottom of the [flow page](web-flow-page.md): replaying a captured run stage by stage without re-running anything, the bounded per-page archive and its resource baseline, and the server-side execution breakpoints that hold a real run at model/tool boundaries.

## Execution explorer (replay of a captured run)

**Bounded archives.** [Flow/ReplayHistory.cs](../src/TheSeries.Web/Flow/ReplayHistory.cs) owns the Web
page's archived `ConversationTurn`s, evicting whole oldest exchanges on Send under configured count
and estimated UTF-16 payload budgets. FlowRunController invalidates derived caches so evicted captures
are released, resets retention on New conversation, and releases metric totals on disposal.
ExecutionReplayBuilder accepts a numbering offset; the explorer announces eviction rather than silently
presenting retained history as complete. Current-exchange capture is exempt until the next Send;
backend conversation memory is unchanged. The `TheSeries.Web.Replay` meter reports aggregate archived
exchange/event/payload totals and cumulative evictions without content or IDs. Options bind and validate
at Web startup. See [README](../README.md#replay-retention) for settings, measurement semantics and the
remaining load-test gate. Existing ExecutionReplayTests cover limits, fake-SSE controller lifecycle,
stable numbering, reset/disposal metrics and a synthetic archive plateau.

**Resource baseline.** The Web flow publishes aggregate `TheSeries.Web.Flow` instruments for active page
controllers, retained event count and retained estimated payload bytes. The AiService publishes an
aggregate `TheSeries.AiService.Flow` gauge for active `FlowControlRegistry` sessions. These metrics have
no user, conversation or session-id tags and should be read with runtime heap/allocation and latency
when comparing Interactive Server with a client-rendered build. A load test still needs to exercise idle
tabs, active and paused runs, long tool-heavy exchanges, reset, and closed tabs.

The repeatable browser profile lives in [tools/flow-loadtest.mjs](../tools/flow-loadtest.mjs), with setup and
interpretation in [tools/README.md](../tools/README.md). It uses configurable concurrent Playwright browser
contexts and reports browser-side timing/heap only; correlate it with Aspire/OpenTelemetry process
working set, managed heap, allocation rate and the aggregate flow metrics. It is deliberately not part of
the solution test suite and must not be treated as a CI pass/fail capacity claim.

The bottom **Execution** dock ([Components/Pages/FlowParts/ExecutionExplorer.razor](../src/TheSeries.Web/Components/Pages/FlowParts/ExecutionExplorer.razor) + scoped css) replaced the old Steps/Reply `FlowOutput` and the `ConversationTranscript` dock. It reuses the same bottom `SidePanel` (collapse to a rail, drag the top edge to resize, state persisted in the existing six-field `theseries-panels` `PanelState`), but is **always rendered** so the rail is discoverable before the first run, and adds a **maximize** toggle (`FlowViewState.Layout.BottomPanelMaximized` → the `.main-panel-body.bottom-max` class; deliberately **not** persisted, so `PanelState` needs no migration). `MaxPanelHeight` and `panels.js`'s `MAX_H` were raised 600 → 900 and the default height is 380. See [README.md](../README.md#execution-panel) for the user-facing behaviour.

The dock holds three panes: the conversation's **exchanges**, the selected exchange's **model turns and their stages**, and a **stage inspector** (a **Data** view of readable blocks, or **Raw**). The grouping is pure and client-side: [Flow/ExecutionModels.cs](../src/TheSeries.Web/Flow/ExecutionModels.cs) holds `ExecutionExchange`/`ExecutionTurn`/`ExchangeStatus`, [Flow/ExecutionReplayBuilder.cs](../src/TheSeries.Web/Flow/ExecutionReplayBuilder.cs) groups a run's `FlowEvent`s (`final`/`error` → the exchange's `Outcome`, `Turn <= 0` → `Intake`, the rest by `Turn`) and orders each turn **causally** — `llm-request`, `llm-response`, then the tool calls/results by sequence, because the capture emits a tool call while the response is still streaming — and [Flow/ExecutionStageReader.cs](../src/TheSeries.Web/Flow/ExecutionStageReader.cs) turns a captured payload into `StageSection`s (system prompt, each re-sent message, tools offered, a call's arguments, a tool's result), reusing `PromptSignatureBuilder.ToStrictJson` because the captured `Data` is display-formatted rather than strict JSON. Anything the capture does not hold is reported as *not captured* rather than reconstructed.

`ConversationTurn` gained a stable `Id` (assigned at send, surviving archiving so a selection stays put) plus the `Vendor`/`Workspace` the run actually used, and `FlowRunController` records them at `SendAsync`; `ArchiveCurrentRun` now archives **by exchange, not by event count**, so a run stopped before anything arrived is still listed. The controller exposes the cached `Exchanges` (recomputed in `EnsureComputed`, including the live run from the moment a message is sent) plus `SelectedExchange`/`SelectedStage`/`Replaying`/`CanStepBack`/`CanStepForward`/`StepBack`/`StepForward`/`CallFor`. The cursor itself is view state (`FlowViewState.Cursor.ExchangeId`/`CursorSequence`/`FollowingLive` + `SelectStage`/`SelectExchange`/`FollowLive`), matching the existing split. Replay is **read-only**: none of it can reach `SendAsync`/`SendControlAsync`, so stepping through history makes no model or tool call and the live run keeps recording while a past stage is pinned.

While a stage is pinned the diagram follows **it** instead of the live run: `NodeClass`/`ArrowClass`/`ActiveArrow`/`ResponseHint`/`ActiveToolName`/`ActiveToolArgs`/`ActiveToolResult`/`LoopLabel`/`LoopActive` switch on `Replaying` and derive from the cursor. Crucially a tool **call** stage shows its arguments but **not** the result that came back afterwards (`ReplayToolResult` only fills on a `tool-result` stage), so an earlier stage never leaks a later one. `FlowEventMapping.StageToolName` was added for the explorer because the existing `ToolNameFor` deliberately ignores a `tool-call` (the live diagram only lights a resource once it has actually been contacted).

## Chat execution breakpoints

The Settings tab offers four independent execution breakpoints (`before-model`, `after-model`,
`before-tool`, `after-tool`), all off initially. They pause in Auto as well as Manual mode and remain
selected for the page lifetime only. See [README.md](../README.md#post-chatcontrol) for the API contract
and user-facing Continue/Next/Stop behavior.

[FlowSession](../src/TheSeries.AiService/Application/Flow/FlowSession.cs) owns a separate cancellable latch,
an occurrence ID and a notification channel. Selection changes affect future boundaries without
releasing the current latch. Releases require the current occurrence ID; stale releases are rejected.
[FlowExecutionScope](../src/TheSeries.AiService/Application/Flow/FlowExecutionScope.cs) is an ambient per-run
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

The focused [test project](../tests/TheSeries.AiService.Tests/TheSeries.AiService.Tests.csproj) uses fake
model responses and counted tools to test real execution boundaries without credentials. Run it with
`dotnet test tests/TheSeries.AiService.Tests/TheSeries.AiService.Tests.csproj`.
