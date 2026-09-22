# In-app learning (Learn panel + guided journey)

Part of the [Agentic Lab architecture notes](../AGENTS.md). The two learning surfaces: the in-app Learn panel on the [flow page](web-flow-page.md) that explains each concept the diagram shows, and the standalone guided `/learn` journey that walks from AI basics through the agent loop to running agents in production.

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

The Web flow page doubles as a teaching aid: the diagram's concepts are **clickable** and open the docked right **Learn** panel explaining what each one means. Content is authored as data, not code — each concept lives in its own folder under [src/AgenticLab.Web/Concepts](../src/AgenticLab.Web/Concepts), combining structured metadata with a markdown body (mirroring the `skills/<id>/SKILL.md` folder convention):

```
Concepts/tools/meta.json   { "title", "summary", "category", "links": [{ "label", "url" }] }
Concepts/tools/body.md     (markdown, rendered to HTML with Markdig)
```

[Services/ConceptCatalog.cs](../src/AgenticLab.Web/Services/ConceptCatalog.cs) (a singleton) discovers every `Concepts/<id>` folder at startup, parses `meta.json` with `System.Text.Json`, renders `body.md` via **Markdig**, and caches the results as immutable `Concept` records keyed by the folder id (case-insensitive). The `Concepts/**` files are copied next to the app via a `<Content Update=… CopyToOutputDirectory="PreserveNewest" />` item in [AgenticLab.Web.csproj](../src/AgenticLab.Web/AgenticLab.Web.csproj) (the Web SDK already globs them, so it is an `Update`, not an `Include`, to avoid duplicate-item errors). Editing a `body.md`/`meta.json` and restarting updates the content with no code change. The whole learning layer can be switched on with the **Learn** toggle in the page header (`FlowViewState.Concepts.ShowConcepts`, default off): when on it shows every contextual ⓘ button, the *What is MCP?* pill and the vendor info badges plus the right **Learn** panel; turning it off hides the Learn panel and clears the selected concept (the `ShowConcepts` setter clears `_activeConcept`).

