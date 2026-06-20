# Authoring concepts

Each concept is a folder `Concepts/<id>/` with two files:

- `meta.json` — `{ "title", "summary", "category", "links": [{ "label", "url" }] }`
- `body.md` — markdown body rendered into the concept drawer.

`ConceptCatalog` discovers every sub-folder with a valid `meta.json` at startup (the `id`
is the folder name, matched case-insensitively). This `AUTHORING.md` lives at the root of
`Concepts/`, not in a sub-folder, so it is **not** treated as a concept.

## Golden rule: keep concepts general

A concept explains an **idea**, not this repository. Write it so it would make sense in any
agent app.

- The `meta.json` **summary** and the general sections of `body.md` must be
  **product-agnostic** — describe the concept itself (what it is, why it matters, how it
  generally works). Do **not** mention agent names, the diagram/UI, or this app's wiring
  there.
- Put **every** the-series-specific detail under a single trailing section titled exactly:

  ```markdown
  ## In this application (the-series)
  ```

  This is where you reference concrete agents (e.g. `Coder`, `WikiAssistant`), the flow
  diagram/anatomy boxes, toggles, and how the concept is implemented here.

## Exception: product concepts

Concepts with `"category": "product"` (e.g. `github-copilot`, `claude-code`, `chatgpt`)
describe a specific product by nature, so the "keep it general" rule does not apply — but
they still should not describe the-series internals.

## Checklist for a new concept

1. Create `Concepts/<id>/meta.json` and `Concepts/<id>/body.md`.
2. General summary + general body sections (no the-series specifics).
3. One trailing `## In this application (the-series)` section for app specifics (omit it
   for product concepts).
4. Optionally wire an ⓘ button to `OpenConcept("<id>")` in `Components/Pages/Flow.razor`.
