# ADR-0018: End-to-end journeys: scope, the two sender modes, and what the suite may and may not do

- **Status:** Accepted
- **Date:** 2026-10-31
- **Work package:** WP26

## Context

The unit and in-process tests (WP01–WP25) prove each layer with fakes. Nothing before this record drove the Web in a browser
against the services, so the flows that cross every layer were never run together: sign-in through the form, a send from the
builder to a target, the rendering of the response, the masking of a secret in the page and in history, and the isolation of
one workspace from another in the page. OI-24 and OI-15 name this gap.

Three facts shape the suite.

1. **The estate runs without Docker here.** Sessions have no Docker daemon, and the AppHost has never run (OI-19). The E2E
   suite therefore runs against the no-Docker estate that `scripts/run-local-stack.sh` starts (authservice at its pinned tag,
   the three services and the Web, on the ports of the plan's README §3). CI runs the same script on its runner.
2. **The sender refuses loopback by default**, and that is the product: a user's request may not reach the host's own services
   (ADR-0011). A journey that sends to a local target therefore needs a test-only switch.
3. **Blazor Server renders twice.** The server's first render is replaced when the circuit connects, and an event sent before the
   circuit runs is lost without an error. A journey must wait for the circuit, and it must wait on state, not on time.

## Decision

**Scope.** The suite is `tests/PostmanClone.Web.E2E`. It drives Chromium through Microsoft.Playwright and asserts on what the
user sees and on the state the services hold. It covers the protected flows below and nothing else. Each protected flow has at
least one journey, and each journey has at least one assertion on application state (not only on navigation).

Protected flows (journey file in brackets):

- Sign-up through the register form, a workspace created on the no-workspace page, sign-in, a wrong password refused with no
  session, the session cookie HttpOnly and absent from `document.cookie`, sign-out, a protected page redirecting to sign-in
  (`AccountJourneys`).
- A collection, a folder and a request created, saved, reloaded and found again with their URL (`WorkspaceJourneys`).
- A request sent to a local target: a GET shows status and body, a POST's JSON body arrives as sent, markup in a response is
  shown as text and creates no element, and a sent request appears in history and reopens as an unsaved draft (`SendJourneys`).
- The sender's refusal of loopback with its reason, and of the link-local metadata address in both modes (`SendJourneys`).
- A secret variable typed, saved and reloaded: its value is absent from the page at each step; a bearer secret sent to the target
  is masked in the response and in history (`EnvironmentJourneys`).
- A cURL command and a Postman v2.1 file imported, the collection exported, and the export imported again (`ImportExportJourneys`).
- Another workspace's request answers "no longer exists" and its address does not appear in the page; a user with two
  organizations sees the Multiple page; the members page lists the owner (`AccessJourneys`).
- The shell, builder, environments and history pages have no horizontal scroll at 390 px; the dark colour scheme applies the dark
  surface token (`PresentationJourneys`).

**Two sender modes, selected by a trait, not by a skip.** `scripts/run-local-stack.sh up` with `E2E_SENDER_LOOPBACK=1` sets
`Execution:Ssrf:AllowLoopback=true` for the local stack only (the service refuses that flag in Production, so the stack runs as
Development in that mode). The journeys that send to the local target carry `Mode=loopback`. The journeys that assert the default
refusal carry `Mode=strict`. CI runs the loopback set on the loopback stack and the strict set on a stack started without the
flag. A journey in the wrong set fails, which is visible; it is never skipped to pass. The metadata-address journey has no mode:
link-local is refused in both.

**Prerequisites: skip with a reason locally, fail in CI.** A journey first checks the Web, authservice and CollectionsService
answer, and that a Chromium exists. A missing prerequisite skips the journey with the reason. With `POSTMANCLONE_E2E_REQUIRED=1`
(set in CI) it fails. A skipped journey is never reported as a pass.

**Browser.** The suite finds a Chromium (`E2E_CHROMIUM_PATH`, then a `chromium-*` folder under `/opt/pw-browsers` or Playwright's
cache) and never downloads one. CI installs Chromium with `playwright.ps1 install --with-deps chromium` before the run. Sessions use
the Chromium that is already on the machine and never run `playwright install`.

**Waiting.** A journey waits for the circuit before it interacts with an interactive page (`Circuit.WaitForConnectedAsync`):
the client runtime has started and, where the page has a status strip, the server has replaced its placeholder. Assertions are
web-first (they retry until the state holds or the timeout ends). Two waits are not sleeps: the circuit check, and `AuthBudget`
(below), which waits until the authservice window has room. No journey sleeps for a fixed time.

**authservice's window.** authservice v0.3.4 allows 20 auth requests per minute per client IP and queues five more inside its
own process until the window reopens (`Program.cs`, the `auth` policy). Every journey calls from one address, so an unbudgeted
suite waited up to a minute inside authservice. `AuthBudget` counts the suite's register, login and logout calls and keeps them
at 18 per minute. The journeys also run one at a time (`xunit.runner.json`). Both are needed: the budget bounds the rate, and the
serial run keeps the count predictable. The limit is authservice's; the suite adapts to it and does not change it (no authservice
feature is invented, docs/AUTH.md).

**Data and cleanup.** Each journey creates its own users (a GUID address and a generated password, through authservice's own
API) and its own workspace, and signs in through the real form. Nothing a journey creates is shared with another. The suite does
not delete server data: the local databases accumulate test rows, and a fresh set is one command away (drop the four service
databases and bring the stack up again). A journey's temporary files are written under the system temporary folder and deleted when
the journey ends. A journey never depends on the order of another.

**Locators.** Elements are found by their test id (`data-testid`, the same ids the component tests use) or by role and accessible
name. A tree row is found by its visible text. No locator is a CSS path, an index into the page or a class name.

**Test data that looks like a secret** (the generated passwords and secret values) is created at run time, so it is not in the tree.
The one literal password in a fixture carries `gitleaks:allow`.

## Non-goals

- The AppHost and its containers (not run here; OI-19). The E2E suite does not replace the AppHost run.
- Two-factor sign-in, email verification, password reset, external sign-in and account deletion (not in scope of the plan; docs/AUTH.md).
- Browsers other than Chromium, and devices other than the 390 px and the desktop viewport.
- Accessibility audit, performance and load. The suite asserts the behaviour the plan promises, not timings.
- Deployment (ADR-0007).

## Consequences

- The suite is slower than the unit tests: about 1.5 minutes for the loopback set and 1 minute for the strict set on the sandbox
  (4 vCPU). CI adds the cost of building the estate (about 4 minutes on a runner).
- A change to a protected flow that keeps the unit tests green can still fail a journey. That is the point.
- The journeys depend on the test ids in the Web. A renamed id fails its journey, and the fix is in the Web or in the journey,
  never a looser locator.
- The sender's loopback flag exists only in the local stack, behind an explicit environment variable. The Production validator
  still refuses it, and the AppHost never sets it.

## Alternatives considered

- **Sending to a target on another address in the same network.** Still refused by the private-range rule, and a machine with
  such an address is not guaranteed on a runner. Rejected.
- **Disabling the SSRF guard in the test process.** The guard is in the service, not the test process. Rejected: the journeys must
  test the real service.
- **Running all journeys in one stack configuration.** Would test the refusal only by not testing the send. Rejected: two modes,
  each asserted.
- **Shared test users with stored browser state.** Reuses one login across journeys and couples them. Rejected: each journey signs
  in through the form, which is also what a user does.
- **Sleeping between authservice calls.** A fixed delay is a guess about the window. Rejected for the budget that counts the calls.
