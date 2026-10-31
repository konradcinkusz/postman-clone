# ADR-0019: Final review outcomes

- **Status:** Accepted
- **Date:** 2026-10-31
- **Work package:** WP28 (final review)
- **Reviews:** [SECURITY_REVIEW.md](../SECURITY_REVIEW.md) (findings, severity, status) and
  [ARCHITECTURE_REVIEW.md](../ARCHITECTURE_REVIEW.md) (principles and the compliance checklist)

## Context

WP28 is the last work package. It reviews the whole tree against the repository's own decisions
(ADR-0001 to ADR-0025), against the reference architecture (P1–P15 and its compliance checklist) and against
the security guide (`SECURITY-REVIEW`: finding format, readiness ledger, recurring launch blockers). It must
leave no open blocking finding. Some findings cannot be closed in code, because the decision belongs to the
owner or the fix lives in a repository this session does not own. Those are recorded here, not decided.

## Options and decisions

1. **Where the sender's outbound requests are traced.** The HTTP client instrumentation records the URL of
   every outbound request. For the sender, that URL is the user's target, and a variable can put a secret in
   the path. *Decision:* the sender marks its requests (`OutboundTelemetry.UserTarget`), and the tracing filter
   drops them. Other clients (EnvironmentsService, the Web's calls) keep their spans. This is a deliberate
   departure from "trace every outbound call" (P15); it is recorded in DEVIATIONS.md.
2. **Where the SSRF relaxations may be set.** They were refused only for an environment named `Production`.
   *Decision:* they are accepted only in `Development`. Any other name, Staging included, refuses them at
   startup (ADR-0011, updated here by addendum). The local loopback stack already runs the sender as
   Development, so the browser journeys are unaffected.
3. **Rate limits on the account forms (OI-02).** *Decision:* the Web registers the kernel's limiter. Sign-in,
   register and accept-invitation share the `auth` window per client address; invitations and workspace
   creation use the per-user `api` window. The `auth` limit defaults to 10 a minute and is configurable
   (`RateLimiting:AuthPermitsPerMinute`). The local stack and the browser-journey job set 18, because the
   suite signs in from one address and authservice's own budget is 20 a minute. authservice also locks an
   account for five minutes after five failed sign-ins; that is an upstream control and is recorded, not
   duplicated.
4. **Partial secrets in a body cut at the read cap (OI-12).** *Decision:* the incomplete UTF-8 tail (at most
   three bytes) is set aside before decoding, and the longest tail that is the start of a secret is removed
   before masking. Without the first change, a cut inside a multi-byte character sent the whole body as base64,
   which is not masked.
5. **Static assets (OI-27).** *Decision:* the Web checks a publish manifest against the files on disk and
   refuses to start when a file is missing or the wrong size. The empty-response mechanism named in OI-27 was
   not reproduced in four configurations; the check covers the symptom the register describes.
6. **Telemetry in tests (OI-06).** *Decision:* the test waits, bounded, for the span it asserts on. The
   production code is unchanged.
7. **gitleaks (OI-05).** *Decision:* the hook and the installer name `v8.28.0` as the version that runs the
   hook's `git --staged` form. The hook is not changed, and it stays fail-closed. The CI scan image stays at
   `v8.19.0`: changing it needs an image digest, and this session has no Docker to read one. Changing the pin is
   an owner decision (see OPEN-ITEMS.md §0).
8. **Retries in the sender (OI-08).** The sender builds its own handler with no resilience handler, so a
   user's request is never resent. *Decision:* this stays. ADR-0014 did not say why; this record does. A POST
   retried by a library would reach a third party twice.

## Consequences

- The services' trace output no longer contains a user's target URL. Operators who want it in a trace must
  decide that explicitly; it is not an option in this build.
- A deployment that is not Development cannot relax the SSRF guard, even by mistake of environment name.
- The Web's sign-in limit is a per-address window. Behind one proxy address, all users share it until
  `Proxy:TrustForwardedFor` and authservice's client-IP trust are configured (OI-26). This is a deployment
  precondition, not fixed here: authservice is a different repository.
- The telemetry filter and the body trim were checked by removing them: their tests failed without the change.
  The other changes have tests that pass on the change; they were not checked by removing it.

## Owner decisions (not made here)

- Erasure and retention policy for authorship and history rows (OI-03, PRIVACY.md §5).
- Whether email verification is required when no email sender is configured (authservice
  `RequireConfirmedEmail` follows `canSendEmail`). Without it, accounts are active unverified.
- The client-IP trust for the edge before any deployment (OI-26), and whether one shared sign-in window per edge
  address is acceptable.
- The CI scan image pin (`v8.19.0` in `secret-scan.yml` and `pr-validation.yml`): bump it with a verified digest,
  in its own pull request.
- The legal text for `/legal/privacy` and `/legal/terms` (OI-04).
- Whether a Member may edit a collection another Member created. The product rule allows it; deletion is limited
  to the creator, an Admin or an Owner (README roles table).

## Revisit when

- The first deployment target is chosen (ADR-0007 is superseded then).
- authservice changes its client-IP trust, its password rule or its verification default.
- The OTLP exporter is first run against a collector (OI-18).

## Addenda

- **ADR-0011 (SSRF guard):** the relaxations are accepted only in Development (decision 2 above). The ADR's
  "refuses in Production" wording is superseded by this rule.
- **ADR-0014 (execution limits):** the sender's lack of a resilience handler is intentional (decision 8).
