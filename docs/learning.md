# In-app learning (Learn panel + guided journey)

Part of the [TheSeries architecture notes](../AGENTS.md). The two learning surfaces: the in-app Learn panel on the [flow page](web-flow-page.md) that explains each concept the diagram shows, and the standalone guided `/learn` journey that walks from AI basics through the agent loop to running agents in production.

## In-app learning content (Learn panel)

The primary teaching vocabulary is **Agent = Agent host + Model**. The host manages context,
instructions, tools, memory and execution controls; the model reasons, plans and chooses a next
step or final answer. Model tool requests are proposals, not authorization: the host checks and
executes permitted actions and returns results as context. Memory is retained state outside the
model; context is the input selected for a request. The host lesson matches the overview's five labels
and order: **Gather context**, **Load instructions**, **Make tools available**, **Manage memory**,
and **Enforce execution controls**. Context and memory have separate nodes.
**Harness** remains the technical name for the host's agent-running machinery,
not a replacement name for the complete agent. Host describes software, not a particular machine.
Flow labels use **Agent host**, **Model** and **Expand agent host**; vendor titles remain visible.
Existing concept IDs (`harness`, `llm`), code names and stage URLs (`inside-the-harness`) remain stable.

The Web flow page doubles as a teaching aid: the diagram's concepts are **clickable** and open the docked right **Learn** panel explaining what each one means. Content is authored as data, not code — each concept lives in its own folder under [src/TheSeries.Web/Concepts](../src/TheSeries.Web/Concepts), combining structured metadata with a markdown body (mirroring the `skills/<id>/SKILL.md` folder convention):

```
Concepts/tools/meta.json   { "title", "summary", "category", "links": [{ "label", "url" }] }
Concepts/tools/body.md     (markdown, rendered to HTML with Markdig)
```

[Services/ConceptCatalog.cs](../src/TheSeries.Web/Services/ConceptCatalog.cs) (a singleton) discovers every `Concepts/<id>` folder at startup, parses `meta.json` with `System.Text.Json`, renders `body.md` via **Markdig**, and caches the results as immutable `Concept` records keyed by the folder id (case-insensitive). The `Concepts/**` files are copied next to the app via a `<Content Update=… CopyToOutputDirectory="PreserveNewest" />` item in [TheSeries.Web.csproj](../src/TheSeries.Web/TheSeries.Web.csproj) (the Web SDK already globs them, so it is an `Update`, not an `Include`, to avoid duplicate-item errors). Editing a `body.md`/`meta.json` and restarting updates the content with no code change. The whole learning layer can be switched on with the **Show concept info** toggle in the page header (`_showConcepts`, default off): when on it shows every contextual ⓘ button, the *What is MCP?* pill and the vendor info badges plus the right **Learn** panel; turning it off hides the Learn panel and clears the selected concept (the `ShowConcepts` setter clears `_activeConcept`).

