## What is the user prompt?

The **user prompt** is the message you type — the request the agent is being asked to
handle this turn. It is the one piece of the context authored by the **user**; everything
else (system prompt, persona, tool catalogue) is supplied by the application or the agent.

## How it fits the context

When a turn runs, the harness assembles the full context the model sees: the system prompt
and persona first, then the tool definitions, then the conversation so far — and finally
your user prompt. The model reads all of it and decides what to do next.

In a multi-turn conversation each new user prompt is appended to the earlier messages, so
the model still sees the history that came before.

## In this application (Agentic Lab)

The **User prompt** box is the **user** layer (green) — the only green box, because it is
the only part you author. It shows the message currently being sent. Watch the **Context**
visualization below it: your prompt is the first content chip to land, and the agent's tool
results and replies accumulate on top of it as the run streams.
