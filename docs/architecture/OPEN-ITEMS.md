# Open items register

The register of what is not finished, not verified, or known to be wrong on `main` as of WP27. It follows the
readiness-ledger format of [SECURITY-REVIEW §3](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/SECURITY-REVIEW.md):
one row per item, a status (`OPEN`, `FIXED`, `NOT APPLICABLE`), a priority with a time window, the evidence
location, and the action. Security findings also carry an attack scenario, because a finding without one is a style note.

Priorities: **P1** immediately · **P2** within a week · **P3** within two weeks · **P4** long-term.

Evidence was re-checked on the merged tree (after WP18b, WP19, WP20, WP23 and WP25). Each row says when it was
last checked. Rows that a later work package closes are kept with `FIXED` and the PR that closed them, until the
final review deletes them.

## 1. Blocks deployment

| ID | Pri | Status | Location | Issue | Context: what exists | Action |
|---|---|---|---|---|---|---|
| OI-02 | P2 | OPEN | `ServiceDefaults/RateLimiting/RateLimitingExtensions.cs:39` (policy `auth`, 10 per minute per IP); no production endpoint uses it (`grep`) | The sign-in, register, invitation and accept forms are not rate-limited by this application. | The policy and its fixed-window limiter exist and are tested (`RateLimitingTests.cs:85`). The Web does not call `AddRateLimitingDefaults` and no Web endpoint calls `RequireRateLimiting`. | Register the kernel limiter in the Web, apply `RequireRateLimiting(RateLimitPolicies.Auth)` to the form endpoints in `Auth/AuthEndpoints.cs` and `Membership/MembershipEndpoints.cs`, and test the 429 body. authservice's own limits were not inspected. |

**Attack scenario (OI-02).** An attacker holding a list of addresses posts `/sign-in` with a list of passwords at the rate the
network allows, one request per guess. Nothing in this application slows the attempts down. Impact: a weak password is found.
Recommendation: the change above. Better: a per-account lockout or delay in authservice, which owns the accounts (not inspected here).

## 2. Should be done before deployment

