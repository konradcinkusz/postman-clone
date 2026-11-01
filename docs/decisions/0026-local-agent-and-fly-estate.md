# ADR-0026: Local agent sends requests; Fly hosts the shared estate

- **Status:** Accepted (owner decision). Supersedes ADR-0007 for this product; ADR-0007 stays as the record of the earlier decision.
- **Date:** 2026-11-01
- **Work package:** WP29 (implemented by WP30, WP31, WP32, WP33)

## Context

The sender runs on the server. ExecutionService resolves the request, applies the SSRF guard (ADR-0011) and sends it from the
estate. That guard is the right default for a public API, and it makes the product useless for the targets a developer most
often calls: `localhost`, a LAN address, a container on the desk. A deployed instance cannot reach any of them, and the guard
must not be loosened to make it do so.

The owner's decision is to move sending to the user's own computer and to host the shared parts on Fly.io, so that a team
shares collections, environments and history:

- **(a)** Every user installs a local agent that sends the requests from their machine.
- **(b)** The shared parts (Web, CollectionsService, EnvironmentsService, ExecutionService, authservice, PostgreSQL) run on Fly.io.
- **(c)** The Fly deployment runs only by hand (`workflow_dispatch`). No tag and no push trigger. No Fly token exists yet.
- **(d)** Environment secret values may be delivered to a user's computer. This is an accepted trade-off, recorded here.

This ADR records the design. The code is in WP30–WP33. The contracts it relies on are amendment A2 in
[CONTRACTS.md](../implementation-plan/CONTRACTS.md).

## Options considered

1. **Keep the server-side sender for every request.** Rejected for local and private targets: the guard refuses them by design,
   and relaxing it for a deployed instance would turn the estate into an SSRF platform. Kept for public URLs (see Decision 8).
2. **Browser-direct fetch to the target.** Rejected. A browser can only read a response whose server sends CORS headers, and most
   APIs do not. The page's JavaScript would also hold the resolved secrets and send them from the page's origin. Browsers also restrict
   a public page's requests to private addresses (Private Network Access), so the path would depend on browser behaviour that
   changes between releases.
3. **A reverse tunnel or a VPN from the estate into the user's network.** Rejected. It moves the trust boundary the wrong way: the
   hosted service would reach into every user's network, and each user would be exposed to the whole estate.
4. **A browser extension as the sender.** Rejected for this build. It adds a second store-reviewed product, a second code base
   and a permission model the dependency budget (ADR-0009) does not cover.
5. **A local agent that the browser calls on `127.0.0.1`.** Chosen. The agent is a small program on the user's machine. It sends
   the request, applies the same masking and caps as the server, and returns the same result shape.
6. **JWT agent tokens signed by a new service.** Rejected. Exactly one service holds a signing key (P5); a second signer means a
   second key that can mint tokens for every service. Signing by authservice needs a change to a pinned external service
   (ADR-0010). The agent token is therefore opaque (Decision 3).
7. **The Web fetches the resolved secrets and forwards them to the agent.** Rejected. The Web and the browser would hold
   plaintext secrets for the length of a send. The agent fetches them itself, over TLS (Decision 4).

## Decision

1. **Two senders, one result shape.** A user sends either through their agent or through the server. The Send control chooses,
   and the default is the agent when one is paired and connected. The response viewer shows which sender answered. Both return
   `ExecutionResult` (CONTRACTS §4) with the same masking, caps and outcomes.
2. **The shared estate is deployed to Fly.io**, one app per service, as planned in WP33 (P7). PostgreSQL has no public listener.
   Every app is built from its own Dockerfile by the workflow, and nothing is pushed to a registry except by that workflow.
3. **Agent credentials are opaque and owned by EnvironmentsService.**
   - The **agent token** is 32 random bytes, shown once to the agent, and stored as a SHA-256 hash in `environmentsdb`. It has
     the purpose `agent`, a workspace, a user, an expiry (90 days from pairing) and a revocation time.
   - A **pairing code** is 10 random Crockford base32 characters (about 50 bits), shown in the Web for five minutes, single-use,
     and stored as a hash. A new code for the same user replaces an unused one.
   - **Audience.** The agent token is accepted only by routes under `/api/v1/agent/` that are marked agent-only. A user's token
     is refused on those routes. An agent token is not a credential on any other route, and it gets the same 401 as an unknown
     bearer. Both rules are architecture tests (WP30).
   - No new signing key is created, and authservice is not changed.
4. **Secret values go from EnvironmentsService to the agent, not through the browser or the Web.** The agent calls
   `POST /api/v1/agent/environments/{id}/resolve` with its agent token. The response is the same `ResolvedEnvironment` that
   ExecutionService receives today, with plaintext for enabled secrets. The workspace comes from the agent token. An environment
   of another workspace is `404 not_found`.
