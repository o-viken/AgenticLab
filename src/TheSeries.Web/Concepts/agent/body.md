## What is an agent?

An **agent = model + harness**. The language model supplies the reasoning; the **harness**
supplies everything around it: the system prompt, the available tools, the conversation
history, and the loop that runs the model's tool calls and feeds the results back.

A plain chat completion answers once and stops. An agent **keeps going** — it can think,
call a tool, observe the result, and decide what to do next, repeating until the task is
done. That think → act → observe loop is the heart of "agentic" behaviour.

## In this application (the-series)

Each agent has its own **persona** (its job and tone) layered on top of a shared harness,
plus a bounded **toolset**:

- **WikiAssistant** — concise research grounded in Wikipedia.
- **MathTutor** — patient arithmetic tutor.
- **TriviaMaster** — playful host that researches *and* calculates.
- **ChatAgent** — friendly conversation, no tools.
- **Coder** — workspace-scoped coding agent that reads, writes and runs commands.

The user sits **outside** the agent: you send a prompt and read the reply, but the
think-act-observe loop happens inside the agent. The dashed **Agent** boundary in the
diagram makes this explicit — it wraps the model *and* the harness together.