The learning content renders in the right **Learn** [SidePanel](../src/TheSeries.Web/Components/Shared/SidePanel.razor) (shown whenever **Show concept info** is on), filled by [Components/Pages/FlowParts/ConceptPanel.razor](../src/TheSeries.Web/Components/Pages/FlowParts/ConceptPanel.razor) (+ scoped css). When a concept is selected it shows the eyebrow/title, summary, rendered body (`MarkupString`) and external links (`target=_blank rel=noopener`) plus a **← All topics** button; when none is selected it shows a hint and a **topic index** — every concept (from `ConceptCatalog.All`, grouped by category) as a clickable entry point. It docks as a real column (no overlay/backdrop), can be collapsed to a rail and drag-resized like any `SidePanel`, and its colours come from the themed `.flow-app` CSS variables (custom properties inherit), so it shares the same palette across vendors. Opening a concept (`FlowViewState.Concepts.OpenConcept(id)`) also turns **Show concept info** on and expands the Learn panel. [Components/Pages/Flow.razor](../src/TheSeries.Web/Components/Pages/Flow.razor) injects the catalog, holds the `_activeConcept` state (on `FlowViewState`), and exposes `OpenConcept(id)`/`CloseConcept()`. Concepts are surfaced two ways: the right **Learn** panel's topic index (always reachable when concept info is on, covers every concept including those without a unique node), and contextual **ⓘ info buttons** on the relevant elements — the Harness node → `harness`, the Tools box → `tools`, the Skills box → `skills`, the LLM node → `llm` (plus a *What is MCP?* pill → `mcp`). In the Expert **Expand harness** anatomy view, each layer box also carries an ⓘ → its own concept: the System Prompt box → `system-prompt`, the Client box → `client`, the Agent Persona box → `persona`, the Tools / MCP box → `tools`, the Settings box → `settings`, the User prompt box → `user-prompt`, and the Context box → `context`. A product ⓘ button sits in each vendor rail item's hover tooltip and opens the **product** concept for that vendor (GitHub Copilot, Claude Code, Claude, ChatGPT, Gemini, Microsoft 365 Copilot) — resolved via `VendorCatalog.ProductConceptId`, hidden for the non-product Default vendor and (like every ⓘ) only when **Show concept info** is on. Concepts come in two categories (the `category` field): **core** (`tools`, `skills`, `custom-instructions`, `agent`, `llm`, `reasoning`, `mcp`, `a2a`, `harness`, `system-prompt`, `client`, `persona`, `settings`, `user-prompt`, `context`, `prompt-signature`, `tokenization`, `embeddings`, `neural-network`, `where-agents-run`, `environment`, `agent-risk`, `guardrails`, `sandbox`, `securing-agents`) and **product** (`github-copilot`, `chatgpt`, `gemini`, `claude-code`, `claude`, `cursor`, `microsoft-365-copilot`, and a `agentic-coding-harness` overview). The `where-agents-run`, `environment`, `agent-risk`, `guardrails`, `sandbox` and `securing-agents` concepts back the **Environment & risk view** (the ⓘ buttons on the risk meter / guardrails box and its four learn pills — *Where it runs*, *Environment*, *Sandbox* and *Protect*). To add a concept, drop a new `Concepts/<id>/{meta.json, body.md}` folder and (optionally) wire a click target to `OpenConcept("<id>")`. **Authoring rule (see [Concepts/AUTHORING.md](../src/TheSeries.Web/Concepts/AUTHORING.md)): a concept's `meta.json` summary and the general sections of `body.md` must be product-agnostic — explain the idea itself, not how *this* repo implements it. Put every the-series-specific detail (agent names, UI/diagram behaviour, this app's wiring) under a single trailing `## In this application (the-series)` section.** Product concepts are the only exception (they describe a specific product by nature).

## Guided agent learning

