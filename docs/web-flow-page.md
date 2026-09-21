# Web flow page (live agent run visualization)

Part of the [Agentic Lab architecture notes](../AGENTS.md). How the Blazor flow page animates a real agent run: the page shell and dockable panels, view presets and independent display options, the conversation surface, the captured LLM payloads and prompt signature, the simulated inference / embeddings / neural-network panels, backend-gated stepping and the environment & risk view. The Execution dock and breakpoints have [their own page](execution-explorer.md); the Learn panel is under [learning](learning.md).

## Live flow visualization

**Rename compatibility.** Agentic Lab retains the `theseries-vendor`, `theseries-panels`,
`theseries-workspace-bases` and `theseries-workspace-recent` local-storage keys so existing preferences
survive the rename on the same browser origin. Routes, concept IDs and lesson permalinks are unchanged.
The panel JavaScript API is now `agenticLabPanels`, with matching Blazor interop calls.

**Host inspector.** Enable **Expand agent host** under **View options** to make the anatomy's
section headings clickable and open one read-only **Details** pane in its own dock beside the flow.
The compact host stays quiet: no extra prompt/persona/settings rows or inspect buttons, and visible
tool, catalogue and risk headings remain plain text. The expanded anatomy exposes System prompt,
Agent persona, Settings, Custom instructions, User prompt and Context, plus enabled catalogue
and risk sections. Collapsing the host leaves an already-open Details pane and its selection intact.
Details shows only the selected part, with one title and its content. There is no
section picker, repeated agent heading or visible ownership/source badge. Contributor rails and
titles are shared with the anatomy; selection styling is separate from execution activity.

A2A agent chips, remote-agent headings and catalogue entries open an individual agent in Details only
while **Expand agent host** is enabled. Otherwise they render as plain labels without inspect icons;
an already-open Details pane and its selection remain intact. Inspection shows its description, protocol,
delegation status and full captured request/result without changing the conversation's selected agent.
The remote system prompt, model settings and tools are explicitly unavailable: discovery does not
expose them. A2A details share the diagram's live or causal replay projection and captured roster, so
later results and other agents' payloads cannot appear in the selected agent's detail. Missing agents
show an unavailable state. Selecting another host section or closing Details clears the remote-agent
selection; collapsing Details retains it. Inspection never delegates a request or refreshes discovery.

Details and Learn are separate panels that can remain visible together: Details beside the flow,
Learn on the far right. Each has independent width, scrolling and collapse controls. Details starts
at 320px; its width, collapse state and section selection last for the page lifetime. Learn retains its
existing persisted panel layout. The splitters use separate `--details-w` and `--right-w` grid tracks.
Opening another section replaces the current detail, and switching agent, vendor or workspace keeps
that section selected while its content updates. Opening a concept reveals only Learn; disabling Learn
does not affect Details. Closing Details removes only its dock. Collapse retains selection, and clicking
another anatomy heading expands only Details.
Inspection actions never change the draft, conversation, execution options, display preset or replay cursor.
Changing the Host vendor is a separate conversation action: it starts a fresh conversation and clears
execution captures, while keeping the selected Details section.
On narrow screens both panels stack separately below the main view; explicit section activation brings
Details into view. Native buttons support keyboard activation, and Close/Escape inside Details restores
focus to the invoking heading (or View options when the host has been collapsed).
Background updates do not move focus.

Content stays limited to the selected part: Settings shows the provider and deployment, not execution
controls or a duplicate display label; System prompt shows only the host prompt, not the composed
model request. Persona shows its captured text when available, otherwise the agent description.
Tools shows captured definitions when available, otherwise configured names and enablement.
Skills/instructions show their catalogue descriptions and enablement, not unrelated instruction or
message payloads. User prompt shows the submitted message, not the unsent draft. Context contains the
captured conversation content, without a statistics summary. Current/captured provenance (including
the description fallback) remains in each block's hover title; captured content includes exchange,
agent, vendor and workspace. Model-request captures also include the request turn; A2A boundaries do
not imply a captured remote-model turn. Editing stays in Settings; execution controls sit above live flow. No
additional workspace file reads, model calls or discovery runs are triggered by inspection.

Configuration-specific captures must match the displayed exchange's agent/vendor/workspace to the
current selections. Replay uses only the causal prefix through the selected stage, never a later
request or a request from another exchange. User prompt and Context are explicitly run-derived and
retain their exchange provenance even after configuration changes. Missing/malformed captures show an
unavailable state, not invented text. Selection changes immediately invalidate old prompt/catalogue
display, and versioned fetches ignore late successes and failures, including overlapping reloads for
the same selection. Details holds no extra capture archive; reset and existing eviction release history.

**Discovery overlay.** The Discovery button appears on `/`, not in the `/learn` header or stage
actions. It opens a native modal dialog without navigating or replacing Flow's view/run state:
the conversation, draft, settings and Execution replay remain intact. Close, Escape and backdrop
clicks dismiss it and restore focus to the trigger. The background is inert while the dialog is open,
but an active chat continues; re-discovery is disabled until that chat run ends because it reconnects
shared tool clients. Closing cancels only the Discovery stream. After re-discovery, live catalogs
refresh without resetting chat or historical captures. Direct `/discovery` visits remain supported.

