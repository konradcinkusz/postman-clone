# UI style contract

The Web's visual design is a small, frozen contract that the tests read from disk
([BLAZOR-WEB-APP §9](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/BLAZOR-WEB-APP.md)).
It is the only place colour is defined. The Web has no CSS framework, no component library and no
JavaScript of its own ([ADR-0009](../decisions/0009-dependency-budget.md)), so everything below is plain CSS in
two places: `src/PostmanClone.Web/wwwroot/app.css` (tokens, base elements, shared classes) and one scoped
`*.razor.css` per component.

`StyleContractTests` (`tests/PostmanClone.Web.Tests/Styles/StyleContractTests.cs`) enforces the rules marked
**tested** below. A change that breaks one fails the build's test run, not a review.

## 1. Tokens

Defined on `:root` (light, the default) and redefined under `@media (prefers-color-scheme: dark)`. Dark mode
redefines **colour tokens only**; layout tokens are the same in both themes.

| Group | Tokens |
|---|---|
| Surface | `--surface-page`, `--surface`, `--surface-sunken` |
| Text | `--text`, `--text-secondary`, `--text-muted` |
| Accent | `--accent`, `--accent-hover`, `--accent-contrast` (text on an accent fill), `--accent-soft` |
| Status | `--success`, `--success-soft`, `--warning`, `--warning-soft`, `--danger`, `--danger-soft` |
| Lines | `--border`, `--shadow` |
| HTTP methods | `--method-get`, `--method-post`, `--method-put`, `--method-patch`, `--method-delete`, `--method-head`, `--method-options` |
| Response viewer | `--code-key`, `--code-string`, `--code-number`, `--code-keyword`, `--code-muted` |
| Layout (both themes) | `--radius`, `--radius-sm`, `--space-1` … `--space-6`, `--gutter` (16 px), `--content-width`, `--leading` |
| Type (both themes) | `--font-sans`, `--font-mono` (system stacks only), `--text-xs` … `--text-2xl` |

Rules:

- **Tested:** every colour token is defined in both themes; the dark block defines no layout token.
- **Tested:** no colour appears outside the two token blocks, in `app.css` or in any `*.razor.css`. That means no
  hex value, no `rgb()`/`hsl()`/`oklch()`-style function and no named colour such as `white` or `transparent`.
  Use `var(--token)`, never a literal, in a rule.
- Text on a soft fill uses the solid status colour (`.badge-success` text is `--success` on `--success-soft`).
- Body text and secondary text reach 4.5:1 in both themes (`--text-secondary` and `--text-muted` are chosen for
  that on `--surface` and `--surface-page`).
- **Tested:** there are no web fonts and no `@import`. The stack is the system font.

## 2. Shared classes

These names are the contract. A change that needs a new shared class adds it here first, in the same pull
request; a page-specific style goes in its own scoped `*.razor.css`.

**Tested:** every name below exists in `app.css`.

| Class | Use |
|---|---|
| `.card` | A bordered surface with padding and a shadow |
| `.stack` | A vertical list with the standard gap (also removes the list bullets) |
| `.cluster` | A wrapping row with the standard gap |
| `.split` | A wrapping row with the space between items |
| `.field` | A label above a form control (`.field > label`, then the input, select or textarea) |
| `.btn` | A button or link-button. `.btn-primary` for the one main action, `.btn-danger` for a destructive one |
| `.badge` | A small pill for a state or a count. Modifiers: `.badge-success`, `.badge-warning`, `.badge-danger`, `.badge-accent` |
| `.callout` | A message in a box with a coloured left edge. Modifiers: `.callout-success`, `.callout-warning`, `.callout-danger` |
| `.table-wrap` | Scrolls a wide table inside its own box, never the page |
| `.num` | A monospace, right-aligned, tabular number |
| `.empty-state` | A dashed box for a page or list with nothing in it |
| `.kv-table` | A two-column key and value table |
| `.tabs` / `.tab` | A tab strip. The selected tab carries `aria-selected="true"` |
| `.tree` / `.tree-node` | A list of nested nodes. The selected node carries `aria-selected="true"` |

## 3. Accessibility and layout

- **Tested:** every interactive element shows a focus ring (`:focus-visible`).
- **Tested:** under `prefers-reduced-motion: reduce`, transitions and animations are switched off.
- **Tested:** `.table-wrap` scrolls on the x axis.
- The page works at 390 px width with no horizontal page scroll. The layout stacks the navigation above the content
  below 48 rem (768 px). This was checked in a headless Chromium at 390 px in both themes; it is not a unit test.
- Every test hook (`data-testid`, an ARIA role, an accessible name) survives a restyle. If a design change must move a
  hooked element, the test changes in the same pull request and the pull request says why.

## 4. What is not here

No icon font, no image sprites, no JavaScript interop, no CSS framework, no chart library. Charts, when they come,
are Razor components that emit SVG with their own `--chart-*` tokens, added to this document with the change.
