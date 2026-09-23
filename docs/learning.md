# In-app learning (Learn panel + guided journey)

Agentic Lab has two learning surfaces: a contextual **Learn** panel on the
[Flow page](web-flow-page.md) and a standalone **Agent guide** at `/learn`.

Both use **Agent = Agent host + Model**. The host manages context, instructions, tools, memory and
execution controls; the model reasons and chooses an answer or next step. A tool request is not
permission: the host checks and executes permitted actions. **Harness** means the host's
agent-running machinery, not the whole agent or a particular machine. Memory is retained state;
context is the input selected for a model request.

## In-app learning content (Learn panel)

Enable **Learn** in the Flow header to show contextual info buttons and the right-hand topic panel.
It starts off. Choose a topic from the index or an info button in the diagram; **All topics** returns
to the index. Topics cover core concepts and products, including concepts without a diagram node.
For example, Client appears in the index but not in the host anatomy because it sits outside the host.

Opening a concept enables Learn and expands its dock. The dock can be resized or collapsed;
turning Learn off hides it and clears the selected topic. It is independent of the read-only
**Details** dock, so both can remain open. Product info beside the Host selector comes from the
enabled example's metadata; Default has no product topic.

## Guided agent learning

Open **Agent guide** from Flow or Discovery. It opens a new tab so a live or paused run stays in
its original page. The guide also works without AiService or Azure credentials:

```sh
dotnet run --project src/AgenticLab.Web
```

Visit `/learn` at the printed Web URL. Lessons are local explanations, not live telemetry or
deployment/security maps. Their controls make no model, tool, discovery, scheduling or deployment
calls. Live-flow links navigate without sending a prompt. For Discovery, use the
[Flow overlay or standalone page](protocols.md#discovery-visualization-mcp--a2a); Learn has no
Discovery entry.

## Agent guide

The guide opens at **Demystify** and follows these ten stages. Bookmark any stage with
`/learn?stage=<id>`; reload and browser Back/Forward preserve navigation. Hidden or unknown IDs
fall back to the first stage. The product-specific `map-to-foundry` definition is hidden.

| Stage | ID | Focus |
| --- | --- | --- |
| Demystify | `why-agents` | Why agents matter, what they are, and how a task flows through them. |
| Agent | `model-to-agent` | Host + model, their responsibilities, and the exchange between them. |
| The Agentic Landscape | `agent-landscape` | Chat, coding, office and custom purposes share one foundation. |
| Inside the agent host | `inside-the-harness` | Gather context, load instructions, make tools available, manage memory, enforce execution controls. |
| Anatomy of an agent | `anatomy-of-agent` | Shared guidance, persona, tools, model settings and per-task context. |
| The agent loop | `agent-loop` | Decide, execute, observe; a final answer can bypass tools. |
| Same foundation, different setting | `agents-everywhere` | Compare purposes, local/cloud settings and user/schedule/event triggers. |
| The wider ecosystem | `wider-ecosystem` | MCP tool calls versus A2A delegation. |
| Where should your agent run? | `where-to-run` | Personal runtime, existing product, own service or managed agent platform. |
| Run and improve | `run-and-improve` | Run, observe, evaluate and improve across versions, not within one turn. |

### Navigation and reveals

Chapter navigation and lesson reveals are separate. **Previous/Next**, **Show complete diagram**
where available, and **Restart** control the current lesson. There is no autoplay. Chapter changes
or reloads reset progress; opening a related concept preserves it. Restart preserves example
selections. Demystify returns to **Why** when revisited.

The lesson and navigation scroll independently; navigation becomes a horizontal strip on narrow
screens. Reveal controls stay visible while scrolling. Unrevealed content is excluded from focus
and assistive technology; reduced motion disables effects, not manual progression. Both learning
surfaces reuse the [Blazor design system](design-system.md).

### Illustrative configurations

**Anatomy** builds up guidance, available capabilities, persona, selected tools and model/settings,
task, custom instructions, skill descriptions and a loaded skill. **Chat / Office / Coding / Custom**
changes the example, not the selected backend agent. Each persona explains its model trade-offs;
neither model choice, persona, instructions nor skills grant permissions.

- Office contrasts a meeting assistant's confirmed communications with a read-only document reviewer.
- Coding offers Ask, Plan, Implement and Review. Only Implement includes writes and terminal use,
  with approval or allowlisting, resource limits and no production access.
- Chat researches questions; Custom reads equipment telemetry and manuals without controlling equipment.

Changing purpose selects its first mode without resetting reveals. Restart keeps the selection;
reentering Anatomy restores Coding / Ask. These are original teaching examples, not vendor prompts,
product replicas or claims about native connectors and enforcement.

**Hosting** separates the agent host, model service, tools/data access and operational ownership.
Trigger and readiness selections are independent of hosting and reveal progress. Restart keeps
selections; chapter reentry resets them. A local host can use a remote model, and moving to cloud
does not guarantee portability or tool access.

The expandable **Vendors and stacks** entries link to official sources and distinguish frameworks,
products and managed runtimes. Offerings can span categories. Sources were checked on
17 September 2026; preview, licensing and access caveats do not guarantee organizational availability.
Changing category collapses entry details without resetting the lesson. Foundry hosting and managed
evaluation integration are future directions, not capabilities implemented by the guide.

## Authoring and verification

Each topic lives in [Concepts](../src/AgenticLab.Web/Concepts) as a `Concepts/<id>` folder containing:

```text
meta.json   { "title", "summary", "category", "links": [{ "label", "url" }] }
body.md     Markdown explanation
```

[ConceptCatalog](../src/AgenticLab.Web/Services/ConceptCatalog.cs) loads topics at startup and renders
Markdown with Markdig. Edit the files and restart Web. Categories are `core` and `product`; adding
a topic makes it available in the index, with an optional contextual `OpenConcept("<id>")` action.
Preserve existing concept IDs such as `harness` and `llm` and the stage IDs above.

Follow the [authoring rules](../src/AgenticLab.Web/Concepts/AUTHORING.md): summaries and general
explanations are product-agnostic. Put Agentic Lab-specific details in one trailing
`In this application (Agentic Lab)` section. Product topics are the exception.

[AgentLearningJourney](../src/AgenticLab.Web/Learning/AgentLearningJourney.cs) owns stages and topic
references; [FoundationStory](../src/AgenticLab.Web/Learning/FoundationStory.cs) and
[HostingStory](../src/AgenticLab.Web/Learning/HostingStory.cs) own illustrative content and reveal
state. Keep source-review dates aligned with the curated hosting list when rechecking it.

Tests cover stage URLs, navigation, reveal/reset behavior and topic references:

```sh
dotnet test tests/AgenticLab.Web.Tests/AgenticLab.Web.Tests.csproj
```