The learning content renders in the right **Learn** [SidePanel](../src/AgenticLab.Web/Components/Shared/SidePanel.razor) (shown whenever **Learn** is on), filled by [Components/Pages/FlowParts/ConceptPanel.razor](../src/AgenticLab.Web/Components/Pages/FlowParts/ConceptPanel.razor) (+ scoped css). When a concept is selected it shows the eyebrow/title, summary, rendered body (`MarkupString`) and external links (`target=_blank rel=noopener`) plus a **← All topics** button; when none is selected it shows a hint and a **topic index** — every concept (from `ConceptCatalog.All`, grouped by category) as a clickable entry point. It docks as a real column (no overlay/backdrop), can be collapsed to a rail and drag-resized like any `SidePanel`, and its colours come from the themed `.flow-app` CSS variables (custom properties inherit), so it shares the same palette across vendors. Opening a concept (`FlowViewState.Concepts.OpenConcept(id)`) also turns **Learn** on and expands the Learn panel. [Components/Pages/Flow.razor](../src/AgenticLab.Web/Components/Pages/Flow.razor) injects the catalog, holds the `_activeConcept` state (on `FlowViewState`), and exposes `OpenConcept(id)`/`CloseConcept()`. Concepts are surfaced two ways: the right **Learn** panel's topic index (always reachable when concept info is on, covers every concept including those without a unique node), and contextual **ⓘ info buttons** on the relevant elements — the Harness node → `harness`, the Tools box → `tools`, the Skills box → `skills`, the LLM node → `llm` (plus a *What is MCP?* pill → `mcp`). In the **Expand agent host** anatomy view, each layer box also carries an ⓘ → its own concept: the System Prompt box → `system-prompt`, the Agent Persona box → `persona`, the Tools / MCP box → `tools`, the Settings box → `settings`, the User prompt box → `user-prompt`, and the Context box → `context`. The **Client** topic (`client`) remains available in the Learn topic index, but has no anatomy row or Details section because it sits outside the agent host. A product ⓘ button sits in the selected host's info control beside the Host selector and opens the **product** concept for that vendor (GitHub Copilot, Claude Code, Claude, ChatGPT, Gemini, Microsoft 365 Copilot) — resolved from the enabled example's `HostPresentation.ProductConceptId`, hidden for the non-product Default vendor and (like every ⓘ) only when **Learn** is on. Concepts come in two categories (the `category` field): **core** (`tools`, `skills`, `custom-instructions`, `agent`, `llm`, `reasoning`, `mcp`, `a2a`, `harness`, `system-prompt`, `client`, `persona`, `settings`, `user-prompt`, `context`, `prompt-signature`, `tokenization`, `embeddings`, `neural-network`, `where-agents-run`, `environment`, `agent-risk`, `guardrails`, `sandbox`, `securing-agents`) and **product** (`github-copilot`, `chatgpt`, `gemini`, `claude-code`, `claude`, `cursor`, `microsoft-365-copilot`, and a `agentic-coding-harness` overview). The `where-agents-run`, `environment`, `agent-risk`, `guardrails`, `sandbox` and `securing-agents` concepts back the **Environment & risk view** (the ⓘ buttons on the risk meter / guardrails box and its four learn pills — *Where it runs*, *Environment*, *Sandbox* and *Protect*). To add a concept, drop a new `Concepts/<id>/{meta.json, body.md}` folder and (optionally) wire a click target to `OpenConcept("<id>")`. **Authoring rule (see [Concepts/AUTHORING.md](../src/AgenticLab.Web/Concepts/AUTHORING.md)): a concept's `meta.json` summary and the general sections of `body.md` must be product-agnostic — explain the idea itself, not how *this* repo implements it. Put every the-series-specific detail (agent names, UI/diagram behaviour, this app's wiring) under a single trailing `## In this application (the-series)` section.** Product concepts are the only exception (they describe a specific product by nature).

## Guided agent learning

The guide and contextual Learn dock consume the shared [Blazor design system](design-system.md):
locally hosted IBM Plex fonts, document-level tokens, fine dividers and the common dotted diagram
surface. `AppHeader` supplies the guide's header. Intro, Foundation and Hosting reveal commands use
the same `LabButton` as Flow; the underlying lesson state, chapter URLs and reveal/reset behaviour are
unchanged. The guide keeps its independent scrolling lesson/navigation regions and has no backend
dependency. Component-owned styles still handle the domain-specific diagrams.

The standalone [Learn page](../src/AgenticLab.Web/Components/Pages/Learn.razor) at `/learn` supplements,
but does not replace, the contextual Learn panel. See [the reference below](#agent-guide) for the
user-facing journey and its implementation-status caveats. Flow and Discovery link to it in a new
tab to preserve the originating page's live run and page-scoped state.
Learn's header also includes the shared GitHub repository icon link, which opens the source in a
new tab without leaving the current lesson.
Learn's header and stage actions do not link to Discovery. Open Discovery from the live Flow page's
header instead; it appears as an overlay that preserves the current conversation. Direct `/discovery`
visits still work.

Learn uses a `100dvh` flex shell with independently scrolling `.stage-rail` and `.stage-main`
regions. The main region is positioned to contain absolute accessibility labels without leaking
document overflow. Below 760px navigation becomes a horizontally scrolling strip above the lesson.
Foundation and Hosting reveal toolbars are sticky within their lesson, with opaque backgrounds.
These layout rules do not change reveal state or chapter navigation.

[AgentLearningJourney](../src/AgenticLab.Web/Learning/AgentLearningJourney.cs) retains eleven stage definitions
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
product-specific "In Agentic Lab" note. Copy and node definitions live in `AgentLearningJourney`; scoped CSS owns
the responsive layouts. Focused tests protect stage node selection and references, alongside
browser checks for rendered diagrams and connectors. These diagrams are not live telemetry or
deployment/security maps. Current execution traces are distinguished from future Foundry hosting
and managed evaluations. No Foundry integration is added by this page.

The `model-to-agent`, `agent-landscape`, `anatomy-of-agent`, and post-loop `agents-everywhere`
lessons render [FoundationLesson](../src/AgenticLab.Web/Components/Pages/LearningParts/FoundationLesson.razor)
with its own scoped CSS. The pure local [FoundationStory](../src/AgenticLab.Web/Learning/FoundationStory.cs)
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
[HostingLesson](../src/AgenticLab.Web/Components/Pages/LearningParts/HostingLesson.razor) and its scoped CSS
render the four-part hosting comparison and three manual reveals. The pure
[HostingStory](../src/AgenticLab.Web/Learning/HostingStory.cs) owns platform-neutral option content,
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

[AgenticLab.Web.Tests](../tests/AgenticLab.Web.Tests/AgenticLab.Web.Tests.csproj) follows the solution's
`tests/` convention and accesses the internal journey through `InternalsVisibleTo`. Concept content
is copied to test output rather than located through repository-relative directory traversal.
Run `dotnet test tests/AgenticLab.Web.Tests/AgenticLab.Web.Tests.csproj` for stage/permalink,
navigation and concept/node-reference validation. Public page members use XML summaries, for
example `StageId` documents the stable query-string identifier.

## Agent guide

Open **Agent guide** from Flow or Discovery for the standalone `/learn` page. The link opens a new
tab so a live or paused run remains in its original page. The existing **Learn** checkbox still
controls the contextual topic panel; it is independent of the guide.

The guide fits the viewport: the header and stage navigation stay visible while the lesson pane
scrolls independently. Navigation scrolls separately when needed and becomes a horizontal strip
on narrow screens. Reveal controls stay pinned at the top of the pane while scrolling their lesson.

Ten stages connect the ideas: **Demystify**, **Agent**, **The Agentic Landscape**, **Inside the agent host**,
**Anatomy of an agent**, **The agent loop**, **Same foundation, different setting**, **The wider ecosystem**, **Where should your agent run?**, and
**Run and improve**. The short **Demystify** introduction demystifies agents: why understanding
them matters, what they are, and how a task moves through the system. It is the default opening at
`/learn` and retains the `why-agents` permalink. Each subsequent stage has a concept diagram,
and all stages link to related topics from the same concept catalog as the contextual panel.
The introduction shows **Why**, **What**, then **How**, one at a time, with Previous/Next step
controls, a counter and Restart. Returning to the stage starts at Why.
**The Agentic Landscape** connects chat, coding, office and custom purposes to one shared
agentic foundation. These overlapping examples are not a vendor taxonomy.
It has two reveals: the agent purposes, then their shared **Agent host + Model** foundation.
Execution is introduced later in the dedicated **The agent loop** chapter.
**Agent** builds **Agent host + Model** in seven reveals: a names-only Agent host + Model
overview, host details and responsibilities, host tool execution, model details,
the outbound context/tool definitions/tool results, the model's answer or tool
request, and the enclosing agent boundary. The agent host manages context, instructions, tools,
memory and execution controls; the model reasons, plans and chooses a next step or final answer.
**The model reasons. The agent host acts. Together, they form an agent.** A model request is not
permission to execute. Harness remains the technical term for the host's agent-running machinery;
host describes a software role, not a machine. Triggers remain outside the agent boundary.
Its original `/learn?stage=model-to-agent` permalink is preserved.
Both component boxes and the plus sign remain visible from the opening overview; descriptions,
responsibilities, tools and exchange paths appear progressively without moving the boxes.
These two lessons, **Anatomy of an agent**, and **Same foundation, different setting** provide Previous/Next reveal controls,
**Show complete diagram** and **Restart**, separate from chapter navigation. Reveals reserve
their layout space, are manually advanced (no autoplay), and respect reduced motion.
Reveal progress resets on a chapter change or reload; opening a related concept does not reset it.
**Inside the agent host** uses the overview's five responsibilities in the same order: **Gather context**,
**Load instructions**, **Make tools available**, **Manage memory**, and **Enforce execution controls**.
Its `inside-the-harness` permalink remains unchanged.
**Anatomy of an agent** (`/learn?stage=anatomy-of-agent`) follows with eight cumulative reveals:
system prompt, available host capabilities, agent persona, selected tools and model/settings,
task prompt, custom instructions, skill descriptions, and a loaded skill body. The layered diagram
distinguishes standing guidance, configured capabilities, and per-task context. MCP tools and A2A
delegation are labelled separately; a skill is guidance loaded through an allowed tool, not extra permissions.
The **Chat / Office / Coding / Custom** selector changes the entire anatomy to fit its purpose:
system guidance, capability catalogue and selected subset, persona, model role, required controls,
task, custom instructions and playbooks. **Office** is a Microsoft 365 Copilot-style example with
mail, calendar, document and people connectors plus sending mail with user confirmation.
Its **Meeting assistant / Document reviewer** selector contrasts briefing and confirmed communications
with read-only document comparison. The reviewer has its own task, model-choice rationale and
playbooks; only document search and skill reading are selected, with no sending, editing or deletion.
**Chat** researches questions; **Custom** investigates equipment alerts with read-only telemetry
and manuals, without equipment control. These are illustrative teaching configurations, not product
replicas or claims about native connectors, skills, models or enforcement in Microsoft 365.
**Coding** offers **Ask / Plan / Implement / Review**. Ask, Plan and Review stay read-only;
Implement selects workspace writes and terminal use with approval or allowlisting, resource limits
and no production access. Its task and playbooks demonstrate an edit-and-test workflow.
Every persona across Chat, Office, Coding and Custom carries a **Model choice example** and a short
rationale: conversational speed, long-context synthesis, planning/review reasoning, reliable coding
tool use or domain-tested alert triage. These are capability-based trade-offs, not required model
products or automatic routing. Model choice never grants permissions. The host prompt and capability
catalogue remain shared within each purpose. Purpose and mode changes preserve reveal progress; choosing a purpose
selects its first mode. **Restart** retains the selected purpose and mode; leaving and reentering the
chapter or reloading restores Coding / Ask.
Opening a related topic preserves both selection and progress. No backend agent is selected and no
model, tool or discovery call runs. On narrow screens the layers stack in reveal order.
**The agent loop** shows the model's decision, the
tool execution and observation cycle, and a separate final-answer exit that can bypass tools.
**Same foundation, different setting** compares illustrative local/cloud application configurations
and names familiar applications by purpose: ChatGPT, Gemini and Claude for Chat; GitHub Copilot,
Claude Code and Gemini Code Assist for Coding; Microsoft 365 Copilot and Gemini for Google Workspace
for Office; and an in-house agent or business application for Custom. Products can span purposes,
and agentic capabilities depend on mode/configuration. These examples do not claim that the
local/cloud comparisons describe those products. The configurations show the shared foundation
for each purpose, including context, tools and controls. A separate user/schedule/event selector
explains triggers. A local application can use a remote model; portability and tool access are
not automatic. These controls never move agents, schedule work or make model calls.
**Where should your agent run?** (`/learn?stage=where-to-run`) compares **Personal runtime**,
**Existing product**, **Your own service**, and **Managed agent platform** using one report-review task.
Each option separates agent host, model service, tools/data access and operational ownership,
then explains its trade-off and the approvals or capabilities needed to use it. These are operating
models, not a list of platforms available in your organization.
The **Vendors and stacks** list adds expandable, officially sourced examples for the selected
category: Microsoft Agent Framework, OpenAI Agents SDK, Claude Agent SDK, Google ADK, Strands
Agents and LangGraph for code-based stacks; Microsoft 365 Copilot, Copilot Studio, ChatGPT GPTs
and workspace agents, Gemini Gems and n8n Cloud for product-based work; Foundry Agent Service,
Google's Gemini Enterprise Agent Platform Agent Runtime, Amazon Bedrock AgentCore Runtime,
Claude Managed Agents and LangSmith Deployment for managed operation. Self-hosted n8n appears
under personal/own-service options; LangSmith Deployment spans own-service and managed options.
Each entry distinguishes its offering type from hosting and links to an official source. Sources
were checked on 17 September 2026; preview/beta, license and access caveats are not guarantees of
organizational availability. Category changes collapse the new list's details, without resetting
the lesson's reveal, trigger or readiness selections.
Three manual reveals cover the comparison, triggers/supervision and hybrid connections, then
operational readiness. Later sections remain hidden and unfocusable until revealed. User request,
schedule and event are independent of hosting; all retain review before publication in this example.
**Try it yourself / Share with a team / Run operationally** compares identity, state and isolation,
recovery, cost and release controls. Local-to-cloud is one possible path, not a requirement or a
promise of portability. Selections preserve reveal progress; Restart preserves selections, while
chapter reentry or reload resets them. Related topics do not reset state. No deployment, platform
discovery, scheduling or backend calls are performed by this lesson.
**The wider ecosystem** contrasts MCP tool calls with A2A delegation, without exposing agent internals.
**Run and improve** follows Run, Observe, Evaluate and Improve back to the next tested version;
this operating cycle is separate from the agent's per-turn execution loop.
Stage URLs such as `/learn?stage=agent-loop` support bookmarks, reload and browser Back/Forward.
The product-specific **Map to Microsoft Foundry** stage is retained in code but hidden from the guide.
Previous/Next skip it, and **Run and improve** is stage 08. Hidden or unknown stage IDs fall back
to the first stage. Live-flow links navigate only; they never send a prompt.
Discovery is available from the live Flow header, not from the guide. It opens as a modal overlay
that keeps the conversation, draft and execution history intact. Close, Escape or a backdrop click
returns to the same conversation. Opening only reads the discovery snapshot; re-discovery is explicit
and unavailable while the current chat runs. The standalone `/discovery` URL remains available.

The diagrams are explanations, not live telemetry. Landscape and environment comparisons are
illustrative, not product or deployment guarantees. The final stage distinguishes today's execution traces from future
Foundry hosting and managed-evaluation integration. The guide works without AiService or Azure
credentials; run only the Web project and visit `/learn`:

```sh
dotnet run --project src/AgenticLab.Web
```

Focused tests validate stable stage URLs, navigation, reveal bounds/reset, independent example/trigger
selection, per-stage node selection and references to the shipped concept content:

```sh
dotnet test tests/AgenticLab.Web.Tests/AgenticLab.Web.Tests.csproj
```
