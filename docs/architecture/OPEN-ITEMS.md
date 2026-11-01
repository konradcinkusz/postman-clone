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

**Deployment:** Fly.io deployment was intentionally **not** done, and no tags were pushed, for the build that ended at WP28 (ADR-0007). That
decision is superseded for this product by ADR-0026 (WP29). The manual workflow and the configs are written (WP33, ADR-0029). `flyio/` holds them.
The owner must add `FLY_API_TOKEN` and the production values (docs/FLY_DEPLOYMENT.md §2 and §5); nothing is deployed without them.
The deployment's own open values: the region, the app names, `AllowedOrigins__0` and `FrontendBaseUrl` after a custom domain, and
`Agent:AllowedWebOrigins` from WP30.

**Local agent (ADR-0026, ADR-0027). Decided by the owner on 2026-11-01 (WP30):**

- **Secrets reach the user's computer during a send.** Accepted. They are held in the agent's memory for one send, never written to disk, and
  masked in every response, log line and error (ADR-0026 Decision 11, ADR-0027).
- **Private and loopback targets are allowed by default** (`Agent:Targets:AllowPrivate=true`, the agent's setting `allowPrivate`). The flag
  exists and is documented in `docs/AGENT.md`; setting it to `false` turns the SSRF ranges back on. The per-host confirmation on first use is
  not built.
- **The agent token is stored in a `0600` file** in the user's configuration folder. The OS keychain is a later item (not built).
- **Revocation on membership removal is deferred.** A removed member's agent keeps working until it is revoked or expires (90 days). Listed as
  an open item: the owner decides the mechanism (an authservice event, or a membership check at each resolve).
- **Distribution is through CI artifacts for now** (`agent-artifacts` in `pr-validation.yml`: the dotnet tool package and single-file zips for
  win-x64, linux-x64 and osx-arm64). No release and no tag. A release channel for non-developers is still undecided.

**Web agent client (ADR-0028), WP31:**

- **HTTPS page calling the loopback agent is not verified** in Safari or Firefox, and no HTTPS origin ran in WP31. Chromium was run on `http://localhost`.
  Local Network Access and Private Network Access prompts were not observed (headless). Check on a real desktop before the first user.
- **Pairing is per page visit, and each pairing adds an agent row (OI-31, owner decision).** The page holds its token in memory, so a reload needs
  a new code, and the workspace's list grows until old rows are revoked. WP32 evaluated the fix: the server must reuse one installation for a
  new pairing of the same agent, and that changes EnvironmentsService's pairing contract (CONTRACTS A2.3). Not changed in WP32. The journeys
  pin the current behaviour (one row per fresh pairing; a revoked installation makes the page show Pair again).
- **Pairing tickets are held in the Web's process memory** (ADR-0028). Correct for one instance (ADR-0008); a second instance needs shared storage.

**Still open after WP30:**

- **Companion pattern not compared.** The `aurelius-promptus` repository could not be attached in WP30 (the access request was denied). Compare
  ADR-0026 with `docs/COMPANION_AND_WORKFLOWS.md` and `src/AureliusPromptus.Companion` before the first user.
- **Server path for requests that carry secrets.** Decide whether a send with a secret must go through the agent only.
- **The EnvironmentsService address the agent calls.** The agent needs a public HTTPS address for EnvironmentsService (or a Web-side proxy).
  Fly's private network does not reach a user's computer. WP33 decides the topology.
- **Agent package size.** The single-file binaries are about 110 to 120 MB, because the shared library references the kernel. Trimming is a
  follow-up.
- **Browser support (not verified).** Safari's handling of an HTTPS page calling `http://127.0.0.1`, and Chrome's Local Network Access prompt.
- **The first origin list is set at `setup`.** The server's list replaces it at pairing. An operator must run `setup` with the Web's origin.

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
| OI-28 | P3 | OPEN (product) | collections, the builder | Collections have no variables. The only `{{name}}` source is the environment, so the WP32 journeys use an environment variable where the plan said "collection variable". | Decide whether collections get variables (scope, masking) or the environment-only model is kept and documented. |
| OI-31 | P3 | OPEN (owner) | `EnvironmentsService` agent pairing (CONTRACTS A2.3), the Agent page | Each pairing adds an agent row; a page reload needs a new code (the WP31 follow-up, evaluated in WP32). | Reuse the installation for the same agent (a contract amendment), or keep one row per pairing and document it. |

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
- The agent journeys on a GitHub runner, from an HTTPS origin, and in browsers other than Chromium (WP32, TESTING.md §6). The agent's
  listener is driven by raw HTTP here, so a browser-only check of the Private Network Access prompt is not in the suite.

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
| OI-29 | The agent's run record was never stored: the page sent `X-CSRF-TOKEN` and the framework validates `RequestVerificationToken`, so every run record got 400 and no agent send reached the history (WP31) | WP32: `agent-client.js` and `AgentEndpoints.AntiforgeryHeader` use the framework's name; `AgentAntiforgeryHeaderTests`; the agent journeys assert the 201 and the history row. |
| OI-30 | A run record whose URL or secret was a reference (`{{base}}/items`) was refused (`invalid_url`), so an agent send through an environment never reached the history (WP15, WP30) | WP32: `OutboundSender.CheckTemplate` checks the record's shape and the secret-literal rule without resolving references (`AuthApplier.CheckSecretFields`); `HistoryRecordTests`. A literal secret is still refused. |
| — | SR-04 (body cut mid-character sent as base64 unmasked) | WP28: `df4b7b9`. |
| — | SR-07 (Teredo and 6to4 relay not blocked) | WP28: `df4b7b9`. |
| — | Register page did not show authservice's password rule | WP28: `aa29143`. |
| — | Web had no health check in the AppHost | WP28 (AR-01). AppHost not run. |

## 6. Residual risks (see SECURITY_REVIEW.md §6)

Listed above in §3, with the same content.