**Naming.** Flow and Learn use **Agent = Agent host + Model**. The AI service node is
**Agent host**, the inference node is **Model**, and the anatomy control is
**Expand agent host**. Vendor names remain the node title when selected; subtitles identify the
host role. The client sits outside the host and has no anatomy row or Details section.
A2A agents use the same host/model labels. The model requests a next step; the host
checks permissions and executes permitted actions. Technical harness/LLM references below,
component names, event kinds and concept IDs remain unchanged; these are presentation-only labels.

**Shared design system.** Blazor now follows the React frontend's visual language across Flow, Learn,
Discovery and status pages. The [design system](design-system.md) owns document-level `--lab-*` tokens,
locally served IBM Plex Sans/Mono fonts and reusable Razor controls. Each component still owns its scoped
layout CSS. `AppHeader` provides a common product mark and page actions; `LabButton`, `LabField`,
`LabSegmented` and `LabStatus` provide consistent commands, inputs, choices and state indicators.
The development-only `/design-system` catalogue exercises these actual components without backend calls.
It returns 404 outside Development. No React runtime, CDN or npm build step is added to Web.

The workspace uses white/neutral surfaces, green commands, fine dividers and a shared 18px dotted
diagram grid. Semantic contributor, chart and risk colours remain distinct from branding. Flow and
Learn retain the **Agentic AI** heading; Discovery and browser titles retain **Agentic Lab**.
The shared repository link opens `https://github.com/o-viken/agenticlab` in a new tab.
Font notices and pinned Lucide/Octicons assets remain local under `wwwroot`.
Reduced motion suppresses visual effects, never backend event pacing.

The Prompt signature, Inference, Embeddings and Neural network sections follow the same flush,
divider-led workbench treatment in their own scoped CSS, reusing the Flow tokens. Chart role colors
and the signed red/blue vector scale remain semantic. Signature rows reflow in narrow containers;
network transport uses the shared MiniIcon controls. Reduced-motion rules explicitly reveal generated
tokens and suppress decorative network effects without changing its timer or manual stepping.

Context uses a flush, divider-led list with contributor rails, plain previews and a single accent
growth bar in FlowDiagram.razor.css. ContextStack reads `FlowRunController.Replay.DisplayContext`: live
entries normally, or a cached `ContextSnapshot` built by `ExecutionReplayBuilder.ContextAt` while
replaying. The snapshot includes only exchanges before the selected exchange and its causal stage
prefix (not a numeric sequence cutoff). Its size runs PromptSignatureBuilder over that prefix only;
before the selected exchange's first captured request it is unknown. Cache keys include state version,
exchange ID and selected sequence, so cursor changes and incoming events cannot leave stale content.
PromptSignature reads `DisplayPromptSignature`, built by `ExecutionReplayBuilder.SignatureAt` using
the same causal prefix plus earlier exchanges for Comparison and Delta. Both display snapshots share
the cursor/state cache in `EnsureReplayComputed`; their current sizes agree. Before the selected
exchange has a captured request, the replay signature is empty, even if earlier exchanges have data.
Inference, Embeddings, Neural network and `ContextSize` telemetry stay live. Live restores both panels.
Received-entry previews use the actual exchange message, and final answers use the agent contributor
color. Focused replay tests cover prefix ordering, future-content exclusion and absent captures.

**Web flow page structure.** [Flow.razor](../src/AgenticLab.Web/Components/Pages/Flow.razor)
composes a shared header, a **Host / Agent** selection bar and `.flow-body`. The selection bar replaces
the rendered vendor rail and composer agent picker, preserving `SetVendorAsync`, catalogue refreshes,
workspace-agent choices and the saved vendor. Selectors are locked while a run is active.

Choosing a different Host (harness/vendor) starts a fresh conversation using the existing New
conversation reset: a new conversation ID, empty transcript and execution captures, and a live replay
cursor. The unsent draft, workspace and layout/display preferences stay intact; the vendor's default
agent is selected as before. Re-selecting the current vendor, changing only the agent, and restoring
the saved vendor at startup do not reset chat. Reset happens before persistence and catalogue refreshes.
If server cleanup fails, an error is shown, but the new local conversation ID remains in use.

The primary `.workspace-grid` places Conversation/Settings beside live flow. `ControlsPanel` owns
compact, keyboard-navigable tabs and keeps `FlowChat` mounted while Settings is selected. Its header
places an icon-only **New conversation** action immediately before the collapse control on the right.
The button retains its tooltip, accessible name and active-run disabled state. Chat owns
the log, draft, user-answer input and Send. `FlowControls` retains breakpoints,
tools, workspace/repo selection, skills and custom instructions. Skills remain on by default;
instructions remain opt-in. Telemetry is still deliberately hidden.
`FlowRunControls` above the diagram owns Auto/Manual, delay, pause/resume, Next and Stop; its shared
toolbar keeps three fixed icon slots on the right: Pause/Resume (Continue at a breakpoint), Next and
Stop. All slots remain visible, with unavailable actions disabled, so Auto/Manual changes and
breakpoint pauses never relocate the buttons. `FlowBreakpointControls` shows only the holding reason
and control errors beneath that toolbar, not a second action row. The controls retain their accessible
names, descriptive tooltips and existing controller callbacks, including pending-request guards.
`ExecutionExplorer` sits below live flow and remains available before
the first run. Details and Learn are separate auxiliary docks, never mutually exclusive.

