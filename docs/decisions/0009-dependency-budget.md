# ADR-0009: Dependency budget: the ASP.NET Core shared framework in the Web; no UI, chart, JavaScript or CSS libraries

- **Status:** Accepted
- **Date:** 2026-10-26
- **Work package:** WP00

## Context

Every dependency is a supply-chain surface, a licence to read, an update to carry and a security
advisory to watch. The reference architecture asks that adopting a dependency be a decision the
estate can see, and that a number which goes up in a diff be a question someone can ask
(architecture-standards `REPO-BASELINE.md` §4b).

The Web is a server-rendered application. The shared framework that ships with ASP.NET Core already
provides routing, the Blazor components, antiforgery, authentication and the JSON serializer. A
component library, a charting library or a CSS framework would add a large amount of JavaScript and
CSS to ship and review, with no requirement that needs it.

## Options considered

1. **A component kit** (MudBlazor, Radzen or similar) for tables, dialogs and inputs. Rejected: a large
   dependency with its own JavaScript and styling, when the screens are plain forms and lists.
2. **A CSS framework** (Bootstrap, Tailwind). Rejected: the stylesheet test pins colours to CSS tokens,
   and a framework fights that rule.
3. **The shared framework and small hand-written code.** Chosen. Components use Blazor's own markup
   and scoped CSS, colours come from tokens on `:root`, and there is no JavaScript interop.

## Decision

- The Web uses the ASP.NET Core shared framework and the packages listed in `Directory.Packages.props`.
  No other UI, chart, icon or CSS package is added.
- No JavaScript or CSS library is loaded, and no JavaScript interop is used. Features that need them
  (keyboard shortcuts, clipboard, drag and drop) are out of scope ([ADR-0001](0001-product-scope.md)).
- Colours are defined only as CSS tokens. A stylesheet test rejects a literal colour in a component.
- Each new package is a decision: it goes into `Directory.Packages.props` in the change that needs it,
  with the reason in the pull request.
- Hand-rolling is preferred when the needed subset is small and stable. Sending requests, parsing the
  Postman format and validating variables are written by hand where the library would be larger than the code.

## Consequences

- The Web has few moving parts, a small attack surface and very little JavaScript to review.
- Some UI work costs more: a sortable table or a dialog is written by hand. The screens are simple enough
  that this is a fair trade.
- Dependabot has few packages to update. The weekly update groups keep the diffs reviewable.
- The budget is visible in the README's project table, so a dependency that goes up is a question
  a reviewer can ask.

## Revisit when

A feature needs a capability the framework cannot provide cheaply and a library is the only sane way.
That is a new ADR with the package, its licence and its transitive footprint written down.
