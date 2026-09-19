## What is a persona?

A **persona** is the part of the instructions that makes one agent *different* from
another — its role, its voice, and the way it should approach a task. Where the system
prompt sets the rules every agent shares, the persona sets the character of a single agent.

## Layered on the system prompt

The model's full instructions are built in layers: a shared, cross-cutting set of rules
followed by the agent's own persona. The shared rules are the constant; the persona is the
variable. Same model, same rules — but one agent might research concisely while another
explains arithmetic patiently.

## In this application (Agentic Lab)

Each agent's instructions are composed from a shared **harness** prompt (scoped in
`<harnessMode>` tags) followed by the agent's **persona** (scoped in `<agentMode>` tags).
The **Agent Persona** box is an **agent** layer (yellow) — set per agent. It shows the
selected agent's name and description. Switching the agent (or the vendor's "mode") swaps
the persona while the harness stays the same, which is why the box updates but the System
Prompt box doesn't.