The desktop shell is centred, at most 1600px wide, with independently scrolling regions. A fresh or
reset workspace divides available primary space approximately 1.18:1. Below 900px of **primary
container** width it stacks Conversation before live flow. Below 1200px **viewport** width, Details
and Learn stack separately below the primary workspace. Desktop auxiliary tracks are constrained to
25% each so extreme saved widths cannot consume all primary space. The diagram retains its own 620px
container reflow. Responsive CSS does not alter preferences, selections, draft, capture or replay state.

`SidePanel` still owns collapse and resize. Its optional `ShowHeader` lets Conversation's tab strip
provide the header without duplication. Splitters support pointer dragging, arrow keys (20px, or
50px with Shift), Home and End; inappropriate horizontal splitters disappear in stacked layouts.
[panels.js](../src/AgenticLab.Web/wwwroot/js/panels.js) measures the rendered panel at drag start, updates
the existing CSS variables on `.flow-body`, and reports final sizes through `OnResized`.
Conversation width is clamped to 240-960px and at most 65% of primary space; auxiliary widths remain
240-640px and Execution height 120-900px. Fresh/reset Execution height is 240px and Learn width 260px.

`PanelLayout.AdaptiveConversationWidth` is true initially; dragging Conversation switches to a pixel
preference. `PanelState` writes `L|R|B|leftW|rightW|bottomH|adaptive` under the unchanged
`theseries-panels` key. Legacy six-field values are accepted as pixel layouts with their existing
sizes/collapse flags. **Reset layout** in Settings deliberately restores adaptive sizing without
resetting the run, draft, replay cursor, Details selection or Learn visibility. Details remains
page-lifetime state, not persisted. Execution maximise is also transient.

State ownership is unchanged: the page code-behind creates and cascades `FlowViewState` and
`FlowRunController`, subscribing to their `Changed` events for per-event renders. View collaborators
under `Flow/ViewState` own layout, concepts, options, workspace preferences, diagram, cursor, roster,
agent and harness selections. Run collaborators under `Flow/Run` own projections, replay, focus,
status and catalogues. `RunProjections` caches derived collections by `StateVersion` rather than
rebuilding them on every render. Pure builders remain beside them under `Flow/`.
Feature components stay under `FlowParts` to avoid colliding with the `Flow` page type. Public
parameter types remain public; internal state travels through private cascading parameters. Shared
design controls are presentation-only and do not depend on either state root.

**Conversation surface.** [FlowChat](../src/AgenticLab.Web/Components/Pages/FlowParts/FlowChat.razor)
renders a flat, top-aligned thread with 2px actor-coloured rails, readable line spacing and an anchored
composer. User, host reply and error markers use design-system actor/status tokens; the separate
context-provenance colours retain their meanings in the anatomy, capture and charts.
`LabStatus` presents the controller's existing `AgentStatusLabel`, with `TurnMeta` and
`AgentStatusNote` describing the current boundary or wait. `FlowEventMapping.ProgressLabelFor` and
`FlowRunController.Status.Activity` still derive those labels; no execution state is inferred from CSS.
Held and reduced-motion states do not animate. `ComposerHint` retains missing-workspace and active-run
feedback. New conversation, Send and the pending-question answer use shared `LabButton` controls.
New conversation stays in the panel header so the composer is reserved for the current message.
At narrow panel widths the header uses compact spacing and hides its decorative section number and
Settings count; both tab labels and action icons remain visible without changing saved preferences.
Keyboard submission and scroll-follow behaviour remain; selecting Settings does not recreate the
chat log or scroll it while hidden. Replies remain escaped text, not a new HTML/Markdown renderer.

