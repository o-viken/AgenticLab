## What are settings?

**Settings** are the run-time knobs that shape *how* a turn executes — separate from the
prompt content. They don't tell the model *what* to say; they decide which model answers
and how the run unfolds.

## Common knobs

- **Model** — which backing model (and deployment) handles the request.
- **Stepping / pacing** — whether the run advances automatically or one step at a time.
- (In larger systems: temperature, max tokens, tool-choice policy, and similar.)

Changing settings can change behaviour without touching a single word of the prompt.

## In this application (Agentic Lab)

The **Settings** box is an **agent** layer (yellow). It surfaces two things:

- **Declared model**: the configured model identifier or Azure deployment, which can differ from
  the executing model when force-default is enabled. The provider is selected at server startup.
- **stepping: Auto / Manual** — *Auto* paces the animation with a server-side delay you
  can adjust live; *Manual* blocks each step until you click **Next**.

Stepping is enforced on the *server* so the animation stays in sync with the real agent
execution and its telemetry, not just a client-side replay.
