# ADR-0002: Blazor Web App (interactive server) over Next.js; WebAssembly rejected

- **Status:** Accepted
- **Date:** 2026-10-26
- **Work package:** WP00

## Context

The user interface needs to call three backend services, hold a server-side session tied to a
cookie, and render a request builder, a collection tree and a response viewer. The team is
.NET-only. The estate's reference architecture offers two frontend stacks: a Next.js frontend
with a backend-for-frontend, and a Blazor Web App.

The Blazor guide's comparison (`BLAZOR-WEB-APP.md` §1) puts Blazor's best fit at "internal tools,
dashboards, single-owner or low-concurrency tools, teams that are .NET only". Next.js's best fit
is "public or multi-instance products, SEO, offline-tolerant UIs". This product is an internal
tool for a small set of signed-in users, and it has no public pages to rank.

## Options considered

1. **Next.js with a BFF** (the estate's other supported option, `FRONTEND-BFF.md`). Rejected for
   this product: it adds a second language and a Node runtime to a .NET-only team, and the product
   has no public or SEO surface that would benefit.
2. **Blazor WebAssembly.** Rejected. The UI code would run in the browser and call the backend
   directly, so the access token would have to live in JavaScript-reachable code. That breaks the
   browser boundary in [`AGENTS.md`](../../AGENTS.md) and [ADR-0005](0005-bff-session.md). It also
   puts the .NET runtime download on every first visit.
3. **Blazor Web App, interactive server render mode.** Chosen. Rendering and state stay on the
   server; the browser holds only a websocket and an HttpOnly cookie.

## Decision

The Web is a Blazor Web App in interactive server render mode. There is no WebAssembly project,
no Node toolchain and no JavaScript or CSS framework (see [ADR-0009](0009-dependency-budget.md)).
The `HeadOutlet` and `Routes` components use interactive server mode; a test pins this (PROTOCOL §5).

## Consequences

- One language and one toolchain across the Web and the three services. The team can review
  both sides of a change.
- Every interaction is a round trip over the circuit. Latency is acceptable for an internal tool
  and would not be for a public, offline-tolerant product.
- A circuit holds server memory per open tab. [ADR-0008](0008-single-instance-web.md) records the
  consequence: the Web runs as one instance.
- The UI is limited to what Blazor's components and CSS provide. No charting library, no component
  kit, no JavaScript interop; features that need them are out of scope.
- Blazor's own test tooling (bUnit) covers component logic; end-to-end tests run in a browser
  (Playwright).

## Revisit when

The product gains a public, unauthenticated surface that needs SEO, or the user count grows to a
point where server-held circuits become the bottleneck. Either would reopen the comparison in
`BLAZOR-WEB-APP.md` §1 and would be a new ADR.