The Blazor web app animates a real agent run. [src/AgenticLab.Web](../src/AgenticLab.Web/Program.cs) calls
`POST /chat/stream` on the AI service;
[Application/Flow/FlowTracer.cs](../src/AgenticLab.AiService/Application/Flow/FlowTracer.cs) runs the resolved
agent with `RunStreamingAsync` and projects its execution into ordered `FlowEvent`s (`received`,
`llm-request`, `tool-call`, `tool-result`, `ask-question`, `llm-response`, `final`, `error`). An
`llm-response` is emitted for **every** round-trip as its response completes (not just the last one), so the
response that requested a tool stays visible; `tool-call`/`tool-result`/`ask-question` also carry the model's
`CallId` so a result pairs with its own call even when one turn calls the same tool twice. Each event also
carries a `Turn` (the 1-based LLM round-trip it belongs to) and an optional `Data` payload (the full,
untruncated data for that step). The endpoint returns them as Server-Sent Events via
`TypedResults.ServerSentEvents`. The UI
([Components/Pages/Flow.razor](../src/AgenticLab.Web/Components/Pages/Flow.razor)) consumes the stream with
`System.Net.ServerSentEvents.SseParser` (in
[Services/AiServiceClient.cs](../src/AgenticLab.Web/Services/AiServiceClient.cs)) and lights up each node/arrow
as events arrive. The diagram draws **separate send and receive arrows** for both the User↔node and node↔LLM
links (so requests and responses animate independently), and a **loop badge** on the node↔LLM link shows the
live turn / total round-trip count. The AI service is drawn as a node titled
**Agent host** or the selected vendor's name. **Technical labels** adds the implementation and agent name
to its subtitle; otherwise the subtitle reads **Agent service**. Workspace agents also show their path.
The optional **Skills** box mirrors the model's progressive-disclosure view: it lists the
workspace's skill catalogue as chips and **highlights each one as the model loads it via `ReadSkill`**. The
known skills are fetched up front from `POST /skills` (which opens a `WorkspaceScope` for the supplied path
and runs `SkillLoader.Load()`) whenever the agent or workspace changes, so the catalogue shows **before** a
run; as a fallback they are also parsed client-side from the first `llm-request`'s captured `<skills>`
instructions. Loaded skills are detected from `ReadSkill` tool-call events (matched against the known names).
`GET /agents` exposes each agent's `SupportsSkills` flag; the catalogue appears when both that capability
and the **Skills** display option are enabled. The **Model** subtitle shows the real Azure OpenAI deployment
when **Technical labels** is on or the **Default** vendor is selected. Otherwise a branded vendor's
`ModelLabel` is explicitly marked **(simulated)**; the real backend remains Azure OpenAI.

**View presets and options.** All diagram visibility lives in `FlowViewState.Diagram` (`DiagramOptions`).
The compact diagram toolbar keeps **Basic / Technical** and the **Custom** status visible. It sticks
to the top of the flow scroll panel on desktop, or the viewport on narrow screens, until the diagram
ends. The diagram shares its parent's scroll area so the toolbar follows the actual scrolling content.
A single
**View options** popover groups the independent checkboxes under Details, Boundaries, Model internals
and Where it runs. It stays open while changing options, closes on Escape or an outside click, and
floats above the diagram without adding layout height. Native popover focus behavior supports keyboard
navigation and returns focus to the trigger on Escape. The panel is viewport-constrained and scrollable;
CSS anchor positioning places it beside its trigger where supported, with a centered fallback.
**Basic** and **Technical** are commands applying complete option sets, not rendering modes:

| Display option | Basic (initial) | Technical |
|---|---|---|
| Model and its arrows | Off | On |
| Loop counter | Off | On |
| Technical labels | Off | On |
| Tools | Off | On |
| Skills, MCP servers, A2A agents | Off | Off |
| Environment & risk, Agent / Agent host boundaries | Off | Off |
| Expand agent host, Prompt signature, Inference, Embeddings, Neural network | Off | Off |

All option controls remain available under every preset. An exact option match selects the corresponding
preset; other combinations show **Custom**, which is a status rather than a third preset. Selecting a
preset again resets all the listed options in one change notification. Panel interactions such as the
pinned token and prompt-signature comparison mode are retained. Display preferences last for the page
lifetime and are not persisted across refreshes.

The loop counter control is disabled while **Model** is hidden, but retains its checked preference; it
returns when the model is shown again. Teaching panels can be displayed independently of the Model node.
**Tools** controls tool details in both compact and expanded hosts plus the active tool resource below
the host. External tool activity occupies its own diagram row, so showing, updating or clearing a
resource card does not recenter the User, host, Model or their main connections. The diagram grows
downward as needed. At container widths of 620px or less, activity follows the entire main node stack
(including Model when visible), ahead of A2A and model-internals panels. No empty activity row is
reserved while inactive; changing the host's content or display options can still change its layout.
**Skills**, **MCP servers**, and **A2A agents** independently control their supported catalogues
in both layouts; **A2A agents** also controls the remote-agent flow row. `HostCatalogues` shares the
catalogue markup between both layouts. No option enables or disables an actual tool, filters captured
events, clears conversation history, or resets replay selection. Run settings remain in `RunOptions`.

