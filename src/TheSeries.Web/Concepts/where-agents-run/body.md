## Where does an agent run?

An agent is not an abstract thing in the cloud — its tool calls execute on a **real
machine**, and it can do whatever that machine lets it. The same agent is far more
powerful (and far riskier) depending on **where it runs**:

- **Your own device** — a coding agent on your laptop can read and write your files, run
  shell commands, install packages, and reach the network *as you*. Maximum capability,
  maximum risk.
- **A server you control** — a backend process acts within that server's permissions and
  network. It can't touch your laptop, but it may reach internal systems.
- **A cloud service** — a hosted model reasons about your request but runs outside your
  environment. It only "acts" through the tools the host gives it.

## Why it matters

The model decides *what* to do; the **environment** decides *what is possible*. An agent
runs **on your behalf, in an environment, with that environment's access** — so before you
let one run, it's worth knowing three things:

1. **Where** does it execute (your machine, a server, the cloud)?
2. **What** can it reach there (files, shell, network, secrets)?
3. **What limits** are in place (a sandbox, an allowlist, read-only mode, approvals)?

The bigger the reach, the more the guardrails matter.

## In this application (the-series)

Turn on **Environment & risk** above the diagram. Each node gets a "where it runs" badge:
the **User** is your browser, the **LLM** is a cloud service (Azure OpenAI), and the merged
**Harness** node shows whether the selected agent runs *on your machine* (workspace agents
like `Coder`, with file and shell access) or *as a server process with no local access*
(everyone else). A dashed **Your environment** boundary wraps the User and the Harness —
the parts running on your behalf — leaving the cloud model outside it.
