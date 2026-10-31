# ADR-0011: Sender threat model and the SSRF guard

- **Status:** Accepted
- **Date:** 2026-10-30
- **Work package:** WP10

## Context

ExecutionService sends a user's HTTP request from the server. That is the only outbound network capability in
the product ([ADR-0003](0003-service-boundaries.md)). The URL, the redirect chain and the response all come from
the user or from a third party, so the server can be made to request an address the user could not reach: the
loopback interface, the private network, the cloud metadata address, or a service on a port that speaks a
different protocol (SSRF). The plan's §4 lists the required controls: scheme allow-list, DNS resolved by us, every
address checked, the connection pinned to the checked address (rebinding), redirects followed by us and
re-validated, no ambient proxy, no cookies.

## Options considered

1. **Check the URL only, before sending.** Rejected as the only layer. The URL says `example.com`, the resolver
   says `127.0.0.1`, and a second lookup can answer differently (DNS rebinding). A URL-level check cannot see the
   address that will be used.
2. **Check the resolved address, then let the HTTP stack resolve the name again.** Rejected. The check and the
   connection would each resolve the name, so a second answer could differ from the one that was checked.
3. **Resolve the name once inside the connection callback, check every answer, and dial only a checked address.**
   Chosen. The HTTP stack's `ConnectCallback` runs for every new connection with the endpoint it is about to use.
   The guard resolves that name itself, refuses the whole connection if any answer is blocked, and dials only the
   checked addresses, in the order returned. The address checked is the address dialled.
4. **Parse the URL host with the framework's `Uri`.** Rejected as the sole parser. `System.Uri` normalises
   `http://0x7f.1/` to 127.0.0.1 itself, so a host that the guard reads one way and the stack reads another is a
   bypass. The guard therefore reads literal hosts with its own WHATWG-compatible parser, and sends the canonical URL
   it built, so the text sent and the text checked agree.

## Decision

The sender's outbound layer is `Sending/Ssrf/` in ExecutionService, with these parts:

- **`TargetValidator`** (before sending): scheme `http` or `https` only; no userinfo; a port of 1 to 65535 that is
  not in `Execution:Ssrf:BlockedPorts` (default `22,25,110,143,465,587,993,995,3306,5432,6379,9200,27017`); the
  host read as WHATWG does, so tabs and line breaks are dropped, a backslash is a slash, the host is
  percent-decoded, IDN is punycoded, a trailing dot is dropped, and every IPv4 spelling (decimal, octal, hex, short
  forms) is one address. A literal address is checked against `IpRangePolicy`. A name is checked for shape and for
  `localhost` / `*.localhost`. The result is the canonical URL to send.
- **`GuardedConnector`** (at connect, authoritative): the port check; then, for a name, one resolution; every
  answer is checked against `IpRangePolicy`, and any blocked answer refuses the connection; then dial each checked
  address in order. A second resolution is never made within one connection.
- **`IpRangePolicy`**: the block list in the plan (IPv4 and IPv6). Loopback is lifted only by
  `Execution:Ssrf:AllowLoopback`; the RFC 1918, link-local and unique-local ranges only by
  `Execution:Ssrf:AllowPrivateRanges`; the rest are never lifted. IPv4 addresses carried inside IPv6
  (IPv4-mapped, IPv4-compatible, NAT64 `64:ff9b::/96`, 6to4 `2002::/16`) are judged on the IPv4 address they carry.
  Zone ids are refused. Both flags are refused in Production (`ExecutionOptionsValidator`).
- **`RedirectPlanner`**: the sender follows redirects, not the HTTP stack. Each hop is validated again; the chain is
  capped at `MaxRedirects`; an https request redirected to http is refused (scheme downgrade); a 303 becomes GET,
  and a 301 or 302 from POST becomes GET (the body is dropped whenever the method changes); 307 and 308 keep both. A
  cross-origin hop (different scheme, host or port) drops the credential headers: `Authorization`,
  `Proxy-Authorization`, `Cookie`, `X-Api-Key` and `X-Auth-Token` (the set CONTRACTS §4 masks).