5. **The browser talks to the agent through one local protocol** (CONTRACTS A2.6):
   - The agent listens on `127.0.0.1:47631` only. It never binds `0.0.0.0` or `::`, and it refuses to start if the port is taken.
   - **Host check.** `Host` must be `127.0.0.1:47631` or `localhost:47631`. This defeats DNS rebinding: a page on an attacker's
     domain that rebinds to `127.0.0.1` still sends its own `Host` header.
   - **Origin allow-list.** `Origin` must equal one of the Web origins that EnvironmentsService returns at pairing
     (`Agent:AllowedWebOrigins`, set in the server's configuration, not by the user). An absent `Origin` is refused. The agent
     echoes only that exact origin in CORS responses (never `*`, never credentials, `Vary: Origin`).
   - **Private Network Access.** The preflight answers `Access-Control-Allow-Private-Network: true` when the browser asks for it.
   - **Pairing token.** Pairing gives the page a 32-byte local token, `pairingToken`. The page keeps it in circuit memory only,
     never in markup, persisted component state, storage or a URL. The agent stores only its hash. Every `/v1/send` must carry it
     in `X-Agent-Pairing-Token`, which also forces a preflight. The body must be `application/json`, so a "simple" cross-site
     request cannot reach the handler.
   - **Pairing exchange.** The one-time code is shown in the Web. The page passes it to the agent's `POST /v1/pair`, and the agent
     exchanges it with EnvironmentsService for its agent token. The browser never receives the agent token. A new pairing replaces
     the previous local token.
6. **The agent's sender is the server's sender without the guard, by default.** The agent uses the same assembler, executor,
   masking and caps (CONTRACTS §4): body ≤ 1 MB, ≤ 100 headers, URL ≤ 8192, read cap 5 MB, timeout 30 s / 60 s, redirects ≤ 5,
   no ambient proxy, no cookies. It allows loopback, private and link-local targets, because reaching them is the point. It always
   refuses a non-http(s) scheme, a URL with user info, and a target that is the agent's own listener. The option
   `Agent:Targets:AllowPrivate` (default `true`) turns the server's guard back on for a user or a team that wants it.
7. **Run history is written by the Web, not by the agent.** The agent holds no credential for ExecutionService. After a send, the
   page posts the masked result to the Web's BFF endpoint `POST /agent/runs`. That endpoint uses the user's session token to call
   `POST /api/v1/history/records` (CONTRACTS A2.5). The record has the same retention as a server send: 200 per user and
   workspace, the template with its references, the result at most 64 KB. A history entry has `source: "agent"` (an optional new
   field).
8. **ExecutionService is kept, and its role is narrower.** It keeps:
   - the history store (`executiondb`), which now also receives agent runs;
   - `POST /api/v1/execute` for requests to public URLs, with the SSRF guard unchanged (ADR-0011, ADR-0014); a local target through
     the server is refused with `ssrf_blocked_address`, as before;
   - the `resolve` call it makes today for the server sender.
   Nothing in it is removed. It stops being the only sender.
9. **The browser runs one hand-written JavaScript file** (`agent-client.js`, WP31). It has no library, and it is the only script the
   Web loads. It probes the agent, pairs, sends, and posts the masked result to the BFF. This is a deliberate exception to ADR-0009,
   which forbids JavaScript interop. A browser cannot reach `127.0.0.1` from a server-rendered page without script, and a form
   post cannot read the answer. ADR-0028 records the exception and its limits. The Web's CSP gains exactly the two agent origins in
   `connect-src`, and nothing else.
10. **Deployment runs by hand only.** `flyio.yml` has `on: workflow_dispatch` and nothing else, with an input that selects the apps.
    It contains no tag, push, pull-request or schedule trigger, and a CI check fails if one appears (WP33). The workflow needs
    `FLY_API_TOKEN`, which the owner adds (owner action).
11. **Secrets reaching a user's computer is accepted (d).** The agent holds the resolved values in memory for one send. It never
    writes them to disk, and it masks them in every response, log line and error it produces (CONTRACTS §4 masking). The agent's
    own configuration (its agent token) is the only credential it stores, in a file readable by the user alone (`0600`, or a
    user-only ACL on Windows).

## Threat model of the local listener

The listener is the new attack surface. Each row is a threat, the control, and where the control is tested.

| Threat | Control | Test (WP30 / WP32) |
|---|---|---|
| A web page on another site calls `127.0.0.1` (CORS and the browser's Origin) | Origin allow-list, exact echo, no `*`, no credentials | Foreign Origin refused; preflight has no allow-origin for it |
| DNS rebinding: a page's domain resolves to `127.0.0.1` | `Host` must be the loopback name and the port | `Host: evil.example:47631` → 421 |
| A "simple" request (`text/plain`, no custom header) reaches the handler | Custom header required; JSON content type required | Missing token → 401; `text/plain` → 415 |
| Local Network Access / Private Network Access preflight | Answer the PNA header only for the allowed origin | Preflight with PNA request returns the header only for the allowed origin |
| Another local program takes port 47631 before the agent starts | The agent refuses to start on a taken port. A page sends the code only after `GET /v1/status` answers with the agent's name and the user clicks **Pair**. | Port in use → exit code is non-zero (WP30). **Residual:** a program that wins the port before the agent starts and the user clicks Pair can take the code. The code is single-use and five minutes long, and the exchange is with Fly. |
| Another local program reads the agent token | File `0600` / user-only ACL; hash-only in the Web-side store | Not testable in CI. **Residual:** a program running as the same user can read the file. The OS keychain is the next step (open item). |
| The agent is used to reach its own listener or a loopback service | The target is refused when it is the agent's own port on loopback | Self-target refused (WP30) |
| A malicious shared collection points a request at `http://192.168.x.x` on the user's LAN, and the user sends it | **Accepted for now.** `AllowPrivate` defaults to `true` (owner decision). The request line is shown before send. | **Residual, decision needed:** a per-host confirmation on first use (open item). |
| A team member's collection sends the environment's secret to a host the member controls | The same member can already read the secret (ADR-0006 "resolve is not a confidentiality boundary against workspace members"). The agent makes the same secret reachable from the user's network, not only from the server. | **Residual.** Recorded with ADR-0006. |
| A revoked or removed member keeps an agent working | Revocation is per agent, from the Web. A removed membership in authservice does not revoke an agent token, because the token carries no membership check. | **Residual.** The owner decides whether removal should revoke the user's agents (open item). |
| A leaked agent token | Expiry 90 days, revocation per agent, hash at rest, no use on any non-agent route | Revoked token → 401 on resolve (WP30) |

## Alternatives for the open trade-offs

- **Per-host confirmation on first use** for private targets: stronger against shared collections, one extra click. Not chosen as
  the default because the owner asked for the local target to work without friction. It is an owner decision.
- **Keyring storage for the agent token** (macOS Keychain, Windows Credential Manager, libsecret): better than a file, more code
  per platform. The first build uses the `0600` file.

## Consequences

- A user without an agent can still send to public URLs through the server. A local target needs the agent. Whether the server
  path should be disabled for any request that carries a secret is an owner decision (open item).
- Secrets reach users' computers. Anyone who can read a secret already can on the server (ADR-0006), but a secret now also sits in
  the memory of a process on a laptop, for the length of one send.
- The Web needs JavaScript it did not need before (Decision 9), and its CSP changes by two origins (WP31).
- Chrome's Local Network Access permission may show a prompt on first use. Safari's handling of an HTTPS page calling
  `http://127.0.0.1` is not verified. Both are checked in WP32 where a browser allows it, and recorded as not verified where not.
- Fly costs are per app. A service on the synchronous path of a send (EnvironmentsService, for the agent's resolve) needs
  `min_machines_running = 1` (P7), so the estate has a fixed monthly cost. The cost reasoning is in WP33.
- Distribution of the agent to non-developers is not decided. WP30 builds artifacts in CI, which a non-developer cannot download
  without a GitHub account. A release channel is an owner decision (open item). It needs a tag or a release step, which this ADR
  does not allow in the Fly workflow.

## Decisions recorded by WP30 (2026-11-01)

- **Origin list before pairing.** The first pairing request is itself a browser request, so the agent needs its allowed origins before it has
  any pairing. They are set by `postmanclone-agent setup --web-origin …`, which the operator runs once. The server's `allowedWebOrigins` then
  replaces them at pairing. Decision 5 said the list comes from the server at pairing; the first list necessarily comes from setup.
- **Server URL.** The agent calls EnvironmentsService at a configured public URL (`setup --server`). Fly's private network does not reach a
  user's computer; WP33 decides how the URL is published.
- **Terminal pairing.** `postmanclone-agent pair <code>` exchanges the code like the page does, and prints a local pairing token once, for a
  page that is linked by hand. The Web does not need it.
- **Owner decisions** (OPEN-ITEMS §0): secrets in memory only; `AllowPrivate` default true; the agent token in a `0600` file; the OS keychain
  later; membership-removal revocation deferred; distribution through CI artifacts for now.
- **Sending library.** The sender moved to `src/PostmanClone.Sending`, shared with the agent. See DEVIATIONS (2026-11-01, P2).

## Not verified here

- The `aurelius-promptus` **Companion** pattern (`docs/COMPANION_AND_WORKFLOWS.md`) was not read in WP29 or WP30: the repository
  could not be attached (WP30: the access request was denied by the session's permission classifier). The pairing design above comes from P5, the estate's rules and this repository. The owner should compare
  it with the Companion pattern before WP30 starts.
- Nothing in this ADR has been run. There is no Docker daemon here, no Fly account, and no browser test of the agent.

## Revisit when

- The owner decides on per-host confirmation, on keyring storage, on the release channel, or on membership-driven revocation.
- A browser stops treating `http://127.0.0.1` as a potentially trustworthy origin for an HTTPS page.
- Fly's per-app cost of the synchronous path becomes too high, and a different topology is needed.
