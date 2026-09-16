## What is an agentic coding harness?

It's an **agent** (model + harness) specialised for software work. On top of the usual
loop, the harness gives the model coding-specific tools and context:

- **Read/write files** in a workspace
- **Run commands** in a terminal (build, test, run)
- **Search** the codebase and pull in repo context
- Often **skills / instructions** and review/permission gates

The model proposes changes; the harness applies them and feeds back compiler errors, test
output and command results — so the agent can iterate toward working code. The **Coder**
agent in this app is a miniature version of exactly this idea.

## The landscape (2026)

These products differ in where they run (editor, terminal, cloud) and which models they
use, but they share the model + harness shape:

- **GitHub Copilot** — agent mode inside VS Code and other editors.
- **Claude Code** — Anthropic's terminal-based coding agent.
- **Cursor** — an AI-first code editor.
- **ChatGPT** — general assistant with coding and agentic modes.

Click any of these in the **Vendor** switcher's info badges to learn more. Vendor logos
distinguish the harnesses; all share the same workbench colors.
