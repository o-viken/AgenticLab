## What is reasoning?

**Reasoning** is the model working through a problem step by step before committing to an
answer, instead of blurting out the first thing it predicts. Because a language model only
ever predicts the next token, giving it room to "think out loud" lets each intermediate
step condition the next — so multi-step problems (maths, logic, planning, multi-tool work)
come out far more reliably than a single-shot answer.

## Chain-of-thought

The classic technique is **chain-of-thought**: the model generates a sequence of
intermediate steps ("first…, then…, therefore…") that lead to the final result. Spreading
the work across many tokens gives the model more computation to reach an answer, and the
explicit steps tend to be more accurate than jumping straight to a conclusion. It can be
prompted ("think step by step") or trained into the model.

## Reasoning tokens vs. answer tokens

Newer **reasoning models** do this thinking as a distinct phase. They first produce
**reasoning tokens** — a private scratchpad of intermediate thought — and only then the
**answer tokens** the user sees. The thinking is usually hidden or summarised, but it is
still generated, counted and billed like any other output. Some models expose a
**reasoning effort** dial that trades more thinking tokens for higher quality.

## Trade-offs

Reasoning is not free. More thinking means **more tokens, higher cost and more latency**,
and a longer chain can still reach a wrong conclusion — confidently. The skill is matching
the effort to the task: let the model think hard on a genuinely hard problem, and answer
directly on a simple one. Grounding the reasoning in **tool results** (rather than the
model's memory) keeps the chain honest.

## In this application (the-series)

Reasoning happens **inside the Model node** — each request/response exchange is one round-trip where the
model may reason before replying with a final answer or a **tool call**. When a question
needs several steps, the model requests a tool, the agent host checks and executes it,
and the model reasons over the returned result. The loop badge counts those round-trips.
Intermediate reasoning tokens aren't surfaced as their own flow step: you see the model's
requests, the host's tool results and the final answer, not a transcript of internal reasoning.
