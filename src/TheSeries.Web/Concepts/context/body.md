## What is the context?

The **context** is everything the model can see when it produces the next response — its
entire working memory for that turn. The model has no memory between calls of its own, so
the harness re-sends the full context every time.

## What's in it

A turn's context is assembled from several contributors:

- **Application** — the system/harness prompt and plumbing.
- **Agent** — the persona and the tool catalogue.
- **User** — the user's message.
- Plus the **conversation so far**: earlier messages, tool calls and their results.

Each tool call and reply adds more, so the context **grows** as the run proceeds. Because a
model's context window is finite, what goes in (and how much) matters.

## In this application (the-series)

The **Context** visualization grows as the run streams. A gradient bar scales with the
captured data size, and one chip is appended per **content** event — the user message, tool
results, the assistant's replies and the final answer — each colour-coded by who
contributed it and carrying a short preview of the real content. The structural parts
(system prompt, tool catalogue, persona) are omitted from the chips because the coloured
boxes above already show them. When the conversation has earlier turns, they appear first as
**dimmed** chips under an *Earlier in this conversation* divider (one for the user's message
and one for the agent's final answer per past turn), above the current turn's live chips —
reflecting that the harness re-sends the whole conversation each turn. The bar and the
character count include that carried-over history, so you can watch the model's context
accumulate turn by turn across the conversation.

