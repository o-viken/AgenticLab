# Vision

**Make how agentic systems work understandable to humans.**

Agentic AI — models that reason, call tools, and act in a loop — is becoming a core
part of how software is built and used. Yet for most people the inner workings stay
hidden behind a chat box: a question goes in, an answer comes out, and everything in
between is invisible. That opacity makes agents hard to trust, hard to reason about,
and hard to learn from.

**Agentic Lab exists to open that black box.** It is a tool/app that shows, in a clear
and human-understandable way, *how an agentic system actually works* — turning the
abstract "model + harness + tools" loop into something you can watch, explore, and
understand step by step.

## What we want people to walk away understanding

- **The agent loop** — the think → act → observe cycle, where the model decides to
  call a tool, sees the result, and continues until it can answer.
- **The harness** — that an agent is *the model plus a harness*: the harness assembles
  context, exposes a bounded set of tools, runs the loop, executes the model's tool
  calls, and relays results. The user sits outside the agent.
- **Context** — what the model actually sees: the system prompt, the persona, the tool
  catalogue, the conversation history, and how that context grows turn by turn.
- **Tools, skills, and instructions** — how an agent's abilities are granted, scoped,
  and disclosed progressively rather than all at once.
- **Where it runs and what it can touch** — the boundaries, environment, risk levels,
  and guardrails that determine how safe an agent is.
- **How real products frame agents** — that different vendors wrap the same underlying
  model in different harnesses, personas, and modes.

## Principles

- **Show, don't tell.** Visualize real executions — actual LLM requests, tool calls,
  and responses — rather than a scripted replay.
- **Faithful, not hand-wavy.** What the diagram shows reflects what the system really
  did, down to the captured prompts and payloads.
- **Layered understanding.** Meet people where they are: a non-technical overview, a
  technical view, and an expert deep-dive into the harness anatomy.
- **Learn in place.** Concepts are explained right next to the thing they describe, so
  curiosity can be satisfied without leaving the flow.
- **Honest about boundaries.** Make trust boundaries, risk, and guardrails explicit so
  people can reason about what an agent can and cannot do.

## Who it's for

Anyone trying to build a mental model of agentic AI — developers adopting agents,
teams evaluating them, learners and educators, and decision-makers who need to
understand the capabilities and risks without reading the source.

## How people reach it

- **Hosted service.** Agentic Lab runs as a service on our website, so anyone can open
  it in a browser and start exploring immediately — no install, no setup, nothing to
  configure.
- **Run it locally (BYOK).** People can also download a Docker image and run the whole
  system on their own machine, bringing their own key (BYOK) for the AI model. This
  keeps their data and credentials under their control, lets them point it at their own
  workspace, and makes the tool usable in environments where the hosted service isn't
  an option.

Both paths run the same agentic system and the same flow visualizer — the only
difference is where it runs and whose key powers the model.

## How this project serves the vision

Agentic Lab is a working agentic system *and* a window into one. Real agents answer real
questions using real tools, while the flow visualizer animates every step of that
execution — the agent loop, the growing context, the harness anatomy, the tools and
skills, and the environment in which it all runs — backed by in-app learning content
that explains each concept. The goal is not just to *be* an agentic system, but to make
agentic systems **legible**.
