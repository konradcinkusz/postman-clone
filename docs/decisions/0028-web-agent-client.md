# ADR-0028: The Web's agent client: one hand-written script, pairing in the circuit, and the browser's limits

- **Status:** Accepted
- **Date:** 2026-11-01
- **Work package:** WP31 (the Web side of ADR-0026; the agent is WP30, its threat model ADR-0027)

## Context

ADR-0026 moves sending to the user's computer. A server-rendered page cannot call `http://127.0.0.1:47631` by itself: a form post
cannot read the answer, and a Blazor component runs on the server. The browser must make the call, so the Web needs JavaScript.
ADR-0009 forbids JavaScript interop and JavaScript libraries. This ADR records the exception, its size, and what it may do.

The page also holds a pairing token for the agent. The rule that tokens never reach browser JavaScript (AGENTS.md) is kept for the
session token, which stays on the server (ADR-0005). The agent's pairing token is different: it is the credential the browser must
present to the agent on loopback (ADR-0026 Decision 5). It is held in the circuit and never stored in the browser.

## Options considered

1. **A JavaScript library for the calls.** Rejected: ADR-0009 forbids it, and one `fetch` needs none.
2. **Several scripts, one per concern.** Rejected: each file is a surface to review. One file keeps the boundary in one place.
3. **The browser keeps the pairing token in `localStorage` or a cookie.** Rejected. A stored token survives the page and is readable
   by any script on the origin. The page holds it in the circuit's memory only (ADR-0026 Decision 5).
4. **The server calls the agent.** Impossible by design: the agent listens on the user's loopback, and the server cannot reach it.
5. **Pairing code in the URL after the form post.** Rejected. A URL is kept in history and in logs. The code goes to the page by a
   one-time ticket that only the same session can redeem (`AgentPairingTickets`, two minutes).
6. **`[JSInvokable]` callbacks for every result.** Not chosen. Every call starts in the circuit, so the answer comes back as the
   return value of the `IJSRuntime` call. The WP text mentioned callbacks; no call needs one, so none is written.

## Decision

1. **One script, `wwwroot/agent-client.js`.** It has no library and writes nothing to the page. Its functions are `probe`, `pair`,
   `send`, `cancel` and `postRun`. Each is one `fetch` whose status and text go back to the circuit. It keeps no state except the
   abort controllers of sends in flight. It is the only script the Web loads besides Blazor's own. The architecture test
   `AgentBoundaryTests` checks the count, the absence of DOM writes and of storage, and the absence of any package reference.
2. **The browser's calls.** `probe` tries the configured loopback origins in order, with a two-second timeout. A refusal from the
   origin check (403 or 421) is a distinct answer, so the page says "not paired" and does not keep trying other origins.
   `pair` and `send` go only to the origin that answered the probe.
3. **Pairing token in the circuit.** `AgentConnection` holds it. It is passed to the script on each send, and is never rendered,
   never persisted and never in a URL. A reload or a closed page loses it, and the status then reads **Pair again**. That is the
   cost of not storing it.
4. **Session token never sent to the agent or the script.** The browser calls the agent with the pairing token only. The Web's
   own run record uses the session's bearer on the server (decision 6).
5. **Sender choice.** The default is the agent when the page holds a pairing and the agent answered as paired; otherwise the
   server. A choice the user makes is kept for the page visit. The response panel names the sender.
6. **Run record through the Web.** After an agent send the script posts the masked result to `POST /agent/runs` with the
   antiforgery token in the `RequestVerificationToken` header (the framework's default; WP31 wrote `X-CSRF-TOKEN`, which the validation
   never read, so no run was recorded until WP32 fixed it). The route refuses an anonymous caller with 401 and a bad token with 400, applies the
   name-based mask, and forwards the record to ExecutionService with the session's bearer (CONTRACTS A2.5, A2.7). A JSON caller that is
   not signed in gets 401 from the session scheme, not a redirect: a general rule in `SessionAuthenticationHandler`, tested.
7. **Secrets.** A send carries the environment's id. The agent resolves the values itself with its agent token (ADR-0026 Decision 4).
   No secret value passes through the browser or the Web.
8. **CSP.** `connect-src` gains exactly the configured loopback origins (`Agent:ConnectOrigins`, default `http://127.0.0.1:47631` and
   `http://localhost:47631`). Configuration that names any other origin, a wildcard or another port stops the host at startup.
   `script-src` stays `'self'`. A test reads the header of a running host.
9. **Layout.** The agent pages use the existing tokens and classes, and the new rules add no colour outside the tokens.

## What was verified, and by what

| Claim | How | Result |
|---|---|---|
| The status states, the default sender, the sentinel in no markup | bUnit with a real `AgentConnection` and a fake browser (`AgentComponentTests`, `AgentSendTests`) | Passed |
| The Agent page: the one-time ticket, the revoke form's token, a ticket of another session | bUnit (`AgentPageTests`) | Passed |
| The run route: 401 anonymous, 403 foreign origin, the CSP text | `AgentHostTests` against the running host | Passed |
| The loop in a real browser: probe, pair, send to a local target, the agent's masking, a wrong token | Chromium (Playwright) on `http://localhost:5310` against the built agent, with a stand-in server for the pairing route | Passed (done by hand in WP31, not in CI) |
| The CSP blocks any other origin | Chromium: a probe to an unlisted port is refused by the policy | Passed |

## Not verified

- **HTTPS pages.** The Web is deployed on HTTPS, and the browser calls `http://127.0.0.1`. Chrome treats loopback as a
  potentially trustworthy origin, so the call is allowed. This was not tested on an HTTPS page: no HTTPS origin runs here.
- **Other browsers.** Only Chromium was run. Firefox and Safari were not tested. Safari's handling of an HTTPS page calling
  `http://127.0.0.1` is an open question (ADR-0026 Consequences).
- **Local Network Access and Private Network Access prompts.** Headless Chromium does not show the prompt. A prompt on a real
  desktop is not observed.
- **The full pairing with the real server, and a real signed-in run record.** These need authservice and EnvironmentsService, which
  do not run here. WP32 covers them in the browser journeys.

## Consequences

- The Web has one script, and that script is the boundary. A reviewer checks it by reading about 120 lines.
- After a reload the page must pair again. A new pairing creates a new agent row in the workspace's list, so old rows accumulate until
  they are revoked. Reusing the agent's row for a new pairing is a follow-up (OPEN-ITEMS).
- The pairing store and the run-record path are in memory and in the process, which is correct for one instance (ADR-0008).

## Revisit when

- A browser changes how it treats `http://127.0.0.1` from an HTTPS page, or how it asks for Local Network Access.
- The owner decides on a keychain for the agent token, on per-host confirmation, or on the release channel (ADR-0026 open items).
- A second Web instance is planned: the pairing tickets and the run path would need shared storage.
