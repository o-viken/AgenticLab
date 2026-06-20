## What is MCP?

The **Model Context Protocol** is an open standard that defines *how* an agent connects to
external tools and data sources. Rather than every app inventing its own way to expose a
database, an API or a file system to a model, MCP gives them a **common interface** — often
described as "a USB-C port for AI applications".

An **MCP server** publishes a set of tools (and resources/prompts); an **MCP client**
inside the harness discovers them and makes them callable by the model — the same
tool-call loop you see in the diagram, but the tools live in a separate process behind a
standard protocol.

## In this app

The LLM node is labelled "LLM (MCP)" to show *where* an MCP server would plug in, but this
sample keeps things simple: the tools are plain **local functions** wired straight into the
agent, and the model is **Azure OpenAI**. There is **no real MCP server** here — MCP is
shown as the concept you'd reach for to share these tools across many agents or apps.

See the introduction linked below for the full picture.
