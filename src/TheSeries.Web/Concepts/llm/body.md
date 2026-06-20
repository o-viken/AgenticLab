## What is an LLM?

A **Large Language Model** is a neural network trained on huge amounts of text to predict
the next token (roughly, the next word). From that simple objective it learns grammar,
facts, reasoning patterns and even how to follow instructions.

Crucially, an LLM only **produces text**. It has no memory between calls and can't touch
the outside world by itself — everything it "knows" was baked in at training time, and it
can be confidently wrong. That's why we wrap it in a **harness** that supplies fresh
context and **tools**, and grounds answers in real results.

## How it fits the diagram

Every arrow to the **LLM** node is one round-trip: the harness sends the conversation plus
the tool definitions, and the model replies with either a final answer or a **tool call**.
The loop badge counts how many of these round-trips a single question takes.

> The node is labelled "LLM (MCP)" as a concept. In this app the backend is **Azure
> OpenAI** with local function tools — there is no separate MCP server.

## Want the intuition?

3Blue1Brown's series visually explains how neural networks and transformers actually work
— a great way to build a mental model of what's happening inside this node.
