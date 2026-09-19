## What is a tool?

A **tool** is a capability the model can request and the **agent host** can execute:
look something up, run a calculation, read a file or execute a command. The model
chooses a request; the host controls whether and how the action happens.

## How it works

1. The agent host describes each available tool to the model: its **name**, a **description**, and
   the **parameters** it accepts (a JSON schema).
2. When the model decides a tool would help, it emits a **tool call** with arguments
   instead of a normal answer.
3. The host applies execution controls, runs the function only if permitted, captures the
   **result** and feeds it back to the model. A rejected request may return an error or stop the run.
4. The model reasons over the updated context and selects another request or a final answer.

This request → tool-call → result → response loop is exactly what the diagram animates.

## In this application (Agentic Lab)

Each agent is given a fixed subset of tools, for example:

- `SearchWiki` / `GetWikiPage` — research grounded in Wikipedia
- `Calculate` — arithmetic the model shouldn't do in its head
- `ReadFile` / `WriteFile` / `RunCommand` — workspace actions for the Coder agent

You can **toggle individual tools off** for a run to see how the agent copes with fewer
capabilities. Tools are deliberately bounded — the agent can only do what its tools allow.
