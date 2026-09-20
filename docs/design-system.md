# Blazor design system

Agentic Lab's Blazor UI uses a small, repository-owned design system based on the React frontend's
visual language. It is not a second UI framework. Shared presentation has no dependency on Flow state,
the AI service, React or an npm build step.

## Ownership

- [design-system.css](../src/AgenticLab.Web/wwwroot/design-system.css) owns the document-level `--lab-*`
  tokens and local font faces. [App.razor](../src/AgenticLab.Web/Components/App.razor) loads it once.
- [app.css](../src/AgenticLab.Web/wwwroot/app.css) contains application resets and framework styles.
- [Components/Shared](../src/AgenticLab.Web/Components/Shared) owns reusable Razor controls and their
  scoped styles. Each feature component owns its layout in its own scoped stylesheet.
- `/design-system` is a development-only, interactive catalogue of real shared controls. It makes no
  backend calls and returns Not Found outside Development. It is not part of production navigation.

## Tokens

| Family | Purpose |
| --- | --- |
| `--lab-font-sans`, `--lab-font-mono` | IBM Plex Sans for UI; IBM Plex Mono for metadata and code |
| `--lab-text-*`, `--lab-leading` | Fixed type scale and readable line height; no viewport-sized text |
| `--lab-surface*`, `--lab-page` | White and quiet neutral backgrounds |
| `--lab-ink`, `--lab-muted`, `--lab-subtle` | Text hierarchy; subtle is for nonessential decoration |
| `--lab-accent*`, `--lab-on-accent` | Green commands, focus and selected states |
| `--lab-user`, `--lab-host`, `--lab-model`, `--lab-tool` | Actor markers, not small body text |
| `--lab-danger*`, `--lab-warning*`, `--lab-info*` | Semantic states, always accompanied by text |
| `--lab-contributor-*` | Fixed context-provenance colours, independent of actor/vendor branding |
| `--lab-space-1` through `--lab-space-8` | 4, 8, 12, 16, 20, 24, 32 and 40 pixel spacing |
| `--lab-control-*`, `--lab-icon-size` | Stable control geometry |
| `--lab-border*`, `--lab-radius-*` | Fine dividers and 4/6/8px corners |
| `--lab-grid-image`, `--lab-grid-size` | Shared subtle dotted diagram surface |
| `--lab-focus-*`, `--lab-duration`, `--lab-easing` | Visible keyboard focus and restrained motion |
| `--lab-shadow-overlay`, `--lab-backdrop`, `--lab-layer-*` | Overlays, not decorative section cards |

Reuse semantic tokens instead of repeating palette literals. Existing feature-specific aliases may map
to these tokens, but must not self-reference or confuse a foreground accent with a tinted background.
Contributor provenance, risk levels and signed chart scales retain their distinct meanings.

## Components

`AppHeader` provides the product mark, page context and repository link. Its child content supplies only
the page's existing actions. Keep it inside the page's render boundary; it does not own navigation or
run state. Agent guide links from Flow and Discovery open a new tab. Learn has no Discovery entry.

`LabButton` supports `primary`, `secondary`, `quiet` and `danger` intents, an optional `MiniIcon`, an
icon-only mode, disabled/busy states and an optional pressed state. `Label` is required even for an
icon-only command. Use `OnClick` for actions; use native anchors for navigation. An icon-bearing command
keeps its icon slot when busy. Keep the label stable during progress to prevent width changes.

```razor
<LabButton Label="Send" Icon="send" Variant="primary" OnClick="SendAsync" />
<LabButton Label="Pause" Icon="pause" IconOnly="true" OnClick="PauseAsync" />
```

`LabField` associates its label with a caller-supplied native input using `For`. The caller keeps binding
and validation. When supplying `Hint` or `Error`, set the input's `aria-describedby` to
`<id>-description`; also set `aria-invalid` for an error. `Inline` supports compact selection bars.

```razor
<LabField For="workspace" Label="Workspace" Error="@Error">
  <input id="workspace" @bind="Workspace" aria-invalid="@(Error is not null ? "true" : "false")"
           aria-describedby="workspace-description" />
</LabField>
```

`LabSegmented` groups a small set of mutually exclusive native buttons. Each button supplies its
`aria-pressed` state and callback. Render ARIA booleans as the strings `"true"` and `"false"`, not
Razor boolean attributes (which are minimised). It is a choice group, not a tablist. Real tabs need tab/tabpanel
relationships, arrow-key navigation and a managed tab stop.

`LabStatus` pairs a textual state with a marker. `Tone` is `neutral`, `success`, `warning` or `danger`;
`Busy` animates only the marker. The label must explain the state without relying on colour or motion.

`SidePanel` remains the reusable resizable/collapsible dock. `MiniIcon` and the pinned Lucide assets
remain the icon source. Native dialogs retain focus containment, Escape dismissal and focus restoration;
do not substitute visually styled containers for modal semantics.

`PageNotice` uses the same header and typography for error and not-found routes without changing their
HTTP status or diagnostics. `SidePanel.ShowHeader` defaults to true; a tabbed child can supply its own
header and collapse action. Splitters are focusable separators supporting arrows, Shift+arrows, Home
and End as well as pointer dragging. Their geometry comes from the owning layout, not the design system.
The framework reconnect dialog consumes the same colour, font and motion tokens while retaining its
native JavaScript-driven reconnection lifecycle.

## Accessibility and layout

Use native buttons, labels, selects, checkboxes, ranges and dialogs. Tool actions use labelled icons;
binary options use checkboxes, mode sets use segmented choices, and numbers use sliders or inputs.
Hover is never the only way to reach an action. Maintain visible focus, disabled and validation states.
Normal text should meet WCAG AA contrast; actor colours are supplemental markers. Reduced motion must
disable decoration, never backend pacing or progression.

Page sections are flush, divider-led layouts, not nested cards. Use fixed type sizes, zero letter spacing
and container-driven reflow. Set `min-width: 0` on grid/flex children and wrap long labels; only payload
regions may scroll horizontally. Reflow must not mutate saved panel preferences, run options or state.
Use the shared spacing scale, but let each feature own its responsive grid and domain-specific diagrams.

## Assets and contributions

Fonts are the Latin subsets of `@fontsource/ibm-plex-sans` and `@fontsource/ibm-plex-mono` **5.3.0**:
Sans 400/500/600, Mono 400. WOFF2 files live under Web's `wwwroot/fonts`, with OFL notices under
`wwwroot/licenses`. No CDN is contacted at runtime. Diagram icons use the existing pinned Lucide SVGs
and their bundled licence; the repository link retains the Octicons mark and notice.

Before adding a shared component, find two real consumers or a repeated accessibility/behaviour contract.
Prefer parameters and events over dependencies on a feature's state. Add a catalogue example and short
XML parameter summaries, then verify the actual consumers. Do not build a parallel component library
that pages do not use. Build Web and check the showcase at desktop/mobile widths before migrating a
new pattern; run the Web regression tests for layout-state or interaction changes.
The repeatable no-model-call browser check and temporary Playwright setup are in
[tools/README.md](../tools/README.md). It checks the catalogue and real pages, not just isolated examples.