- **`SsrfHandlerFactory`**: the one `SocketsHttpHandler` for outbound requests, with no proxy (an environment
  variable cannot route around the checks), no cookie jar, no automatic redirects, and no automatic decompression.

The refusal carries an error code from CONTRACTS §4 (`ssrf_blocked_address`, `ssrf_blocked_scheme`,
`ssrf_blocked_port`, `dns_failed`, `too_many_redirects`, `invalid_url`) and a category (`loopback`, `private`,
`link_local`, and so on). It never carries the address or the URL, so a response cannot be used to map the
internal network.

## Threat model

**Protected**

- A URL that names an internal address in any spelling: decimal, octal, hex, short, IPv4-mapped IPv6, NAT64, 6to4,
  bracketed or unbracketed IPv6, percent-encoded, with tabs or line breaks, with a backslash, with a trailing dot.
- A name that resolves to an internal address, including a name that answers with one public and one private
  address (the whole connection is refused), and including a name that changes its answer on the next lookup (the
  connection uses only the address it checked, and the next connection is checked afresh).
- A redirect that leaves the target: to another internal address, to another scheme, or to a blocked port.
- Credentials sent to the user's target being forwarded to a different origin by a redirect.
- Traffic routed through an environment proxy, or cookies set by a response and sent on a later request.

**Not protected, and why**

- **A connection that is kept alive and reused.** The guard checks at connection time. A later request that reuses
  an open connection goes to the address that was checked when the connection opened. This is the intended pooling
  behaviour, and it is safe for the check, but a name whose answer changes during a long-lived connection is not
  re-checked until that connection closes.
- **The time between the check and the dial.** Within one connection the guard resolves, checks and dials in
  sequence. A different answer on a later lookup cannot reach the dial, but the operating system's own resolver
  cache is outside the guard's view.
- **Teredo (`2001::/32`) and other tunnelling forms** not listed in the plan are not blocked. They embed an address
  the guard does not evaluate. A deployment should block them at the network boundary.
- **A name-level blocklist beyond `localhost`.** `metadata.google.internal` is refused because it resolves into the
  link-local range, not because its name is listed. A name that resolves to a public address is allowed.
- **`AllowPrivateRanges` includes the link-local range** (`169.254.0.0/16`), which holds the cloud metadata address.
  That is deliberate for the loopback test stack only, and the service refuses to start with it set in Production.
- **TLS** is checked against the name in the request, not the address dialled. The guard changes where the bytes go,
  not what the certificate must say.

**Owner note.** The guard is the application's check. The real backstop is the network egress policy of the place
the service runs: the sender's outbound rules should refuse the same ranges and the metadata address. Deployment is
out of scope ([ADR-0007](0007-deployment-out-of-scope.md)), so this is recorded here for the owner, not configured.

## Consequences

- The sender must use `SsrfHandlerFactory.Create`, and must send `SsrfTarget.Uri`, not the text the user typed. WP14
  wires the sender to these types; until then no route reaches them.
- The sender client does not add the kernel's resilience handler. A retry of a user's request is a second request
  to a third party, which is the sender's decision and not the kernel's.
- Decimal-literal and name-based cases are covered by table-driven tests in `tests/PostmanClone.ExecutionService.Tests/Sending/Ssrf/`.
  The one test that opens a socket uses only the loopback listener it starts, and it runs with the flag off and on.
- Not verified here: no real DNS rebinding and no real public address (the sessions have no network access by
  design). The rebinding behaviour is tested with a scripted resolver and a recording dialer.

## Revisit when

- The sender gains a second outbound path (for example OAuth or webhooks), which must go through the same guard.
- The plan adds a DNS-level control such as a resolver that refuses private answers; the guard would then be a
  second layer, not the only one.
- A bypass is found in the URL parser: fix it here, with a test in `TargetValidatorTests`, and record it as a new
  ADR that supersedes this one's parsing section.
