## What is the model's role?

A **Large Language Model** is a neural network trained on huge amounts of text to predict
the next token (roughly, the next word). From that simple objective it learns grammar,
facts, reasoning patterns and even how to follow instructions.

In an agent, the model **reasons, plans and chooses the next step** from the context the
agent host supplies. Its output can be an answer or a structured tool request. That request
does not itself run a function, read a file or change a system: the **agent host** checks
and executes permitted actions.

The model uses both learned patterns and supplied context, including fresh tool results.
Conversation memory is managed outside the model and made available through context;
it is not the model remembering a previous invocation on its own. Reasoning and planning
can still be wrong, so a proposed action must remain subject to execution controls.

## In this application (Agentic Lab)

Each request/response exchange with the **Model** node is one round-trip: the agent host sends the conversation plus
the tool definitions, and the model replies with either a final answer or a **tool call**.
The loop badge counts how many of these round-trips a single question takes.

The model runs through the configured **Azure OpenAI**, **OpenAI** or **Gemini** API.
Local functions, MCP tools and A2A delegation
are capabilities reached through the agent host, not actions the model executes itself.

## Want the intuition?

3Blue1Brown's series visually explains how neural networks and transformers actually work
— a great way to build a mental model of what's happening inside this node.