The standalone [Learn page](../src/TheSeries.Web/Components/Pages/Learn.razor) at `/learn` supplements,
but does not replace, the contextual Learn panel. See [README.md](../README.md#agent-guide) for the
user-facing journey and its implementation-status caveats. Flow and Discovery link to it in a new
tab to preserve the originating page's live run and page-scoped state.

Learn uses a `100dvh` flex shell with independently scrolling `.stage-rail` and `.stage-main`
regions. The main region is positioned to contain absolute accessibility labels without leaking
document overflow. Below 760px navigation becomes a horizontally scrolling strip above the lesson.
Foundation and Hosting reveal toolbars are sticky within their lesson, with opaque backgrounds.
These layout rules do not change reveal state or chapter navigation.

[AgentLearningJourney](../src/TheSeries.Web/Learning/AgentLearningJourney.cs) retains eleven stage definitions
in `AllStages`, with stable IDs, diagram nodes, concept references and optional local demo links.
Its `Stages` list excludes definitions marked `Hidden`; the product-specific `map-to-foundry`
stage is hidden, leaving ten visible stages. The opening `why-agents` stage is titled **Demystify**.
Its three short sections demystify agents: why understanding them matters, what they are, and how a
task moves through the system. `IntroductionSteps` owns this copy. Learn's local step index shows
one part at a time using bounded Previous/Next controls, a counter and Restart. Inactive parts
reserve layout space but are hidden visually and from assistive technology; the current part is
announced politely. Stage changes reset to Why; related-topic rerenders preserve the current step.
No diagram or backend calls are needed. The introduction's controls and text share the other lessons' bordered
24-pixel grid surface. All navigation, numbering and URL resolution use
that filtered list, so Previous/Next skip hidden stages and hidden URLs fall back to the first stage.
The page code-behind
resolves `?stage=` case-insensitively with a first-stage fallback, handles focus after navigation,
and opens existing `ConceptCatalog` content inline. It does not depend on `FlowViewState`,
`FlowRunController`, AiService or cloud credentials. Each stable stage ID selects its own diagram
in Learn.razor: model-plus-harness composition, five agent host responsibilities, the per-turn
decision/execution/observation loop, MCP versus A2A connections, a three-row Foundry capability
mapping, and the Run/Observe/Evaluate/Improve operating cycle. Only `map-to-foundry` has
`PlatformMap` set, controlling its conceptual badge; the lifecycle
is platform-neutral. Lessons move from the takeaway directly to related concepts, without a
product-specific "In TheSeries" note. Copy and node definitions live in `AgentLearningJourney`; scoped CSS owns
the responsive layouts. Focused tests protect stage node selection and references, alongside
browser checks for rendered diagrams and connectors. These diagrams are not live telemetry or
deployment/security maps. Current execution traces are distinguished from future Foundry hosting
and managed evaluations. No Foundry integration is added by this page.

The `model-to-agent`, `agent-landscape`, `anatomy-of-agent`, and post-loop `agents-everywhere`
lessons render [FoundationLesson](../src/TheSeries.Web/Components/Pages/LearningParts/FoundationLesson.razor)
with its own scoped CSS. The pure local [FoundationStory](../src/TheSeries.Web/Learning/FoundationStory.cs)
owns bounded reveal progression, captions and illustrative task/environment examples. Chapter changes
reset reveals; parent rerenders (such as opening a related concept) preserve them. Purpose and trigger
selection are independent. The host/model composition introduces the harness as the host's
agent-running machinery, reveals controls/tools before the exchange paths, and keeps triggers outside the agent
boundary. Desktop exchange paths become vertical on narrow containers. Hidden reveals reserve space and
are excluded from accessibility/focus; reduced motion disables transitions, not manual progression.
The landscape shares one foundation across overlapping purposes; local/cloud comparisons describe
context, tools and permissions, not automatic portability, real scheduling or deployment. These lessons
keep the landscape to two reveals: purposes, then the shared host/model foundation. It has no agent-loop
diagram; execution remains in the dedicated `agent-loop` chapter. These lessons
do not reference live Flow state or call backend APIs. Existing stage permalinks remain valid; `/learn`
now starts at Demystify. See README for the ten-stage order and presentation controls.

The `wider-ecosystem` lesson follows `agents-everywhere`, introducing optional MCP tool connections
and A2A delegation before hosting choices. The `where-to-run` lesson follows `wider-ecosystem`
and precedes `run-and-improve`: tools, data and connections inform hosting, then operational
responsibilities lead into the improvement cycle. Stage IDs and bookmarks remain unchanged.
[HostingLesson](../src/TheSeries.Web/Components/Pages/LearningParts/HostingLesson.razor) and its scoped CSS
render the four-part hosting comparison and three manual reveals. The pure
[HostingStory](../src/TheSeries.Web/Learning/HostingStory.cs) owns platform-neutral option content,
trigger and operational-readiness selections, and bounded reveal progress. Those selections are
independent; Restart preserves them. Removing the component on chapter navigation resets its state,
while related-topic rerenders preserve it. Unrevealed sections use native `hidden` and are not focusable.
This is separate from FoundationStory's space-reserving diagrams. Existing Lucide assets and Learn
tokens are reused, with container-based stacking. No deployment or service calls occur; product
features and organizational platform access are not assumed. Existing journey tests cover placement,
references, bounded reveals and independent selections. See README for the operating-model comparison.
`HostingStory.Examples` contains sourced `HostingExample` records with vendor, offering type,
explanation and explicit `OptionIds`; `CurrentExamples` filters by the selected operating model.
An offering can span categories. Frameworks are distinguished from managed runtime products,
and n8n's cloud/self-hosted options remain separate. The always-visible **Vendors and stacks** list
uses native `details` with official-source links; its category key resets expansion on selection
changes. No vendor backend, discovery or deployment integration is introduced. Tests cover valid
category mappings, required examples, cross-vendor coverage and HTTPS sources. Keep the visible
source-review date and README in sync when rechecking this curated list.

`anatomy-of-agent` sits between harness responsibilities and the agent loop. Its eight cumulative
reveals cover the shared system prompt/catalogue, persona, configured tools and model/settings,
task prompt, custom instructions, skill descriptions and a loaded playbook. `FoundationStory`
owns `AnatomyPurpose` profiles containing separate `AnatomyExample`, `AnatomyCapability` and
`AnatomySkill` data. The always-available Chat / Office / Coding / Custom selector switches the
system guidance, catalogue, subset, persona, model example, required controls, task, instructions and skills.
Office is Microsoft 365 Copilot-inspired work grounding and confirmed sending, not a native product
contract; Chat researches questions and Custom reads equipment telemetry without equipment control.
Office has two selectable personas: **Meeting assistant** (the existing `office-agent` ID) prepares
briefings and confirmed communications; **Document reviewer** compares proposals against approved
briefs with document search and skill reading only. The reviewer has its own task, comparison
playbooks, reasoning-model rationale and read-only controls, with no email, editing or deletion.
Both share Office's system prompt and catalogue; switching preserves reveal progress.
The shared system prompts are original teaching examples, each under 60 words, not vendor prompt
copies: Chat emphasizes direct answers and source/tool grounding; Office uses work sources, actions
and confirmed communications; Coding follows repository instructions, preserves unrelated work and
verifies permitted edits; Custom checks measurement quality and escalates to an operator.
Coding offers Ask / Plan / Implement / Review with a shared catalogue. Only Implement selects writes
and terminal execution; it overrides the read-only purpose controls with workspace scope, command
approval or allowlisting, time/resource limits and no production access. It has its own task and playbooks.
Each `AnatomyExample` owns a model choice and rationale across all four purposes, plus an optional
controls override. Settings renders these for the selected persona, falling back to purpose controls.
Model examples describe capability, context, latency, cost and evaluation trade-offs, not specific
deployments or automatic routing. Neither a model nor persona grants tool permissions.
These are not backend agents. Purpose selection chooses its first mode without changing reveal progress.
Restart preserves selection; chapter reentry resets it to Coding / Ask. Hidden persona controls are inert.
MCP tools and A2A delegation remain distinct; instructions and skills do not grant permissions.
Anatomy styles stay in FoundationLesson.razor.css and stack by container width. Existing journey
tests cover ordered permalinks, reveal/reset semantics, per-persona model descriptions and bounded
capability subsets, including Implement-only write and terminal access.

[TheSeries.Web.Tests](../tests/TheSeries.Web.Tests/TheSeries.Web.Tests.csproj) follows the solution's
`tests/` convention and accesses the internal journey through `InternalsVisibleTo`. Concept content
is copied to test output rather than located through repository-relative directory traversal.
Run `dotnet test tests/TheSeries.Web.Tests/TheSeries.Web.Tests.csproj` for stage/permalink,
navigation and concept/node-reference validation. Public page members use XML summaries, for
example `StageId` documents the stable query-string identifier.
