## What is an agent?

**Agent = Agent host + Model.** An agent is the system that uses a model to make
decisions and carry out actions, not the model alone.

The **agent host** gathers and manages **context, instructions, tools, memory and
execution controls**. Its agent-running machinery is also called the **harness**.
The **model** reasons over the supplied context, plans and chooses a next step or final answer.

## Who does what?

1. The user sends a request to the agent host.
2. The host gathers context, loads instructions, selects relevant memory and exposes tool definitions.
3. The model returns a tool request or a final answer.
4. For a tool request, the host checks permissions and execution controls, then executes it only if allowed.
5. The host returns the result to the model as context for its next decision.
6. When the model returns a final answer, the host delivers it to the user.

A tool request is not permission to act. The host can reject it, require approval or stop
the run. An agent can also answer without using tools; a loop need not repeat indefinitely.

> **The model reasons. The agent host acts. Together, they form an agent.**

## In this application (Agentic Lab)

Each agent has its own **persona** (its job and tone) layered on top of shared host instructions,
plus a bounded **toolset**:

- **WikiAssistant** — concise research grounded in Wikipedia.
- **MathTutor** — patient arithmetic tutor.
- **TriviaMaster** — playful host that researches *and* calculates.
- **ChatAgent** — friendly conversation, no tools.
- **Coder** — workspace-scoped coding agent that reads, writes and runs commands.

The user sits **outside** the agent: you send a prompt and read the reply, but the
decision-execution-observation loop happens inside the agent. The dashed **Agent** boundary in the
diagram makes this explicit: it wraps the agent host *and* the model together.
