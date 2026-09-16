## Why agents are risky

A chatbot only produces **text**. An **agent** produces **actions**: it writes files,
runs commands, sends requests, and changes state in the environment it runs in. That power
is the whole point — and the whole danger. The model can be wrong, be steered by a
malicious instruction hidden in the data it reads (*prompt injection*), or simply do more
than you intended.

The risk of letting an agent run scales with what it can **do**, not what it can **say**:

| Level | What the agent can do | Example |
|-------|-----------------------|---------|
| **None** | Reason and reply from its own knowledge. No tools. | Plain chatbot |
| **Low** | Read-only or computational tools, no side effects. | Search, calculate, read files |
| **Medium** | Confined, reversible changes; no arbitrary commands. | Scoped writes |
| **High** | Write/delete files and run commands on the host. | Coding agent on your machine |

## Managing the risk

You don't avoid the risk by avoiding agents — you **bound** it: give the agent the
smallest set of capabilities the task needs, keep it confined to a known scope, and keep a
human in the loop for the actions that are hard to undo. Knowing an agent's risk level
*before* it runs is the first step.

## In this application (the-series)

With **Environment & risk** on, the merged node shows a **Risk** meter for the selected
agent — `None` for `ChatAgent`, `Low` for read-only or calculator agents like
`WikiAssistant`, `Ask` and `Plan`, and `High` for `Coder`, which can write files and run
allowlisted commands in your workspace. The level comes from the backend (`GET /agents`),
derived from what each agent's tools can actually do.
