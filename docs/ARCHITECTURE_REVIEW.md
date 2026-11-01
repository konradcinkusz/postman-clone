# Architecture review (WP28)

- **Scope:** the whole tree at `main` (`52eb745`) plus the WP28 changes on `wp/28-final-review`.
- **Method:** the playbook's REVIEW mode ([PLAYBOOK](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/PLAYBOOK.md),
  "Choosing REVIEW vs MODERNIZE": the repository already uses Aspire, a ServiceDefaults kernel and a database per service, so REVIEW
  applies; it builds, so RECOVER does not). The principles P1–P15 and the compliance checklist of
  [00-REFERENCE-ARCHITECTURE §3](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/architecture/00-REFERENCE-ARCHITECTURE.md#3-compliance-checklist),
  which [COMPLIANCE.md](architecture/COMPLIANCE.md) answers row by row.
- **Companion:** [SECURITY_REVIEW.md](SECURITY_REVIEW.md). **Decisions:** [ADR-0019](decisions/0019-final-review-outcomes.md).
- The architecture-core skill named in the brief is not installed in this session. The review follows the standards repository's
  playbook and guides directly, which is what its skills carry.

## 1. Summary

- **Blocking: none open.** One checklist gap was closed (the Web has no health check, §3 row 1).
- **Should-fix:** four, all closed or recorded: a deliberate departure from P15 (telemetry for the sender, recorded), the sender's
  missing resilience handler (recorded in ADR-0019), the Web's missing health check (fixed), and the CI tag-driven workflow (ADR-0007, recorded).
- **Consider:** five, open or documented.
- **Positive:** the checklist is met in 15 of 19 rows on the evidence in code and tests, and the architecture rules R1–R10 run in CI.

## 2. Findings

Each finding has the principle or guide it cites, the location, a concrete failure scenario, and a status.

### 2.1 Should-fix

**AR-01 — The Web has no health check in the AppHost (checklist row 1, P1, Fixed).**
- *Location:* `src/PostmanClone.AppHost/Resources/WebResource.cs`.
- *Failure scenario:* `WaitFor(authservice)` makes the Web start after authservice, but nothing waits for the Web itself to answer. The
  AppHost's dashboard shows it running while its sign-in page is still failing (the static-assets problem of OI-27 would not be visible).
- *Fix:* `WithHttpHealthCheck("/health")`, the same as the other services. The AppHost has never been run (OI-19); this is a compile-checked
  change. *Status:* FIXED in this PR.

**AR-02 — The sender's outbound calls are not traced (P15, recorded).**
- *Location:* `src/PostmanClone.ServiceDefaults/Telemetry/OutboundTelemetry.cs`.
- *Failure scenario:* see SECURITY_REVIEW SR-02. A user's path reaches a span.
- *Assessment:* P15 says every outbound call is traced; the sender is the exception on purpose, because the target is user data. The tracing
  filter drops only marked requests, so the other services keep their spans. *Status:* FIXED and recorded in DEVIATIONS.md (2026-10-31).

**AR-03 — The sender has no resilience handler (checklist row 14, P2, recorded).**
- *Location:* `src/PostmanClone.ExecutionService/Sending/Execution/RequestExecutor.cs:192-194`.
- *Failure scenario:* if the sender used the kernel's handler, a POST that timed out would be sent again, and a third party would receive the
  write twice.
- *Assessment:* deliberate, and it has a stronger reason than the kernel's rule (ADR-0023): the sender's own limits (timeout, redirect cap,
  read cap, SSRF guard) apply instead. ADR-0014 did not say so; ADR-0019 §8 does. *Status:* RECORDED (DEVIATIONS.md, ADR-0019).

**AR-04 — The CI tag-driven workflow with path-based detection is not met (checklist row 18, P12, decided).**
- *Location:* `.github/workflows/pr-validation.yml` (`detect-changes`), `secret-scan.yml` (no `v*` trigger).
- *Failure scenario:* none in this build; there is no deployment to make a mistake in.
- *Assessment:* ADR-0007 puts deployment out of scope, and the protocol forbids tags. The pull-request workflow runs every check on each
  change; the path filter exists for the E2E job only. *Status:* RECORDED (DEVIATIONS.md). Revisit with ADR-0007.

### 2.2 Consider

**AR-05 — The OTLP exporter has no test and was never run against a collector (row 4, P15, OI-18).** The exporter is registered only
when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (`OpenTelemetryExtensions.cs:45-48`). *Failure scenario:* the endpoint is set in production, and
the exporter fails silently or blocks shutdown. *Status:* OPEN. A stub collector test would close it.

**AR-06 — Two composition paths can disagree about configuration (P1, REPO-BASELINE §8, OI-14).** `src/PostmanClone.AppHost/Resources/*.cs`
and `scripts/run-local-stack.sh` both set `Auth__*`, `ConnectionStrings__*` and the key settings. *Failure scenario:* the AppHost's
issuer changes and the script keeps the old one, so local sign-in works in one path and not in the other. *Context:* `docs/OPERATIONS.md` §1
names the authority. *Status:* OPEN. A test that reads both would close it.

**AR-07 — Environment limit is checked then inserted (P4, SERVICE-API-PATTERNS, OI-09).** `EnvironmentsService/Environments/EnvironmentLimits.cs:10`.
*Failure scenario:* two creates at 49 both succeed, so the workspace has 51 environments. *Impact:* the limit is exceeded by one. *Context:*
CollectionsService takes a workspace lock for the same check (WP08). *Status:* OPEN.

**AR-08 — `PostmanClone.ServiceDefaults` is close to its ceiling (kernel rule, AGENTS.md).** `scripts/check-kernel-size.sh` reports
766 of 800 non-blank, non-comment lines. *Failure scenario:* the next change that adds a helper for one service fails CI, and the change is
then moved into the kernel to fit, which is how a domain leaks into the kernel. *Status:* OPEN. The OpenTelemetry HTTP filter added in this
PR (`OutboundTelemetry`, 20 lines) is generic; any further service code belongs in the service.

**AR-09 — The browser journeys' budget is a test-only wait with no production twin (P13).** `tests/PostmanClone.Web.E2E/Support/AuthBudget.cs`
(OI-26). The budget is now tested on a fake clock (`AuthBudgetWindowTests`). *Status:* FIXED (tests); the production issue is OI-26 (owner).

### 2.3 Positive findings

- **P1 (composition root):** the AppHost wires five resources with `WithReference`, `WaitFor` and a health check each (AR-01 closed the last gap).
- **P2 (kernel):** `ServiceDefaults` holds no entity, DTO, enum or user-facing string (architecture rule R1, `ServiceDefaults.Tests/Architecture/`).
  The one resilience handler is the kernel's (`Extensions.cs:33-56`) and retries are off for unsafe methods (ADR-0023).
- **P3 (database per service):** four databases on one PostgreSQL server, no service connects to another's (rule R2).
- **P4 (schema):** EF Core migrations applied by `MigrateAsync` in a hosted service; `EnsureCreated` only on the InMemory path (rule R7).
- **P5 (configuration):** everything from the environment; the secret scanner runs in the hook and in CI (SR-18 notes the CI image pin).
- **P9 (manifests):** `Program.cs` is 19–28 lines in each service and the Web, and 22 in the AppHost, under the 120-line ceiling (rule R4).
- **P10 (extension points):** interfaces registered in DI; `grep 'abstract class' src` finds none.
- **P13 (testing):** 2067 tests in the solution, green on the head (`TEST_POSTGRES` set); 19 more than `main` (2048), each new behaviour with a test.
- **P14 (documentation):** 25 ADRs and this review; `docs/` describes the state as it is (checked against the code in WP27 and here).
- **Dependencies (ADR-0009):** the Web has no package references beyond the framework; no vulnerable package in the audit.
- **Architecture rules R1–R10 are tests,** not reviews: they fail on a broken sample (ADR-0020).

## 3. The compliance checklist (reference architecture §3), item by item

Status: **Met** (evidence in code, and a test or check covers it), **Partly**, **Not met**, **N/A** (with the reason).

| # | Item | Status | Evidence | Gap or reason |
|---|---|---|---|---|
| 1 | AppHost: `WithReference`, `WaitFor`, `WithHttpHealthCheck` | Met (AppHost never run, OI-19) | `AppHost/Resources/*.cs`; `WebResource.cs` (AR-01, this PR) | The AppHost has not been run in any session. |
| 2 | `AddServiceDefaults()` and `MapDefaultEndpoints()` | Met | each `Program.cs`; rule R4 | |
| 3 | `/health` and `/alive`; platform check at `/health` | Met (platform check N/A) | `ServiceDefaults/Health/` | No platform health check: no deployment (ADR-0007). |
| 4 | OTLP traces, metrics, logs | Partly | `OpenTelemetryExtensions.cs`; outbound filter (SR-02) | The exporter is untested against a collector (AR-05, OI-18). |
| 5 | Owns its database | Met | `AppHost/Program.cs`; rule R2 | |
| 6 | Schema by `MigrateAsync` in a hosted service | Met | `ServiceDefaults/Database/MigrationHostedService.cs`; rule R7 | |
| 7 | Configuration from the environment; secret scanner in CI | Partly | `OPERATIONS.md` §2–3; hook (`v8.28.0` documented, OI-05); `secret-scan.yml` | The CI image is `v8.19.0`, not run here (SR-18, owner). |
| 8 | One signing key holder; the others validate by JWKS | Met | `AuthServiceResource.cs`; `Auth__MetadataAddress` in the others | Key custody: owner (ADR-0007). |
| 9 | Kernel holds no entity, DTO, enum, seed dataset, pricing constant or user-facing string | Met | rule R1; `check-kernel-size.sh` (766 of 800) | Close to the ceiling (AR-08). |
| 10 | Every optional integration has a no-op or fallback | Met | `UnavailableSecretProtector`; OTLP is a no-op when unset | SSRF protection is not optional; it refuses relaxation outside Development (SR-03). |
| 11 | Health reports optional integrations; banner prints the same list | Met | `OptionalIntegrationHealthCheck.cs`; `StartupBanner.cs`; `StartupBannerTests` | CollectionsService has none, so its list is empty. |
| 12 | Multi-stage Dockerfile, `:8080`, non-root, runtime matches the TFM | Met (not built here) | `src/*/Dockerfile`; rule R8 | No Docker daemon in sessions. CI's `docker-build` is the first build. |
| 13 | One `fly.toml` | N/A | none | Deployment out of scope (ADR-0007). |
| 14 | Outbound `HttpClient`s have the resilience handler with explicit timeouts | Met with a recorded exception | `Extensions.cs:33-56`; timeouts in `HttpClientTimeouts`; the sender (AR-03, ADR-0019 §8) | |
| 15 | `Program.cs` is a manifest; wiring in `ServiceCollectionExtensions` | Met | 19–28 lines per `Program.cs`; rule R4 | |
| 16 | Extension points are interfaces in DI, not base classes | Met | `IHistoryRecorder`, `IHostResolver`, `IAddressDialer`, `ISecretProtector`, `IOptionalIntegration` | None found: `grep 'abstract class' src` = 0. |
| 17 | Test project; the logic layer is covered | Met | five test projects, 2067 tests in the solution | No behaviour is being migrated, so characterisation tests are N/A. |
| 18 | Tag-driven workflow with path-based detection | Not met (by decision) | `pr-validation.yml`, `secret-scan.yml` (no tags) | ADR-0007 and the protocol forbid tags and deployment (AR-04). |
| 19 | Architectural decisions recorded in `docs/` | Met | `docs/decisions/`: ADR-0001 to ADR-0025 (0019 written here) | |

**Score: 15 Met, 2 Partly (rows 4 and 7), 1 Not met by decision (row 18), 1 N/A (row 13).** Two of the Met rows carry
a caveat that matters: row 1 (the AppHost has not been run, OI-19) and row 12 (the images have not been built here).
Row 14 is Met with a recorded exception. COMPLIANCE.md carries the same statuses.

## 4. Verification record (what ran, what did not)

| What | Result |
|---|---|
| `dotnet build PostmanClone.sln` (Debug) | 0 warnings, 0 errors (before and after each commit) |
| `dotnet test --solution PostmanClone.sln` with `TEST_POSTGRES` set | 2067 total, 0 failed (`EXIT 0`) on the branch before the AppHost edit |
| `dotnet test` of the Web and the ExecutionService projects after each change | ran; see the commit list |
| `TelemetryTests` alone in Release, three runs (OI-06) | 3 of 3 passed after the change (the old failure was not re-run on the old code) |
| Mutation checks (removing the change makes its test fail) | telemetry filter (fails), partial-secret trim (fails); the others were not mutation-checked |
| `scripts/check-doc-links.sh` | 144 of 144 relative links resolve (before this PR's docs; re-run below) |
| `scripts/check-kernel-size.sh` | 766 lines, limit 800 |
| `dotnet list … package --vulnerable --include-transitive` | no vulnerable packages |
| gitleaks `v8.28.0`: `git --staged` form and a token-shaped dummy | runs; the dummy is found (exit 2) |
| The pre-commit hook on every commit in this PR | scan clean (ran through the hook, not bypassed) |
| The Web from the repository root and from its publish folder, Debug and Release, Development and Production, with and without gzip | full files served; OI-27's empty response not reproduced |
| The publish Web with a truncated asset | refused to start, named the file |

**Not verified in this session:**

- The AppHost and the four Dockerfiles (no Docker). OI-19.
- The browser journeys against the stack in this session. The CI `e2e` job runs them for the head SHA.
- `scripts/smoke-auth.sh` against authservice (not started in this session).
- The OTLP exporter against a collector (AR-05).

## 5. Owner decisions that affect the architecture

- Keep the sender's lack of retries (ADR-0019 §8). Confirm.
- Tag-driven deployment, when a target is chosen (ADR-0007 is superseded then; AR-04).
- Whether the second composition path (`scripts/run-local-stack.sh`) is kept or replaced by the AppHost once it runs (AR-06).
