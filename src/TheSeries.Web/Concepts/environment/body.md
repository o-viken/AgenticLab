## What is the environment?

An agent never acts in a vacuum. Every tool call runs inside an **environment**: the
process and identity the agent runs as, plus **everything that identity can reach**. The
model decides *what* to attempt — the environment decides *what is actually possible*.

A useful way to see it: the model is the brain, the tools are the hands, and the
environment is the **room the hands can reach into**. Two agents with identical models and
tools behave very differently depending on the room they're standing in.

## What an environment is made of

When you size up an environment, you're really asking what it grants:

- **Identity** — *whose* permissions does the agent borrow? Your user account, a service
  principal, an anonymous process? It acts **as** that identity.
- **Filesystem** — which files and folders can it read and write? One project folder, your
  whole home directory, a server's disk?
- **Shell & processes** — can it run commands, install software, or start other programs?
- **Network** — can it reach the internet, or internal systems behind a firewall?
- **Secrets** — are API keys, tokens, or credentials sitting in environment variables,
  config files, or a logged-in CLI it can use?

The capability of an agent is the **union** of these, not just its list of tools.

## The trust boundary

Because the environment defines reach, it's the natural **unit of trust**. Draw a boundary
around the parts that run on your behalf with your access, and treat everything the agent
does inside it as something *you* effectively did. Anything that can be reached from inside
the boundary is in scope if the model is wrong or is steered by a malicious instruction
hidden in the data it reads (*prompt injection*).

The practical takeaways:

1. **Right-size the environment.** Give the agent the smallest environment the task needs —
   a single project folder beats your whole machine.
2. **Mind the identity.** An agent running *as you* can do anything you can; a scoped,
   low-privilege identity limits the damage.
3. **Know what's reachable.** Secrets and network access inside the boundary are part of
   the agent's power whether you intended them to be or not.

A **sandbox** shrinks the environment; **guardrails** limit behaviour within it. Both are
ways of shaping the environment so the trust boundary stays small.

## In this application (the-series)

Turn on **Environment & risk** above the diagram. A dashed **🔒 Your environment** boundary
is drawn around the parts that run on your behalf — your browser and the merged
Harness/Application node — while the cloud model is left outside it. Each node also gets a
"where it runs" badge, and the merged node shows whether the selected agent runs *on your
machine with file + shell access* (workspace agents like `Coder`) or *as a server process
with no local access* (everyone else). That badge is the environment in miniature: it's the
difference between an agent that can only reason and one that can reach into your files and
shell.
