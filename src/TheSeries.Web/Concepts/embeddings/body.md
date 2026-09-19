## From tokens to vectors

A neural network cannot do arithmetic on words, so after text is split into **tokens**, each token is
turned into a list of numbers called an **embedding** — a **vector** in a high-dimensional space (real
models use hundreds or thousands of dimensions). The vector *is* how the model represents that token's
meaning.

Crucially, embeddings are arranged so that **similar meanings sit close together**. Words used in
similar ways end up near each other in the space, and the directions between them can even capture
relationships. Measuring the **distance** (or cosine similarity) between two vectors is how a model — or
a search system — judges how related two pieces of text are. This is exactly what powers semantic search
and retrieval-augmented generation (RAG): text is embedded once, and the closest vectors are the most
relevant matches.

## Reading a vector

Each number in the vector is one **dimension** of meaning. The numbers themselves are not human-readable
labels — no single dimension means "is a colour" — but together they place the token at a precise point in
the space. Projecting that high-dimensional point down to two dimensions gives a **map** you can actually
look at, where nearby points are tokens the model treats as similar.

Those same numbers are what get fed into the network: the embedding is literally the network's **input**,
so the vector is the bridge from language to computation.

## Why it helps to see it

Seeing tokens as vectors makes the leap from language to numbers concrete, and the 2-D map makes
"distance = similarity" tangible.

## In this application (the-series)

Turning on **Embeddings** (a display option) shows a panel under the flow diagram with
two steps. Step ① turns each prompt token into a small fake embedding vector, drawn as a red↔blue heatmap
strip; clicking a token row (or a point on the map) **pins** it and shows its actual numbers — each of the
vector's components plus its 2-D coordinates. Step ② projects those vectors down to a 2-D **meaning map**
where identical tokens overlap. The companion **Neural network** panel shows the symbolic forward pass that
consumes these vectors, and a pinned token lights up that network's input layer.

Everything here is **fabricated for teaching** — the backend is Azure OpenAI and exposes no real
embeddings, so the vectors and positions are invented (though deterministic, so they don't flicker). It
conveys the *shape* of embeddings, not the model's real internals.
