## How do you protect against an agent?

An agent's power — taking **actions**, not just producing text — is also its danger. The
model can be wrong, do more than you intended, or be steered by a malicious instruction
hidden in the data it reads (*prompt injection*). You don't remove the risk by avoiding
agents; you **bound** it with layers, so that no single failure turns into real damage.

The guiding idea is **defence in depth**: assume any one layer can fail, and make sure
another still contains the blast radius.

## Techniques that work together

- **Least privilege.** Give the agent the smallest set of tools the task needs, and offer
  a **read-only** mode when no changes are required. A tool the agent doesn't have can't be
  misused.
- **Sandboxing & scoped access.** Run the agent in a confined **environment** — one folder,
  a restricted account, or a disposable container — and reject attempts to escape it
  (`..`, absolute paths, unexpected hosts).
- **Allowlists, not blocklists.** Permit a known-good set of commands, executables or
  network destinations, and reject everything else — including attempts to chain or
  redirect (`&`, `|`, `;`, `>`).
- **Human in the loop.** Require approval (or a clarifying question) before anything
  significant or hard to undo: deleting data, sending messages, spending money, pushing
  code.
- **Hooks / policy gates.** Run your own code *before* (or after) each tool call to inspect
  the arguments and **allow, deny or modify** the action — a programmable checkpoint that
  enforces policy the model can't talk its way around (e.g. block writes outside a folder,
  refuse risky commands, redact secrets).
- **Resource limits.** Time out long-running commands, cap output size, and rate-limit
  tool calls so a runaway loop can't do unbounded work.
- **Prompt-injection defences.** Treat tool results and fetched content as **untrusted
  data, not instructions**; separate the system prompt from external text, and don't let
  retrieved content silently expand the agent's permissions.
- **Secret hygiene.** Keep API keys and credentials out of the agent's reach where you can;
  scope the ones it must use, and never log them.
- **Content exclusion.** Use ignore rules to keep sensitive files (secrets, customer data,
  key material) out of what the agent can read or send to the model in the first place.
  Most tools have an equivalent: GitHub Copilot's
  [content exclusion](https://docs.github.com/en/copilot/managing-copilot/configuring-and-auditing-content-exclusion/excluding-content-from-github-copilot)
  settings (and `.copilotignore`), and Claude Code's
  [`permissions.deny`](https://code.claude.com/docs/en/settings) rules (e.g.
  `Read(./secrets/**)`); many also honour your existing `.gitignore`.
- **Monitoring & audit.** Log every tool call and its arguments so a run is reviewable
  after the fact, and you can detect abuse or debug a bad outcome.

No single technique is enough. A strong setup combines a small **sandbox** (limiting
*reach*) with layered **guardrails** (limiting *behaviour*) and a human gate on the
irreversible actions.

## Where to learn more

The resource links on this card point to the current, authoritative guidance:

- **OWASP Top 10 for LLM Applications** and **OWASP Agentic AI: Threats & Mitigations** —
  the common risks (including prompt injection and *excessive agency*) and concrete
  mitigations.
- **NIST AI Risk Management Framework** — a structured way to identify and manage AI risk.
- **Google SAIF** — a practical secure-AI framework.
- **MITRE ATLAS** — a knowledge base of real adversarial techniques against AI systems.

## In this application (the-series)

This sample shows several of these techniques in miniature, visible with **Environment &
risk** turned on. The workspace agents are **confined** to a single folder (a lightweight
**sandbox**); `Coder`'s commands are checked against an **allowlist** with shell-operator
chaining rejected and a 60-second timeout; `Ask` and `Plan` use a **read-only** tool subset
(least privilege); `Plan` can **ask you before assuming** (human in the loop); and for every
agent, individual **tools can be toggled off per run**. The merged node's **Risk** meter and
**Guardrails** box make the agent's reach and its protections explicit *before* you let it
run. See the related **guardrails**, **sandbox**, **environment** and **agent risk**
concepts for each piece in detail.
