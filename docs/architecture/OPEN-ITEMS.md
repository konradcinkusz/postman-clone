# Open items register

The register of what is still open, unverified, or accepted on `main` after WP28 (the final review). It follows the
readiness-ledger format of [SECURITY-REVIEW §3](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/SECURITY-REVIEW.md):
one row per item, a status (`OPEN`, `FIXED`, `NOT APPLICABLE`), a priority with a time window, the evidence location, and the action.
Findings from the review are in [SECURITY_REVIEW.md](../SECURITY_REVIEW.md) and [ARCHITECTURE_REVIEW.md](../ARCHITECTURE_REVIEW.md);
decisions are in [ADR-0019](../decisions/0019-final-review-outcomes.md).

Priorities: **P1** immediately · **P2** within a week · **P3** within two weeks · **P4** long-term.

Rows closed by WP28 are listed in §5 with the commit that closed them, so the history stays findable.

## 0. Owner decisions and actions

Nothing in this list is done by a session. Each item says what the owner must do; none of them is a code change.

**Repository and GitHub settings (the owner, in GitHub):**

- **Branch protection on `main`:** require the `pr-validation` check (its aggregate counts the `e2e` job), and require `secret-scan` if wanted.
- **Secret scanning and push protection, Dependabot alerts, and CodeQL** if wanted (PR #2).
- **Stale remote branches to delete** (sessions cannot delete them; the merged work is on `main`):
  `origin/claude/postman-clone-plan`, and `origin/wp/00-baseline`, `wp/01-kernel-core`, `wp/02-kernel-security`, `wp/03-apphost-authservice`,
  `wp/04-collections-skeleton`, `wp/05-environments-skeleton`, `wp/06-execution-skeleton`, `wp/07-web-skeleton`, `wp/08-collections-api`,
  `wp/09-environments-api`, `wp/10-ssrf-guard`, `wp/11-variables-assembler`, `wp/12-web-auth`, `wp/13-requests-api`, `wp/14-sender`,
  `wp/15-history`, `wp/16-postman-io`, `wp/17-curl-io`, `wp/18-web-shell`, `wp/18b-signin-origin-fix`, `wp/19-request-builder`,
  `wp/20-response-viewer`, `wp/21-environments-ui`, `wp/22-history-ui`, `wp/23-import-export-ui`, `wp/24-workspace-members`,
  `wp/25-isolation-arch-tests`, `wp/26-e2e`, `wp/27-docs-final`. Delete `wp/28-final-review` after its pull request is merged.
- **Licence holder:** confirm the copyright holder in `LICENSE` (PR #2).
- **Make the `e2e` job part of the required set** with `pr-validation` (the aggregate is the required check).

**Gitleaks (OI-05):**

- The hook and the installer now name `v8.28.0`, the version verified to run the hook's `git --staged` form. The hook itself is unchanged and fail-closed.
- The CI scan still runs `ghcr.io/gitleaks/gitleaks:v8.19.0` (`secret-scan.yml`, `pr-validation.yml`). Bump it in its own pull request, with the
  image digest checked and a full-history scan after the bump. Not done here: this session has no Docker to read a digest.

**Before any deployment (the owner):**

- **OI-26 (client-IP trust):** set authservice's `ClientIpHeader` and trusted proxies for the edge, and the Web's `Proxy:TrustForwardedFor` and
  `ForwardedHeaders:KnownProxies`. Decide whether one shared sign-in window per edge address is acceptable.
- **Email verification (SR-11):** authservice requires a confirmed email only when it can send mail. Configure a sender, or decide that
  accounts may be active unverified.
- **HSTS (OI-20):** set `Strict-Transport-Security` at the TLS edge.
- **Legal text (OI-04):** supply `/legal/privacy` and `/legal/terms`.
- **Erasure, retention and anonymisation policy (OI-03, SR-14):** decide what is removed or kept for a workspace when a user is deleted, and
  for how long. Documented in PRIVACY.md §5; not decided. Then an erasure job.
- **Production key and secret custody (ADR-0007).**
- **The AppHost, once, with Docker (OI-19):** run it and record the result here.

**Deployment:** Fly.io deployment was intentionally **not** done, and no tags were pushed. ADR-0007 keeps deployment out of this build.
`flyio/` does not exist and the deploy workflows were not touched.

**Other decisions the owner should confirm:**

- Whether a Member may edit a collection another Member created (SR-19; the README roles table says yes; deletion is limited to the creator,
  an Admin or an Owner).
- Whether the sender keeps no resilience handler (ADR-0019 §8).
- OI-06 is fixed (§5); confirm that the telemetry test is the right one to accept.

## 1. Open — before deployment

| ID | Pri | Status | Location | Issue | Action |
|---|---|---|---|---|---|
| OI-26 | P2 | OPEN (owner) | authservice v0.3.4 `auth` policy; the Web's `ForwardedHeaders`, `Proxy:TrustForwardedFor` | Behind one edge address, every user shares authservice's sign-in window (20 a minute). The Web's own forms are limited per address too (SR-01). | §0, owner. |
| OI-20 | P4 | OPEN (owner) | `ServiceDefaults/Security/SecurityExtensions.cs` | No `Strict-Transport-Security`. | Set it at the TLS edge (§0). |
| OI-03 | P3 | OPEN (owner decision) | `docs/PRIVACY.md` §5 | Deleting an account in authservice does not remove this system's rows that refer to the user's id (SR-14). | §0, owner. Then an erasure job. |
| OI-04 | P2 | OPEN (owner text) | `Components/Pages/Legal/Privacy.razor`, `Terms.razor` (each a `PlaceholderPage`) | No notice is published. | Owner supplies the text; engineering renders it. |
| — | P2 | OPEN (owner) | authservice `RequireConfirmedEmail` follows `canSendEmail` | Accounts can be active without verification when no sender is configured (SR-11). | §0, owner. |

## 2. Open — engineering

| ID | Pri | Status | Location | Issue | Action |
|---|---|---|---|---|---|
| OI-05 | P2 | OPEN (CI pin) | `.github/workflows/secret-scan.yml`, `pr-validation.yml` | The CI image pin `v8.19.0` is not the version the hook drives (SR-18). Developer guidance is fixed (`v8.28.0`). | §0, owner (bump with a digest). |
| OI-09 | P3 | OPEN | `EnvironmentsService/Environments/EnvironmentLimits.cs:10` | Two concurrent creates at 49 can both succeed (AR-07). | Take the workspace lock CollectionsService takes, or add a constraint. |
| OI-14 | P3 | OPEN | `src/PostmanClone.AppHost/Resources/*.cs`, `scripts/run-local-stack.sh` | `Auth__*`, `ConnectionStrings__*` and key settings are in two places with no check that they agree (AR-06). | A test or script that reads both and compares. |
| OI-18 | P3 | OPEN | `ServiceDefaults/Telemetry/OpenTelemetryExtensions.cs` | The OTLP exporter has no test and was never run against a collector (AR-05). | A test with a stub collector. |
| OI-19 | P3 | OPEN (no Docker) | `src/PostmanClone.AppHost/`, the four Dockerfiles | The AppHost has never been run, and no image has been built in a session. | Owner run with Docker, recorded here. |
| OI-16 | P4 | OPEN | `Components/Pages/About.razor` | `/about` is a placeholder. Environments and History are no longer placeholders (WP21, WP22). | Write the page. |
| OI-17 | P4 | OPEN | test totals | The solution run reports 2067 tests on the branch. The per-project discovery total was not re-measured in WP28. | Re-measure and reconcile in TESTING.md §2. |
| OI-23 | P4 | OPEN | `docs/AUTH.md` | The invitation "already a member" branch was never observed live (authservice allows one open invitation). | Covered by a fake only. |
| SR-15 | P3 | OPEN (consider) | `Web/Components/Pages/Environments.razor` and `EnvironmentRules` | Environments have no version: the last write wins (SR-15). Documented in OPERATIONS.md; no contract change made. | Decide whether a version is wanted (a contract amendment, separately). |

## 3. Accepted residual risks

- The Web keeps sessions and open tabs in memory and runs as one instance (ADR-0008, OI-25). A restart ends every session.
- Non-secret environment values are stored in clear, by design (ADR-0006).
- The sender sends a user's secret to the host the user names. That is the product (ADR-0011).
- The masking covers known secret values, not personal data in responses (PRIVACY.md §2.4, SR-21).
- The sender has no resilience handler, so it never retries (ADR-0019 §8).
- Login and sign-up are limited per client address; per account, authservice locks an account for five minutes after five failures (upstream).
- A `v8.19.0` scan image in CI until §0 is done (SR-18).

## 4. Verification gaps (not run in this session)

- The AppHost and the Dockerfiles (OI-19).
- The browser journeys against the local stack in this session. CI's `e2e` job runs them for the head SHA; the local stack was started
  and stopped as documented in OPERATIONS.md §4.5 (see the PR body for the exact commands and results).
- `scripts/smoke-auth.sh` against authservice in this session (see the PR body).
- The OTLP exporter against a collector (OI-18).

## 5. Closed (history)

| ID | Was | Closed by |
|---|---|---|
| OI-01 | Browser sign-in refused with 403 under `Referrer-Policy: no-referrer` | WP18b (#25), ADR-0022. Verified in a browser. |
| OI-02 | Sign-in, register, invitation and accept were not rate-limited | WP28: `2908fc3` (Web limiter, configurable window, telemetry), `6ad2d70` (invitations and creation). Test `AuthFormRateLimitTests`. |
| OI-06 | `TelemetryTests` flaky in Release | WP28: the test waits for the span it asserts on (`TelemetryTests.cs`). Three Release runs alone passed. |
| OI-07 | Status strip read a disposed token | WP18b (#25). |
| OI-08 | Sender's lack of retries was undocumented | WP28: ADR-0019 §8; DEVIATIONS.md. |
| OI-10 | Web started outside the AppHost needed four settings | WP26 (`run-local-stack.sh`). |
| OI-11 | AGENTS.md said the SSRF guard was not wired | WP27 (#27). |
| OI-12 | Secret cut by the 5 MB read cap was not masked | WP28: `df4b7b9` (`TrimPartialSecretAtEnd`, UTF-8 tail). Tests in `ResponseBodyTests`. |
| OI-13 | No EnvironmentsService or Web in the no-Docker stack | WP26 (#30). |
| OI-15 | Browser flows not in CI | WP26 (#30), job `e2e`. |
| OI-16 (part) | Environments and History placeholders | WP21 (#29), WP22 (#28). |
| OI-24 | The send path was not exercised in a browser | WP26 (#30). 16 of 16 loopback journeys passed on the sandbox (2026-10-31). |
| OI-26 (code part) | The suite's own budget was not tested | WP28: `AuthBudgetWindowTests` on a fake clock (`aa29143`). The production issue stays open (§1). |
| OI-27 | Web answered empty static files when started from the wrong folder | WP28: `08f584a` (startup manifest check). The empty response was not reproduced; the check covers the symptom. |
| — | SR-01 (SSRF relaxations allowed outside Production) | WP28: `aa29143`. Now refused outside Development. |
| — | SR-02 (sender targets traced) | WP28: `2908fc3`. Deviation recorded (P15). |
| — | SR-04 (body cut mid-character sent as base64 unmasked) | WP28: `df4b7b9`. |
| — | SR-07 (Teredo and 6to4 relay not blocked) | WP28: `df4b7b9`. |
| — | Register page did not show authservice's password rule | WP28: `aa29143`. |
| — | Web had no health check in the AppHost | WP28 (AR-01). AppHost not run. |

## 6. Residual risks (see SECURITY_REVIEW.md §6)

Listed above in §3, with the same content.