The **Agent host** boundary excludes the model; the **Agent** boundary includes it
(**Agent = Agent host + Model**). The User falls outside both. Controls sit above the diagram, and layout
follows actual Model visibility, not preset identity. When Model is hidden, the host retains its centre
position. **Expand agent host** swaps the compact node for an anatomy breakdown, showing who contributes to
the context: **application** (red: the system/harness prompt and the application plumbing), **agent** (yellow:
the persona, the per-agent Tools/MCP list with `[x]`/`[ ]` enabled markers, and settings) and **user** (green:
the prompt) — mirroring the layered system-prompt model. The **Agent Persona** and **Tools / MCP** boxes each
also show their size as a separate char-count badge — the *agent prompt* (the persona) vs the *tools
available* (the tool catalogue) — derived client-side from the latest captured `llm-request` by
`PromptSignatureBuilder.AnatomySizesFor` (the persona = the instructions' `<agentMode>` text; the tools = the
catalogue's name + description + parameters, which the Prompt signature/Context totals deliberately exclude)
and exposed as `FlowRunController.Projections.PersonaChars`/`ToolsChars`. Below the layers a KISS **Context**
visualization grows as the run streams: a gradient bar (scaled by the captured step-data size) plus one
appended chip per **content** `FlowEvent` (the conversation content — user message, tool result, assistant
message, final answer; the structural system prompt, tool catalogue and persona are omitted because the
coloured boxes already show them), each colour-coded by contributor and carrying a one-line **preview of the
actual content** that flowed into the model, so you can watch the model's context accumulate turn by turn.
When the conversation has **earlier turns**, the Context stack also shows them first as **dimmed** chips under
an *Earlier in this conversation* divider — a compact pair per past turn (the user's message and the agent's
final answer, derived client-side from the archived `_turns`) above the current turn's live chips under a
*This turn* divider — and the bar and the `~chars` count reflect that carried-over history, because the
harness re-sends the whole conversation each turn (cleared by **New conversation**). The `~chars` figure is
kept **identical to the Prompt signature's current total** — both report the conversation content in the
latest `llm-request` (system prompt + every re-sent user/assistant/tool message, excluding the static tool
catalogue and JSON structure): `FlowRunController.Projections.ContextSize` simply returns
`PromptSignatureView.CurrentChars`, so the two numbers always agree. The **Host** selector offers
**Default**, **GitHub Copilot**, **Claude Code**, **Claude**, **ChatGPT**, **Gemini** and **Microsoft 365
Copilot**, in `VendorCatalog.DisplayOrder`. The selected logo remains beside the selector; its native
tooltip retains the model label and mode count. When **Learn** is enabled the selected host's product
concept remains accessible through the adjacent info button. All hosts share the design-system palette.
The selector invokes `SetVendorAsync`, retaining persistence and catalogue refreshes. Each vendor also
**curates which agents the Agent picker offers**, mirroring that product's "modes": the roster comes from the
vendor definition's `Modes` (loaded via `GET /vendors`) for every vendor including the non-brand **Default** —
each entry a `(backend agent name, display label)` pair: **Default** → `wiki` (`WikiAssistant`) + `chat`
(`ChatAgent`); **GitHub Copilot** → `ask` (`Ask`), `plan` (`Plan`), `agent` (`Coder`);
**ChatGPT**/**Claude**/**Gemini** → `chat` (`ChatAgent`); **Claude Code** → `plan` (`Plan`), `agent`
(`Coder`); **Microsoft 365 Copilot** → `chat` (`M365Copilot`), `researcher` (`M365Researcher`), `analyst`
(`M365Analyst`). The `AvailableAgents` computed property filters the `GET /agents` list down to the current
vendor's roster (skipping any name the service didn't register), the Agent `<select>` shows the labels, and
switching vendor auto-selects that vendor's first agent (`VendorDefaultAgent`) and refreshes the known skills.
The shared palette is defined in
[design-system.css](../src/AgenticLab.Web/wwwroot/design-system.css); feature aliases live on `.flow-app`
without vendor-specific overrides. The vendor choice persists in `localStorage` (key
`theseries-vendor`, restored in `OnAfterRenderAsync`, which also re-applies the restored vendor's agent
roster). The contributor colours (app/agent/user = red/yellow/green) are deliberately left un-themed because
they encode a fixed concept rather than branding; they are defined once as shared
`--contrib-app`/`--contrib-agent`/`--contrib-user` CSS tokens on the base `.flow-app` and reused by the
harness anatomy, the Context chips **and** the Prompt signature so the three never drift
apart.

**Real LLM request/response capture.** The Steps list rows are expandable: clicking a step with captured `Data` reveals the actual payload — the messages and tool definitions sent to the model for an `llm-request`, the model's response for an `llm-response`/`final`, or the raw tool arguments/result for a `tool-call`/`tool-result`. The request/response data is captured at full fidelity by [Application/Flow/CapturingChatClient.cs](../src/AgenticLab.AiService/Application/Flow/CapturingChatClient.cs), a `DelegatingChatClient` inserted into the shared pipeline (after `UseFunctionInvocation`, see [Application/Agents/ChatClientProvider.cs](../src/AgenticLab.AiService/Application/Agents/ChatClientProvider.cs)). Because the chat client is a singleton, capture is scoped per run via [Application/Flow/FlowCaptureScope.cs](../src/AgenticLab.AiService/Application/Flow/FlowCaptureScope.cs), an `AsyncLocal` sink that `FlowTracer` opens for the duration of a traced run; when no scope is active (e.g. `POST /chat`) the capturing client is a transparent pass-through. Only messages and tool schemas are rendered — never the Azure OpenAI endpoint or API key.

