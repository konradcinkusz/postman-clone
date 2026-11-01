# ADR-0027: Agent threat model and sending rules

- **Status:** Accepted (owner decisions recorded in OPEN-ITEMS §0, 2026-11-01)
- **Date:** 2026-11-01
- **Work package:** WP30 (the agent and the shared sending library); the Web side is WP31 (ADR-0028)

## Context

ADR-0026 moves sending to the user's computer. The agent then runs a listener that a web page can reach, and it sends requests to
addresses the server never would (localhost, the LAN). ADR-0026 gives the decision and a short threat table. This ADR records the threat
model in full, the sending rules that hold for every agent send, and the owner decisions that bound them.

## Options considered

1. **One sender for both, with the guard as configuration.** Chosen. The assembler, masking, caps and executor are one library
   (`src/PostmanClone.Sending`). ExecutionService wires the SSRF rules; the agent wires its own rules. A result is the same whichever sender
   answered.
2. **A copy of the sender in the agent.** Rejected. Masking is a security control, and two copies drift.
3. **Run the server's guard in the agent.** Rejected as the default: it refuses localhost and the LAN, which is the point of the agent. It
   remains available through `AllowPrivate=false`.
4. **Authenticate the page with a cookie or a session.** Rejected. A browser sends no cookie for a request to `127.0.0.1` from another site
   that the agent could trust, and the agent has no user session. The page holds a pairing token instead.
5. **An unauthenticated listener that trusts `Origin` alone.** Rejected. A non-browser program, and a browser that omits or forges the header in
   some cases, must not reach the send.

## Decision

**Listener.** Bound to `127.0.0.1` only. A taken port stops the agent (exit 1). Every request is checked in this order:

1. `Host` is `127.0.0.1:<port>` or `localhost:<port>`, else `421 host_not_allowed`. This defeats DNS rebinding.
2. `Origin` is present and equals one configured origin exactly, else `403 origin_not_allowed`. An absent or `null` origin is refused.
3. Only then are CORS headers written, and only for that origin: the exact origin, `Vary: Origin`, never `*`, never credentials.
4. `OPTIONS` answers `204`, with `Access-Control-Allow-Private-Network: true` only when the request asks for it and the origin is allowed.

**Send.** `POST /v1/send` needs `Content-Type: application/json` (else `415`), the `X-Agent-Pairing-Token` header, compared with the stored hash in
constant time (else `401 agent_token_invalid`), and a body of at most 2 MB (else `413`). No pairing answers `409 agent_not_paired`.

**Sending rules, the same for every agent send.** The contract caps (CONTRACTS §4) apply through `SendingLimits.Contract`. The refusals that hold
whatever `AllowPrivate` says: a scheme other than `http` or `https`, user information in the URL, and the agent's own listener (host and port),
each with `outcome: blocked`. With `AllowPrivate` true (the default) loopback, private and link-local targets are reached. With it false, the
guard's address ranges apply, and a loopback target is `ssrf_blocked_address`.

**Secrets.** The agent fetches a variable set with its agent token for one send, holds the values in memory for that send, and writes them
nowhere. It masks them in the result (the shared masker). The browser never receives the agent token, and the pairing token is held by the
browser only in circuit memory (ADR-0028).

**Credentials on disk.** The config file holds the agent token and the hash of the pairing token, never the pairing token itself. The file is
written atomically with mode `0600` on Unix (its folder `0700`). On Windows the profile ACL applies; a stricter ACL is open.

**Logging.** Method, host, status, sizes and durations only. No header value, body, URL path, token or variable value.

**Pairing.** The one-time code is single-use, five minutes, hashed. An unknown, used or expired code gives the same `400`. A new pairing replaces
the local pairing token.

## Threat model

| Threat | Control | Test |
|---|---|---|
| A page on another site calls the listener | Origin allow-list, exact echo, no `*` | `ListenerSecurityTests` (foreign, null, prefix, port variant, absent) |
| DNS rebinding | Host must be the loopback name and port | `A_rebinding_host_is_refused_even_with_an_allowed_origin` |
| A "simple" request reaches the handler | Custom header required, JSON only | missing or wrong token, `text/plain` 415 |
| Private Network Access preflight | Header only for the allowed origin | `A_preflight_with_private_network...` |
| Another program takes the port | Refuses to start on a taken port | `A_second_agent_on_the_same_port_exits_non_zero` |
| The agent reaches its own listener | Own host and port refused | `A_send_to_the_agents_own_listener...`, `The_name_localhost...` |
| A response carries the secret | Shared masker; no value in any output | `A_send_to_a_local_target_delivers_the_secret...` |
| A log carries the secret | Log lines name ids, codes and sizes only | the same test, log capture |
| A leaked agent token | Expiry (90 days), revocation, hash at rest, no use on user routes | `Revoking_an_agent_stops_its_token_at_once`, audience tests |
| A malicious shared collection reaches the LAN | **Residual.** `AllowPrivate` defaults to true (owner decision) | `By_default_a_loopback_target_is_reached` documents the default |
| A program running as the same user reads the token file | **Residual.** Mode `0600` only; the keychain is an open item | Unix mode test |
| A removed member keeps an agent | **Residual.** Revocation on membership removal deferred (owner decision) | none |

## Consequences

- Secrets are in one more process's memory for the length of a send. That is the accepted trade-off (ADR-0026 Decision 11).
- The listener is the only attack surface on the user's machine. It is small: three routes and one guard.
- A browser on another origin cannot use the agent at all, and the server's list is the one the page must match.

## Revisit when

- The owner decides per-host confirmation on first use, the keychain, the release channel, or membership-driven revocation.
- A browser changes how it treats `http://127.0.0.1` from an HTTPS page, or how it asks for Local Network Access.
- A second listener process or a second pairing scheme is proposed.
