## Gemini

**Gemini** is Google's assistant, built on the **Gemini** family of natively multimodal
models. It started as conversation and now spans a broad set of capabilities:

- **Chat** — answer questions, write and explain text or code.
- **Multimodal** — reason over text, images, audio, video and code in one prompt.
- **Tools** — browse the web, run code, and call connected tools/APIs.
- **Agentic tasks** — break a goal into steps and carry them out across several turns.

The same models are available to developers through the **Gemini API** in **Google AI
Studio** and **Vertex AI**, where you supply your own tools and harness — exactly the
request → tool-call → result → response loop drawn in the diagram.

## In this application (the-series)

Selecting the **Gemini** logo chooses its representative harness while keeping the shared
workbench colors. It labels the
LLM node *Gemini 2.5 Pro (Google)* in the non-Expert perspectives to mimic that the product
runs on its own model. The real backend is always **Azure OpenAI** — no Google model is
called. The vendor offers a single **chat** mode backed by the `ChatAgent` agent (which answers
from its own knowledge, with no tools).
