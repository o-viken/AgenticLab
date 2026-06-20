## What are guardrails?

**Guardrails** are the mechanisms that keep an agent inside safe bounds, so a wrong or
manipulated model decision can't turn into real damage. They are enforced by the **harness
and the tools**, not by the model — the model can ask for anything, but the guardrails
decide what actually happens. Common ones:

- **Least privilege** — give the agent only the tools the task needs; offer a **read-only**
  mode when no changes are required.
- **Scoped access** — confine file and shell tools to a single root, and reject attempts
  to escape it (`..`, absolute paths).
- **Allowlists** — permit only a known set of commands or executables, and reject anything
  that tries to chain or redirect.
- **Resource limits** — time out long-running commands; cap output size.
- **Human in the loop** — pause for approval (or a clarifying question) before doing
  something significant or irreversible.

Good guardrails are layered: even if one fails, another still contains the blast radius.

## In this application (the-series)

Turn on **Environment & risk** and the merged node lists the **guardrails** in force for
the selected agent. They're not just labels — they're enforced in code: `Coder`'s commands
are checked against an **allowlist**, shell-operator chaining is rejected, commands **time
out after 60s**, and every file path is **confined to the workspace**. `Ask` and `Plan`
use a **read-only** subset of the file tools; `Plan` can **ask you before assuming**. And
for every agent, individual tools can be **toggled off per run**.
