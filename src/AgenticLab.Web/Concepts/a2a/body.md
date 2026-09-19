## What is A2A?

**Agent2Agent (A2A)** is an open protocol that lets agents talk to *each other*. Where
[MCP](#) standardizes how a model calls **tools**, A2A standardizes how one **agent** calls
another **agent** — even when the two are built by different teams, on different frameworks,
and run as separate services.

An agent that speaks A2A publishes an **agent card**: a small document describing who it is,
what it can do, and where to reach it. Another agent (or a client) **discovers** that card,
then sends it a **message** and gets a reply back — optionally as a long-running **task** or
a **streamed** response. The calling agent treats the remote agent much like a tool: it hands
off a sub-question, waits for the answer, and folds it into its own reasoning.

## Why it matters

Real systems are increasingly built from **many specialized agents** rather than one agent
that does everything: a coordinator delegates research to a research agent, booking to a
travel agent, analysis to a data agent. A2A gives those agents a **common language** so they
can be composed across organizational and technical boundaries — the same interoperability
win MCP brought to tools, applied one level up at the agent boundary.

- **Discovery** — agent cards advertise capabilities and endpoints.
- **Delegation** — an agent forwards a task to a better-suited agent and relays the result.
- **Interoperability** — agents from different stacks cooperate over one standard.

## "Agent" vs "sub-agent"

There is **no protocol difference** between calling "an agent" and calling "a sub-agent". A2A is a
symmetric, peer-to-peer protocol — every participant is simply *an agent* reachable at an endpoint.
"Sub-agent" only names the **relationship**: from a coordinating agent's point of view, the agent it
delegates to is its sub-agent. That same agent could itself delegate onward, becoming the coordinator
for yet another agent. So "sub-agent" is about orchestration topology, not a special kind of call.

## In this application (Agentic Lab)

This sample includes a **real** A2A integration (like its real MCP one). A separate service hosts one or
more **persona-only specialist agents** (a research agent and a poet, by default) and exposes each over
A2A. The **Orchestrator** agent answers arithmetic itself with its calculator tool, but for anything a
specialist handles better it calls a single generic `DelegateToAgent(agentName, question)` tool that
sends the question to the named agent over the A2A protocol and relays the answer.

The roster is **discovered**, not hard-coded: the Orchestrator asks the A2A server which agents it hosts
and builds a client for each, so adding a specialist to the server's configuration makes it available for
delegation with no code change. With **A2A agents** enabled in Flow, selecting the Orchestrator shows each remote agent as
its own **harness + model** composition. Research and Poet share a separate A2A service process; their
models are cloud-hosted. These repeated model nodes describe agent roles, not distinct deployments.
The harness catalogue lists available remote agents, while request/result arrows highlight the target.

Only the delegation request and returned result are captured, not the remote agent's internal model
activity. A requested call is not proof of a network send, and a returned result may contain an error.
Internal model links stay static. Execution playback keeps the exchange's original roster, pairs
results with their own call IDs, and never shows a later reply at an earlier request stage.
