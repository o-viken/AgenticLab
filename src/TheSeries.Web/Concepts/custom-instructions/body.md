## What are custom instructions?

**Custom instructions** are project-specific rules that live in the workspace and are
**always applied** to the model. Where a *skill* is a playbook the model pulls in only when
a task needs it (progressive disclosure), custom instructions are the opposite: their full
text is injected into the model's context on **every** run, so the agent always follows them.

They let a team encode its conventions once — coding style, tone, preferred libraries, things
to avoid — and have every agent run honour them without repeating the guidance in each prompt.
Because they always apply, they are kept short and focused on durable rules rather than
one-off task steps.

## In this application (the-series)

A custom instruction is a `*.instructions.md` file under the workspace's `instructions/`
folder, with an optional `description` in its frontmatter (the name defaults to the file
name):

```
instructions/code-style.instructions.md
---
description: Project coding conventions the agent must follow.
---
(the rules the agent should always follow)
```

1. Before and during a run, the harness scans `instructions/*.instructions.md` and injects
   each file's **full body** into the model's context inside a `<customInstructions>` block —
   automatically, with no tool call.
2. They apply to **any agent that runs with a workspace** (Ask, Plan, Coder and workspace-
   defined agents), not just skill-enabled ones.
3. In the **Expand harness** anatomy view, a green **Custom Instructions** box lists the
   discovered files (set by the workspace). You can also see the injected `<customInstructions>`
   text by expanding the **llm-request** step.
