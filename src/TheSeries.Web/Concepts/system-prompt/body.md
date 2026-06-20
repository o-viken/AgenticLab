## What is the system prompt?

The **system prompt** is the standing instruction the model reads before anything else.
It is written by the **application/harness**, not the user, and it frames *how* the model
should behave for the whole run — the operating rules, the tone, and how to drive the
think → act → observe tool loop.

## Why it matters

The same model behaves very differently depending on its system prompt. It is where the
harness establishes the ground rules every turn runs under:

- Ground answers in tool results; don't fabricate.
- Prefer tools over memory when a tool can get the real answer.
- Be concise and transparent about what you did.

Because it is sent on *every* request, the model can never "forget" these rules mid-run.

## In this application (the-series)

In the anatomy view the **System Prompt** box is coloured as an **application** layer
(red) — it is set by the harness, not per agent. It corresponds to the shared *harness*
prompt that every agent inherits, layered ahead of each agent's own **persona**. Open the
**Harness** concept to see how the harness assembles this around the model.
