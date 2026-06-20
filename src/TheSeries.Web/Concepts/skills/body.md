## What is a skill?

A **skill** is a small, named playbook that lives in the workspace and is loaded **on
demand**. Instead of stuffing every instruction into the system prompt, the harness shows
the model only a short *catalogue* — each skill's name and one-line description — and lets
the model pull in the full instructions for a skill **only when a task needs it**. This is
called **progressive disclosure**: keep the default context small, expand it just-in-time.

## How it works in this app

A skill is a folder under the workspace's `skills/` directory with a `SKILL.md` file:

```
skills/get-date/SKILL.md
---
name: get-date
description: Get the current date and time on a Windows machine using the terminal.
---
(body with the steps the agent should follow)
```

1. Before a run, the harness scans `skills/*/SKILL.md` and injects the **catalogue**
   (names + descriptions) into the model's context.
2. When a listed skill fits the task, the model calls the `ReadSkill` tool to load its
   **full body**.
3. In the diagram's **Skills** box each skill shows as a chip; it **lights up** the moment
   the model loads it.

Only the workspace-scoped **Coder** agent supports skills.
