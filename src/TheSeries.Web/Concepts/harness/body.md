## What is a harness?

The **harness** is everything that wraps the model to turn it into a working agent. The
model thinks; the harness does the rest:

- **Assembles context** — system prompt, persona, conversation history, and (per run) the
  skills catalogue and environment info.
- **Exposes a bounded toolset** — only the tools this agent is allowed to use.
- **Runs the loop** — sends a request to the model, executes any tool calls it makes,
  feeds the results back, and repeats (think → act → observe).
- **Relays results** — returns the final answer to the client.

Because the harness controls the toolset and the prompt, it's also where **safety and
limits** live: tools are scoped to a workspace, commands are allowlisted, and the model
only ever sees the tools you leave enabled.

## In the diagram

The **Harness** node merges the client and the AI service. Turn on **Expand harness** in
the Expert perspective to break it into colour-coded layers — *application* (system prompt
+ plumbing), *agent* (persona, tools, settings) and *user* (the prompt) — and watch the
**Context** bar grow as each turn adds to what the model sees.

> **Agent = Model + Harness.** The dashed **Harness** boundary wraps the scaffolding; the
> **Agent** boundary additionally wraps the LLM.
