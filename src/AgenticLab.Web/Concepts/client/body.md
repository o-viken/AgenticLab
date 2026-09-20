## What is the client?

The **client** is the application surface the user actually interacts with. It takes the
user's message, sends it to the agent service, and renders whatever comes back. It is the
plumbing around the conversation — not part of the agent's reasoning.

## Where it sits

In the [agent harness model](https://code.visualstudio.com/blogs/2026/05/15/agent-harnesses-github-copilot-vscode)
the **user and the client sit outside the agent**. The agent is *model + harness*; the
client simply delivers the user's prompt to it and displays the result. Swapping the
client (web app, console, IDE) doesn't change how the agent thinks.

## In this application (Agentic Lab)

Here the client is the **Blazor web app**: it posts your message to the AI service and
streams the reply back as the flow animates. A console client or an IDE could use the same
agent service. The client stays outside the agent host and is not an anatomy layer; the
**Agent host** node represents the AI service.