**Prompt signature.** A display option (**Prompt signature**, under **Model internals**, `FlowViewState.Diagram.ShowPromptSignature`) renders a full-width [Components/Pages/FlowParts/PromptSignature.razor](../src/AgenticLab.Web/Components/Pages/FlowParts/PromptSignature.razor) panel below the diagram that breaks the latest captured `llm-request` into message **categories** — System, User, Assistant and Tool result (the static **tool catalogue is excluded** so the signature reflects only the conversation content that grows turn to turn) — as a stacked bar, and compares it against the previous request with a prefix-stability **Match** score (the share of the current request byte-identical to the previous one, the way prompt caching measures reuse). The **Assistant** category also folds in each exchange's own **final reply** (captured from its `final`/`llm-response` event), because a turn's request never carries the answer it is about to produce — without this the reply would only appear once a *later* turn re-sent it as history, so a single-turn chat would show `Assistant 0`. It is computed entirely client-side from the captured request payloads (no backend change): [Flow/PromptSignatureBuilder.cs](../src/AgenticLab.Web/Flow/PromptSignatureBuilder.cs) works **per conversation exchange** (each user message → answer Send, across the archived `ConversationTurn`s plus the current run), taking each exchange's **last** `llm-request` as its representative prompt, and parses the `Data` JSON (counting chars per `role`/content type, excluding the tool catalogue) into a `PromptSignatureView` (`FlowModels.cs`), exposed as the cached `FlowRunController.Projections.PromptSignature` derived property (recomputed in `EnsureComputed()` alongside the Context stack). The panel has a header **segmented toggle** (`FlowViewState.Diagram.PromptSignatureDelta`) that switches between the default **Comparison** view (Previous/Current bars + Match, comparing the last two conversation **exchanges** — each labelled with its exchange's user message, not LLM round-trips) and a **Delta** ("growth") view: one bar per conversation exchange (`PromptSignatureView.Requests`, a list of `PromptSignatureRequest`), each split into a muted **reused** (carried-over prefix) segment plus a highlighted **added this exchange** segment and scaled to the conversation's largest exchange (`PromptSignatureView.MaxRequestChars`), so you watch each turn carry the previous prompt forward and append its own. The per-exchange reused/added split is size-additive (`PromptSignatureRequest.ReusedChars` is the previous exchange's total re-sent as the prefix, `AddedChars` is the remaining growth), so the totals chain exactly (previous total + added = this total); the Comparison **Match** is the separate true byte-identical prefix share. The Comparison category colours follow the same contributor palette as the harness anatomy (System = application red, User = user green, Assistant = agent yellow, via the shared `--contrib-*` tokens), with **Tool result** kept a distinct orange (the anatomy folds the system prompt and tool results both into "app", but the signature needs them separable); these and the Delta reused/added colours are not themed per vendor; the ⓘ button opens the `prompt-signature` concept.

**Simulated tokenizer & inference.** A display option (**Inference**, under **Model internals**, `FlowViewState.Diagram.ShowInferenceView`) renders a full-width [Components/Pages/FlowParts/Inference.razor](../src/AgenticLab.Web/Components/Pages/FlowParts/Inference.razor) panel below the diagram that **simulates** what happens *inside* the LLM node — a teaching aid, not real model internals (the backend is Azure OpenAI and never exposes its tokenizer or token logits). **Step ①** splits the latest captured `llm-request`'s user message into word-piece chips (green, `--contrib-user`) and shows a whole-prompt token estimate (≈ chars / 4 across instructions + messages + tool catalogue); **step ②** replays the run's `final` answer as **autoregressively generated** tokens (yellow, `--contrib-agent`), revealed left→right by a staggered CSS animation (`animation-delay = index * 0.045s`, keyframe `inf-tok-pop-in` — no JS timer). Hovering a generated token shows a fabricated **next-token candidate** popover with probability bars. It is computed entirely client-side (no backend change): [Flow/InferenceBuilder.cs](../src/AgenticLab.Web/Flow/InferenceBuilder.cs) (a `partial` class with a `[GeneratedRegex]` word-piece splitter) reads the current run's events — the latest `llm-request` `Data` (parsed via the reused `PromptSignatureBuilder.ToStrictJson`, made `internal`, for the last user message + char-count estimate) and the latest `final`/`llm-response` `Data` (the plain answer text) — into an `InferenceView` (`TokenCandidate`/`SimToken`/`InferenceView` records in `FlowModels.cs`), exposed as the cached `FlowRunController.Projections.Inference` derived property (recomputed in `EnsureComputed()` after the anatomy sizes). The fabricated probabilities are **deterministic** (seeded by a stable FNV-1a hash of the token text ^ its index, *not* `string.GetHashCode()` which is per-process randomized) so they don't flicker across the many per-event re-renders a run triggers. Whitespace tokens render with a `↵`/`␣` glyph; the contributor colours remain the same across vendors. The ⓘ button opens the `tokenization` concept.

