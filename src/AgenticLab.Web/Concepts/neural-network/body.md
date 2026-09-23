## The forward pass

A language model is a large **neural network**: layers of simple units ("neurons") connected by weighted
links. Once a token has been turned into an **embedding** (a vector of numbers), the network does its work
by passing that vector **forward** through the layers. Each layer multiplies its input by a matrix of
**learned weights** and applies a non-linear function, transforming the vector step by step.

The final layer produces a **logit** — a raw score — for every token in the vocabulary. A **softmax** then
turns those scores into probabilities that sum to 1, and one token is sampled as the next output. Repeat
this for each new token and you get generated text.

## Picking a token: the whole vocabulary, temperature and top_p

The softmax produces a probability for **every** token the model knows — tens of thousands of them — not
just the handful you usually see listed. Most get a near-zero share; only a few are real contenders. Two
simple knobs shape how the next token is chosen from that full distribution:

- **Temperature** — how *adventurous* the model is. Low temperature **sharpens** the distribution (the top
  token dominates, so output is focused and repetitive); high temperature **flattens** it (more of the
  probability is spread around, so output is more varied and surprising).
- **top_p** (nucleus sampling) — *how many* candidates are even considered. The model keeps the smallest set
  of top tokens whose probabilities add up to `p` (e.g. 0.9 = the top 90% of the mass) and samples only from
  those, discarding the long tail of unlikely tokens.

Together they trade off **focused vs. creative** generation.

## Where the "knowledge" lives

The network learns nothing at run time. All of its "knowledge" lives in the **weights**, fixed during
training; running the model ("inference") just pushes vectors through those frozen weights. The model is
not looking anything up — it is multiplying a vector through layers of weights to score every possible next
token.

Modern LLMs are **transformers** — a particular network design built around an **attention** mechanism
that lets each token's representation draw on the others — but the core idea is the same: vectors in,
weighted layers, a probability distribution out.

## Why it helps to see it

Drawing the layers and connections makes the **forward pass** concrete: you can see the embedding enter on
one side, flow through the hidden layers, and emerge as scores that pick the next token — and that the input
to it all is just the embedding's numbers.

## In this application (Agentic Lab)

Turning on **Neural network** (a display option) shows a panel under the flow diagram
with a **symbolic** network — input (embedding) → two hidden layers → logits → softmax → next token — drawn
as nodes and fully-connected edges with an animated left→right signal sweep, ending in the run's predicted
token. When you **pin** a token (in the **Embeddings** or **Inference** panels) the network's input layer
lights up: each input node is sized and tinted by that token's vector components, so the embedding's numbers
visibly *become* the network input.

The panel also **decodes the answer on a loop**, token by token (play/pause and step ‹ › by hand), showing
each step's **softmax distribution** over the top candidates and which one is **picked**. A caption makes
explicit that the distribution really spans the **whole vocabulary** (only the top few are listed, with a
"+ N more tokens" row for the leftover mass), and two simulated **temperature** and **top_p** chips stand in
for the sampling settings described above.

Everything here is **fabricated for teaching**. The configured model API exposes no real network
weights, so the shape, edges, sweep, distribution and sampling values are invented. It conveys the *shape*
of a forward pass, not the model's real internals.