| ID | Pri | Status | Location | Issue | Context | Action |
|---|---|---|---|---|---|---|
| OI-03 | P3 | OPEN | `docs/PRIVACY.md` §5 | Deleting an account in authservice does not remove this system's rows that refer to the user's id, and nothing consumes authservice's deletion. | authservice soft-deletes and schedules permanent deletion (`UserCleanupService`, source read in v0.3.4). The services hold `CreatedBy` and `UserId` only. | An owner decision on what to remove or keep for a workspace, then an erasure job. |
| OI-04 | P2 | OPEN | `src/PostmanClone.Web/Components/Pages/Legal/Privacy.razor`, `Terms.razor` (each one `PlaceholderPage`) | `/legal/privacy` and `/legal/terms` are placeholders, so no notice is published. | The routes exist and are linked from the register page. | Owner: supply the text. Engineering: render it as a page. |
| OI-05 | P2 | OPEN | `scripts/hooks/pre-commit`; `.github/workflows/secret-scan.yml` (gitleaks v8.19.0) | gitleaks v8.19.0, built from its module source or from the release binary, fails the hook's `git --staged` form with `flag accessed but not defined: source`, so the hook refuses every commit. The hook fails closed, as designed. | Seen on this branch (2026-10-31). gitleaks latest (`go install …/v8@latest`) passes the hook and refuses a dummy secret. The CI job uses the Docker image with `detect`, not `git --staged`. | Document a version that passes the hook for developers, in its own PR. Verified here: the latest module release passes the hook and refuses a dummy secret. Do not change the hook's checks. |
| OI-08 | P3 | OPEN | `ExecutionService/Sending/Execution/RequestExecutor.cs:192-194` | The sender builds a plain `HttpClient` without the kernel's resilience handler, so it never retries. This is deliberate (a user's request is not resent silently), but ADR-0014 does not say so. | The kernel now adds retries only for safe methods (`Extensions.cs`, `DisableForUnsafeHttpMethods`), so an ordinary service client is safe; the sender is the exception. | Recorded in `DEVIATIONS.md` (2026-10-31). An ADR-0014 addendum would make the reason findable. |
| OI-09 | P3 | OPEN | `EnvironmentsService/Environments/EnvironmentLimits.cs:10` (50 per workspace) | The 50-environment limit is checked, then inserted, with no database constraint. Two concurrent creates at 49 can both succeed. | Reported in the WP09 PR (#10); the collections limit is taken under a workspace lock (WP08) and could be reused. | Take the same workspace lock, or add a constraint. Not re-tested here. |
| OI-12 | P4 | OPEN | `ExecutionService/Sending/Execution/ResponseBody.cs` | A secret cut by the 5 MB read cap is not masked, so the kept prefix is visible. | The masker works on what was read. | Mask the last `secret-length - 1` bytes of the cap, or refuse the response when a secret is near the cap. |
| OI-13 | P2 | OPEN | `scripts/run-local-stack.sh` (starts authservice, CollectionsService, ExecutionService); `ExecutionService` resolves through `http://environments-service` | EnvironmentsService and the Web are not started by the no-Docker script. A send with `environmentId` answers `502 upstream_unavailable` locally. | Seen on the live stack (2026-10-31): collection create 201, loopback `blocked`, `environmentId` 502, history 200. | WP26 extends the script (in progress or planned). |
| OI-14 | P3 | OPEN | `src/PostmanClone.AppHost/Resources/*.cs` and `scripts/run-local-stack.sh` | The `Auth__*`, `ConnectionStrings__*` and key wiring exists in two places with no check that they agree. | `docs/OPERATIONS.md` §1 names the authority. | A test or a script that reads both and compares the variable names. |
| OI-15 | P2 | OPEN | `.github/workflows/pr-validation.yml` | No E2E job yet. The browser flows are not in CI. | WP26 (E2E) is planned. | WP26. |
| OI-18 | P3 | OPEN | `ServiceDefaults/Telemetry/OpenTelemetryExtensions.cs:43` | The OTLP exporter branch has no test and was never run against a collector. | The exporter is registered only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. | A test with a stub collector. |
| OI-19 | P3 | OPEN | `src/PostmanClone.AppHost/` and the four Dockerfiles | The AppHost has never been run, and no Docker image has been built in a session. | Sessions have no Docker daemon. CI's `docker-build` builds the four images. | An owner run of the AppHost once, with Docker, recorded here. |
| OI-20 | P4 | OPEN | Web security headers (`ServiceDefaults/Security/SecurityExtensions.cs`) | No `Strict-Transport-Security`. | Right for the plain-HTTP local stack. A TLS edge decides it. | Owner: set HSTS at the TLS edge when deployed. |
| OI-24 | P3 | OPEN | `Components/Builder`, `Components/Response`, `Api/ExecutionClient.cs` | The Web's send path (builder → `POST /api/v1/execute` → response viewer) was not exercised in a browser in this session. | WP20's tests use a fake handler (bUnit). The sender was exercised with curl. | An E2E journey (WP26, journey 4). |

## 3. Fixed (kept until the final review)

| ID | Pri | Status | Location | Issue | Closed by |
|---|---|---|---|---|---|
| OI-01 | P1 | FIXED | `ServiceDefaults/Security/SecurityExtensions.cs:16` (`Referrer-Policy: same-origin`) | Browser sign-in POST was refused with 403: the browser sent `Origin: null` under `no-referrer`, and the origin guard refused it. | WP18b (#25), ADR-0022. **Verified 2026-10-31** in headless Chromium: the sign-in POST returns 200 and lands on `/no-workspace` (before: 403, `/sign-in`). |
| OI-07 | P4 | FIXED | `Components/Shared/StatusStrip.razor` | The status strip read a disposed cancellation token and logged `ObjectDisposedException`. | WP18b (#25): a `_disposed` guard and the cancellation cancelled in `Dispose`. Read in code; not re-run. |
| OI-11 | P3 | FIXED | `AGENTS.md` | AGENTS.md said the SSRF guard was "not yet wired to a route". It is wired (`SsrfHandlerFactory` in `RequestExecutor`). | WP27 (this PR). |

## 4. Other open rows (verification and housekeeping)

| ID | Pri | Status | Location | Issue | Action |
|---|---|---|---|---|---|
| OI-06 | P3 | OPEN | `ServiceDefaults.Tests/Telemetry/TelemetryTests.cs` | `Requests_are_traced_but_health_probes_are_filtered_out_of_traces` passes in full Debug and full Release runs, and fails when run alone in Release (`Assert.Contains() Failure: Filter not matched in collection`, empty collection). Reproduced on 2026-10-31 on the merged tree. Predates WP25 (PR #21). | Make the test wait for the span to be exported, or assert on what it can see deterministically. Not fixed here: it is a test change, out of WP27 scope. |
| OI-10 | P3 | OPEN | `docs/OPERATIONS.md` §4.3 | The Web started outside the AppHost needs four settings to sign in. Without them, sign-in answers `error=unavailable`. | Document in a launch profile for the no-Docker route (WP26). |
| OI-16 | P3 | OPEN | `Components/Pages/Environments.razor`, `History.razor`, `About.razor` | Placeholders. | WP21 (environments UI) and WP22 (history UI) are in progress. |
| OI-17 | P4 | OPEN | test totals | The run reports 1927 tests; the per-project discovery (`--list-tests`) sums to 1919. Eight tests are not attributed to a project in this session. | Find the eight (probably a theory or a shared fixture) and say so in TESTING.md. |
| OI-23 | P4 | OPEN | `docs/AUTH.md` | The invitation accept branch "already a member" was never observed live (authservice does not allow a second open invitation). | Covered by a fake only. Revisit when authservice allows it. |
| OI-25 | P4 | OPEN | `Membership`, `Auth` | `ServerSessionStore` and `OpenTabs` live in the Web's memory; a restart signs everyone out. | Accepted by ADR-0008 (one instance, sessions in memory). Residual risk, listed. |

## 5. Owner actions

Collected from the merged PR bodies and this work. None of them is done by a session.

- Branch protection on `main`: require the `pr-validation` check (and `secret-scan`, if wanted).
- GitHub secret-scanning push protection, Dependabot alerts, and CodeQL if wanted (PR #2).
- Confirm the copyright holder in `LICENSE` (PR #2).
- Make `ghcr.io/konradcinkusz/authservice:v0.3.4` pullable by CI and developers if the package is private (PR #4).
- Decide OI-02 (the rate-limit owner) and OI-03 (erasure); supply the text for OI-04.
- Decide the production key and secret custody (ADR-0007).
- Decide OI-06: fix the test or accept the flake.
- Delete the merged remote branches, which sessions cannot delete (PR #3 onward):
  `origin/wp/00-baseline`, `wp/01-kernel-core`, `wp/02-kernel-security`, `wp/03-apphost-authservice`, `wp/04-collections-skeleton`,
  `wp/05-environments-skeleton`, `wp/06-execution-skeleton`, `wp/07-web-skeleton`, `wp/08-collections-api`, `wp/09-environments-api`,
  `wp/10-ssrf-guard`, `wp/11-variables-assembler`, `wp/12-web-auth`, `wp/13-requests-api`, `wp/14-sender`, `wp/15-history`,
  `wp/16-postman-io`, `wp/17-curl-io`, `wp/18-web-shell`, `wp/19-request-builder`, `wp/23-import-export-ui`, `wp/24-workspace-members`,
  `wp/25-isolation-arch-tests`, and `claude/postman-clone-plan`. Keep `wp/27-docs-final` until this PR is merged.

## 6. Residual risks accepted for now

- The Web keeps sessions and open tabs in memory and runs as one instance (ADR-0008). A restart ends every session.
- Non-secret environment values are stored in clear (by design; ADR-0006).
- The sender sends a user's secret to the host the user names. That is the product, not a leak, but it is recorded here so it is a decision.
- The masking covers known secret values, not personal data in responses (PRIVACY.md §2.4).
