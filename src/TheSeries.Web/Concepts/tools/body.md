## What is a tool?

A **tool** is a function the model is allowed to call. On its own, a language model
can only produce text. Tools give it a way to *do* things — look something up, run a
calculation, read a file, or execute a command — and then read the result back.

## How it works

1. The harness describes each tool to the model: its **name**, a **description**, and
   the **parameters** it accepts (a JSON schema).
2. When the model decides a tool would help, it emits a **tool call** with arguments
   instead of a normal answer.
3. The harness runs the real function, captures the **result**, and feeds it back to
   the model.
4. The model continues — often calling more tools — until it can answer.

This request → tool-call → result → response loop is exactly what the diagram animates.

## In this app

Each agent is given a fixed subset of tools, for example:

- `SearchWiki` / `GetWikiPage` — research grounded in Wikipedia
- `Calculate` — arithmetic the model shouldn't do in its head
- `ReadFile` / `WriteFile` / `RunCommand` — workspace actions for the Coder agent

You can **toggle individual tools off** for a run to see how the agent copes with fewer
capabilities. Tools are deliberately bounded — the agent can only do what its tools allow.
