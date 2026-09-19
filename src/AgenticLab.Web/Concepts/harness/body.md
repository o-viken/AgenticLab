## What is an agent host?

The **agent host** is the software that manages an agent's run around the model.
Its agent-running machinery is also called the **harness**. Here, host means that
software role, not a particular machine, cloud provider or product.

**Agent host = Context + Instructions + Tools + Memory + Execution controls.**

- **Context**: gathers the task, relevant information and tool results for the next model request.
- **Instructions**: loads operating guidance, the agent persona and applicable task guidance.
- **Tools**: exposes a bounded catalogue and dispatches permitted requests to actual implementations.
- **Memory**: retains conversation or other state outside the model and selects what to include in context.
- **Execution controls**: enforces permissions, approvals, limits, cancellation and other runtime checks.

The model proposes a next step; the host checks it, executes an allowed action and returns
the result to the model. A final answer is delivered to the client. Instructions guide
behavior, but enforcement must happen outside the prompt. The host can be local or remote,
independently of where model inference runs.

## In this application (Agentic Lab)

The **Agent host** node merges the client and the AI service. Turn on **Expand agent host** to
break it into colour-coded layers — *application* (system prompt
+ plumbing), *agent* (persona, tools, settings) and *user* (the prompt) — and watch the
**Context** bar grow as each turn adds to what the model sees.

Conversation memory is retained in the service with a sliding inactivity expiry; it is not
permanent model memory. Workspace access and tool selection are enforced by application code.

> **Agent = Agent host + Model.** The **Agent host** boundary excludes the model;
> the **Agent** boundary includes both.
