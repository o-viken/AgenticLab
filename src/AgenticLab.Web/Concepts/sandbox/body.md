## What is a sandbox?

A **sandbox** is an isolated environment that **contains** what an agent can reach. Inside
it the agent can read, write and run things freely; outside it, it can't reach anything.
The point is to make the **blast radius** of a mistake (or a malicious instruction)
predictable: if the worst happens, it happens inside the box.

Sandboxing comes in degrees of strength:

- **Path/scope confinement** — restrict file and shell access to one folder. Simple, but
  relies on the tools enforcing it correctly.
- **OS-level isolation** — run the agent under a restricted user with limited permissions
  and no access to secrets.
- **Containers / VMs** — give the agent a disposable, isolated filesystem and process
  space (and optionally no network), then throw it away when done. The strongest common
  option for high-capability agents.

A sandbox **limits reach**; **guardrails** limit behaviour within that reach. Strong setups
use both.

## In this application (Agentic Lab)

This sample uses the lightest form of sandboxing: **workspace confinement**. The `Coder`,
`Ask` and `Plan` agents resolve every file and command path against a single workspace
root and reject anything that tries to escape it, so their tools can't read or change
files outside the folder you point them at. It does **not** use OS-level isolation or
containers — the commands `Coder` runs execute as the service's own process — which is
exactly why the **command allowlist** and the **per-run tool toggles** matter here.
