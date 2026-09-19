## What is a token?

A language model does not read text as letters or whole words. Before anything else, the text
is split into **tokens** — short, frequent chunks drawn from a fixed vocabulary. A token is
often a whole common word, but a longer or rarer word is broken into several pieces (for
example `Lovelace` might become `Love` + `lace`), and punctuation and spaces are tokens too. A
rough rule of thumb for English is that **one token ≈ 4 characters**, or about ¾ of a word.

Tokens matter because everything a model does is counted in them: context windows, pricing and
rate limits are all measured in tokens, not characters or words.

## Inference: generating one token at a time

When the model answers, it does not write the whole reply at once. It works
**autoregressively** — it predicts the **single most likely next token**, appends it to what it
has produced so far, and then repeats, feeding its own growing output back in as it goes. That
is why streamed answers appear to type themselves out piece by piece.

At each step the model actually produces a **probability distribution** over its entire
vocabulary — every possible next token gets a score. A **sampling** strategy (greedy, top-k,
nucleus/top-p, with a temperature setting) then picks one token from that distribution. Higher
temperature spreads the probability out and makes the choice more varied; lower temperature
concentrates it and makes the model more deterministic.

## Why it helps to see it

Seeing the prompt as tokens makes context cost concrete — a long document is *thousands* of
tokens before the model has answered. Seeing generation token by token, with the alternatives
each step chose from, demystifies what "the model is thinking" really means: it is repeatedly
predicting a next token from a ranked list of candidates.

## In this application (Agentic Lab)

Turning on **Inference** (a display option) shows a panel under the flow
diagram that **simulates** what happens inside the LLM node. Step ① splits the latest user
message into word-piece chips and shows a whole-prompt token estimate (≈ characters / 4). Step
② replays the agent's final answer as **generated** tokens, revealing them left-to-right; hover
a generated token to see a list of candidate next-tokens with probability bars.

This view is **fabricated for teaching** — the backend is Azure OpenAI and never exposes its
real tokenizer or token probabilities, so the splitting is a crude heuristic and the
probabilities are invented (though deterministic, so they don't flicker). It conveys the
*shape* of tokenization and autoregressive generation, not the model's actual internals.
