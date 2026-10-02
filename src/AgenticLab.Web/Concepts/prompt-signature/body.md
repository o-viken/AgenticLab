## What is a prompt signature?

Every time an agent calls the model it sends a whole **request** — not just the latest user
message, but the system prompt, the tool catalogue, the conversation so far and any tool
results. The **prompt signature** is a breakdown of that request by **category**, so you can
see *what* fills the model's context and *how big* each part is.

## The categories

A request is assembled from a few kinds of content:

- **System** — the system/harness prompt and any always-on instructions.
- **User** — the messages the person sent.
- **Assistant** — the model's own earlier replies and tool calls.
- **Tool result** — the data tools returned and fed back into the context.
- **Tools catalogue** — the schemas of the tools the model is allowed to call.

Measuring each category shows where the budget goes. A long system
prompt or a large tool catalogue can dominate a request before the user has said much at all.

## Why compare requests

Across a multi-step run the agent calls the model repeatedly, and most of each request is the
**same** as the last one — the system prompt and tool catalogue don't change, and earlier
turns are re-sent verbatim. Providers reward that stability with **prompt caching**: the
unchanged **prefix** of a request can be served from cache, which is cheaper and faster.

Comparing the current request against the previous one — and measuring how much of the prefix
is byte-identical — gives a **stability / match** score. A high match means the prompt is
cache-friendly; a low match means something near the front of the prompt changed and broke
the prefix. It does not establish a cache hit: providers apply their own eligibility rules and
tokenization. Only provider-reported cached-input counts describe actual cache usage.

## In this application (Agentic Lab)

The panel reads real captured requests and compares the last visible model request of adjacent
conversation exchanges, not adjacent tool-loop calls.

**Characters** includes captured replies and excludes the tool catalogue, matching the Context count.
Comparison shows the prefix-match score; Delta shows a size-based reused/added split. Neither is
actual cache usage.

**Tokens** estimates input only, including tool definitions and earlier assistant messages but not
the newly generated response. The explicit `o200k_base` or `cl100k_base` reference encoding runs
locally without a model call. It is not automatically matched to an Azure deployment or another
provider's tokenizer. Provider framing and hidden transformations are omitted, so the estimate need
not equal actual usage. Unknown captured content has no estimate rather than a guessed count.

Enable **Settings → Display → Show token usage summaries** to show actual input and cached input
alongside each bar, conversation footers and Execution's **Token usage** block. This saved preference
defaults off; it does not hide estimates, Delta changes or other Execution details, or stop capture.
The reported counts come from that request's matching response. They
are per-call counts; the conversation footer sums the exchange. Cached input is already included in
input. Counts remain pending until the response is captured and are not reported when unavailable.
Token Delta shows only the previous and current exchange, with the user prompt above each bar on a
shared scale. The muted baseline represents previous size, green shows an increase, and an outlined
tail shows a decrease outside the current size. The signed change compares estimates, not content
reuse or caching. Missing estimates have no calculated change. Replay bounds both the estimates and
reported counts to the selected stage.
