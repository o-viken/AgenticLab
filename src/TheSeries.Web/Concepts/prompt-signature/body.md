## What is a prompt signature?

Every time an agent calls the model it sends a whole **request** — not just the latest user
message, but the system prompt, the tool catalogue, the conversation so far and any tool
results. The **prompt signature** is a breakdown of that request by **category**, so you can
see *what* fills the model's context and *how big* each part is.

## The categories

A request is assembled from a few kinds of content:

- **System** — the system/harness prompt and any always-on instructions.
- **User** — the messages the person sent.
- **Assistant** — the model's own earlier replies and tool calls.
- **Tool result** — the data tools returned and fed back into the context.
- **Tools catalogue** — the schemas of the tools the model is allowed to call.

Measuring each category (here, in characters) shows where the budget goes. A long system
prompt or a large tool catalogue can dominate a request before the user has said much at all.

## Why compare requests

Across a multi-step run the agent calls the model repeatedly, and most of each request is the
**same** as the last one — the system prompt and tool catalogue don't change, and earlier
turns are re-sent verbatim. Providers reward that stability with **prompt caching**: the
unchanged **prefix** of a request can be served from cache, which is cheaper and faster.

Comparing the current request against the previous one — and measuring how much of the prefix
is byte-identical — gives a **stability / match** score. A high match means the prompt is
cache-friendly; a low match means something near the front of the prompt changed and broke
the cache.

## In this application (the-series)

Turning on **Prompt signature** (an Expert-perspective diagram toggle) shows a panel under the
flow diagram. It reads the **real captured requests** the harness sent to the model and draws a
stacked bar for the **Current** request — one segment per category, sized by its share of
characters — plus a **Previous** bar once a second request has been sent. A **Match** bar shows
the share of the current request that is identical to the previous one (its reused prefix). The
five category colours are fixed (they encode message roles, not the vendor brand), so they stay
the same as you switch vendor themes.