**Simulated embeddings & neural network.** Two display options, next to **Inference**, render full-width teaching panels below the diagram — both **simulated** (the backend exposes no embeddings or weights). The **Embeddings** toggle (`FlowViewState.Diagram.ShowEmbeddingsView`) renders [Components/Pages/FlowParts/Embeddings.razor](../src/AgenticLab.Web/Components/Pages/FlowParts/Embeddings.razor): **step ①** turns each meaningful prompt token into a small fake **embedding** vector (8 dims) drawn as a red↔blue diverging heatmap strip (clicking a row pins it and shows its numbers — see the linking section), and **step ②** projects those vectors onto two fixed axes into a 2-D **meaning map** scatter where identical tokens overlap (near = similar). The separate **Neural network** toggle (`FlowViewState.Diagram.ShowNetworkView`) renders [Components/Pages/FlowParts/Network.razor](../src/AgenticLab.Web/Components/Pages/FlowParts/Network.razor): a **symbolic** forward pass (an SVG of fully-connected layers: embedding → 2 hidden → logits, input nodes green `--contrib-user`, output nodes yellow `--contrib-agent`) with a CSS-animated left→right signal sweep (`net-nn-sweep`). It also **decodes the answer on a loop**: a `System.Timers.Timer` (the panel implements `IDisposable`, so its lifetime is bounded by the toggle being on) steps through the simulated generated tokens (`Run.Inference.ResponseTokens`) one at a time, and for each step it shows the **softmax distribution** over that token's candidates (sorted high→low, the picked token highlighted with a `◀ pick` marker), grows/brightens the output nodes by probability (the picked one's node glows via `.net-node.fired`), re-keys the sweep so it replays per token, and appends the token to an *answer-so-far* strip with a blinking caret. Both panels read the same model: it is computed entirely client-side (no backend change) by [Flow/EmbeddingBuilder.cs](../src/AgenticLab.Web/Flow/EmbeddingBuilder.cs), which **reuses the `InferenceView`'s prompt tokenization** (dropping whitespace tokens) and fabricates each vector deterministically from `InferenceBuilder.StableHash` (made `internal`) of the lower-cased token, projecting to 2-D and normalising into an `EmbeddingsView` (`EmbeddingToken`/`EmbeddingsView` records in `FlowModels.cs`), exposed as the cached `FlowRunController.Projections.Embeddings` derived property (recomputed in `EnsureComputed()` right after the inference view). Because the vectors are hash-seeded they are stable across the many per-event re-renders a run triggers. The Embeddings panel's ⓘ opens the `embeddings` concept and the Neural network panel's ⓘ opens the `neural-network` concept.

**Linking the views (pin a token).** The Inference, Embeddings and Neural network panels are **interactively linked** through a single shared selection on `FlowViewState` (`SelectedToken`, set via `SelectToken(text)`/read via `IsTokenSelected(text)`, normalised to lower-cased + trimmed so the same word matches across panels). **Clicking a prompt token** in the Inference view, or a **vector row / scatter point** in the Embeddings view, *pins* that token: it is cross-highlighted everywhere it appears (the Inference prompt chip, the embedding heatmap row and the meaning-map point), the Embeddings panel reveals a **numeric detail card** showing the pinned token's full embedding as actual numbers (each of the 8 components, red positive / blue negative) plus its 2-D `(x, y)` map coordinates, and the Neural network panel's **input layer lights up** — each of the 8 input nodes is sized and colour-tinted by its matching vector component (`InputRadius`/`InputFill` in [Network.razor](../src/AgenticLab.Web/Components/Pages/FlowParts/Network.razor)), so you can see the embedding's numbers *become* the network input. Clicking the pinned token again unpins it. Independently, the **first generated answer token** (Inference step ②) is marked as the network's predicted output, tying the forward-pass result back to the generated answer. The selection is pure view state (no backend change) and only meaningful tokens (non-whitespace) can be pinned.

**Backend-gated stepping (telemetry-synced).** Pacing happens on the *server* so the animation lines up with the real agent execution (and its OpenTelemetry spans), not just a client-side replay. Each run gets a `FlowSession` tracked in a `FlowControlRegistry` ([Application/Flow/FlowSession.cs](../src/AgenticLab.AiService/Application/Flow/FlowSession.cs)); `FlowTracer.StreamAsync` awaits `FlowSession.WaitForStepAsync` *before emitting each event*, so the next real step does not start until the session is allowed to advance. The session is created synchronously inside the `/chat/stream` endpoint (keyed by a client-supplied `SessionId`) so that control calls cannot race ahead of it. The UI drives it via `POST /chat/control` (`FlowControlRequest { SessionId, Action, Manual?, DelayMs?, Answer? }`) with actions `next`, `pause`, `resume`, `stop` (and `answer`, which delivers the user's reply to a tool that asked a question — see [asking the user a question](agents.md#asking-the-user-a-question-human-in-the-loop)). **Auto** mode paces with a server-side `DelayMs`/`stepDelayMs` between steps (adjustable live, plus pause/resume); **Manual** mode blocks each step until the user clicks *Next*. Two implementation notes keep long pauses alive: the Web `AiServiceClient` registration calls `.RemoveAllResilienceHandlers()` (otherwise the shared resilience handler's ~30s timeout/retries would abort a paused stream), and the AiService disables Kestrel's `MinResponseDataRate` so an idle SSE response is not aborted.

**Environment & risk view.** A single **Environment & risk** toggle (the "Where it runs" diagram control, `_showEnvironment` in [Components/Pages/Flow.razor](../src/AgenticLab.Web/Components/Pages/Flow.razor)) makes *where each part of the system runs* and *how risky the selected agent is* explicit. Like every display option, it is always available and independent of the selected preset. When on it adds: per-node **location badges** — the User node shows `🖥️ Your browser`, the LLM node shows `☁️ Cloud service`, and the merged Harness/Application node shows either `💻 Your machine — file + shell access` (for a workspace agent) or `🖧 Server process — no local access` (otherwise); a dashed **`🔒 Your environment` trust boundary** overlay drawn around the parts that run on the user's own machine; and an **`EnvironmentPanel()`** under the harness node that renders a three-segment **risk meter** (filled per the agent's level — High=3, Medium=2, Low=1, None=0) plus a **guardrails** box listing the agent's enforced safety mechanisms as chips (with an empty-state when it has none). When **Learn** is also on, the panel adds three learn pills → the `where-agents-run`, `environment` and `sandbox` concepts, and the risk meter / guardrails box carry ⓘ buttons → the `agent-risk` and `guardrails` concepts. The data is **authoritative from the backend**, not derived client-side: each `IAgentDefinition` declares an `AgentRiskLevel RiskLevel` (`None`/`Low`/`Medium`/`High`, see [Application/Agents/IAgentDefinition.cs](../src/AgenticLab.AiService/Application/Agents/IAgentDefinition.cs)) and an `IReadOnlyList<string> Guardrails` of human-readable mechanisms; [Application/Agents/AgentDefinitionBase.cs](../src/AgenticLab.AiService/Application/Agents/AgentDefinitionBase.cs) defaults them to `None` / empty and each concrete agent overrides them (e.g. `Coder` → `High` with the workspace-confinement, command-allowlist, shell-operator-rejection, timeout and files-only guardrails; `M365Copilot` → `Medium` because its `SendMail` tool sends email on the user's behalf; the read-only and sample-data agents → `Low`; `ChatAgent` keeps `None`). `GET /agents` carries both (`AgentInfo.RiskLevel` as a string and `AgentInfo.Guardrails`, see [Application/Agents/AgentCatalog.cs](../src/AgenticLab.AiService/Application/Agents/AgentCatalog.cs)); the Web client mirrors them on its own `AgentInfo` record ([Services/AiServiceClient.cs](../src/AgenticLab.Web/Services/AiServiceClient.cs)). All styling (the `env-*`, `risk-*` and `guardrail-*` classes and CSS variables) is themed via the `.flow-app` tokens in [Components/Pages/Flow.razor.css](../src/AgenticLab.Web/Components/Pages/Flow.razor.css), so the badges, meter and boundary share the same palette across vendors.

## Corporate workbench

This heading is retained for existing links. The [V8 mockup](../design/mockups/v8-corporate-workbench.html)
is a historical design, superseded by the React-inspired [Blazor design system](design-system.md)
and conversation-first workspace described above. All advanced diagram, learning and execution
features remain available. Inference, embeddings and neural-network views remain simulations, not
captured model internals.

Run the no-model-call browser smoke check in [tools/README.md](../tools/README.md) for responsive
layouts, saved preferences, independent docks, focus restoration and the development catalogue.

## Running the Console

The Console is registered with `WithExplicitStart()`, so it does not launch automatically with the rest
of the app. To run it:

1. Start the app with `dotnet run --project src/AgenticLab.AppHost` and open the Aspire dashboard.
2. Find the `console` resource and start it (▶). It needs an attached terminal for stdin, so use the
   dashboard's terminal/console view to interact with it.
3. The console lists the available agents and starts on the default. Type a question at the
   `<agent>>` prompt and press Enter. Use `/agents` to list them again and `/agent <name>` to switch
   (e.g. `/agent MathTutor`). Press Enter on an empty line to quit.

To run the Console **standalone** (against an already-running AI service), pass the service URL:

```sh
dotnet run --project src/AgenticLab.Console -- --AiService:Url https://localhost:7123
```

Under Aspire it resolves the AI service by name (`https+http://aiservice`) via service discovery, so no
URL is needed.

## Running the web UI

The `web` resource (Blazor Server) starts automatically with the app and is exposed on an external HTTP
endpoint. Open it from the Aspire dashboard's `web` resource link. Type a message, pick an agent, then
watch the data flow light up node-by-node as the agent runs. The stepping is **gated on the backend**, so
the animation stays in sync with the real agent execution (and its OpenTelemetry spans) rather than being
a client-side replay. Two modes control the pacing:

- **Auto** — the AI service advances on its own, pausing for an adjustable *step delay* after each real
  step. The delay can be changed live, and the run can be paused/resumed at any time.
- **Manual** — the AI service blocks before each step until you click *Next*, so the next LLM round-trip
  or tool call does not start until you allow it.

The UI sends `next`, `pause`, `resume` and `stop` actions to `/chat/control`. The final answer appears
once the run completes.

**Breakpoints** in Settings pause actual execution even in Auto mode: **Before model request**,
**After model response**, **Before tool execution**, and **After tool result**. Model breakpoints
apply to every complete round-trip, including responses that request tools, not individual tokens.
Tool breakpoints apply to each local or MCP function call, including the local A2A delegation call
(not the remote agent's internal steps). The paused reason and tool name appear in both Settings
and Chat. **Continue** runs in Auto until the next selected breakpoint; **Next** switches to Manual
and releases the current boundary; **Stop** cancels. Breakpoints start off, remain selected across
conversations, and clear on page refresh. Changing selections during a run affects future boundaries
but does not release an existing pause. After-tool breakpoints follow successful tool returns;
thrown errors retain the existing error behavior. Completed side effects cannot be undone.
