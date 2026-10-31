# Testing

What each layer proves, what runs where, what was run for this page, and what has never run. Counts and
results are from the WP27 session on the merged tree (2026-10-31, .NET SDK 10.0.114, PostgreSQL 16 on localhost).

## 1. Layers

| Layer | Where | What it proves | Example |
|---|---|---|---|
| Unit (pure logic) | each `tests/*.Tests` project | a rule holds on its own: validators, parsers, interpolation, masking, SSRF range tables, the Postman and cURL readers | `RequestDefinitionValidator` limits; `TargetValidator` on `2130706433`; `cURL` tokenizer quoting |
| In-process service (`WebApplicationFactory`) | the service test projects | the endpoint, its auth, its status codes and its scoping, on the InMemory provider; PostgreSQL when `TEST_POSTGRES` is set | `Scoping_GET_requests_id` answers 404 for another workspace |
| Real network, in process | `ExecutionService.Tests` | the sender against real Kestrel targets on loopback: redirects, timeouts, caps, gzip bombs, masking of what comes back | 50 MB generated body stops at the cap; redirect loop gives `too_many_redirects` |
| Architecture and tenancy rules | `ServiceDefaults.Tests/Architecture` | R1–R10 of [ADR-0020](decisions/0020-architecture-fitness-tests.md): isolation, manifests, routes authorized, a scoping test per id route, Dockerfile shape, one resilience handler, exact versions | R6 fails when an id route has no `Scoping_` test |
| Components (bUnit) | `Web.Tests` | Blazor components render the right states, with fake backends | the tree, the empty and error states, the members page |
| Browser-facing host | `Web.Tests` (`WebApplicationFactory`) | the origin guard, the security headers, the CSP and the interactive render mode | cross-site POST refused; `Referrer-Policy` set |
| Contract smoke against a real authservice | `scripts/smoke-auth.sh` | the authservice endpoints and claims the services rely on, against v0.3.4 | register, login, organization, refresh, logout |
| Repository checks | `scripts/check-doc-links.sh`, `scripts/check-kernel-size.sh`, `scripts/hooks/pre-commit` | links resolve; the kernel stays at most 800 lines; no secret in a staged change | |
| End to end (browser) | `Web.E2E` (Playwright, Chromium) against the no-Docker estate | 18 journeys: sign-up and sign-in through the form, collections and requests, sending to a local target, the rendering of a response, environments and secrets, import and export, isolation between workspaces, 390 px and the dark theme. Protected flows and non-goals: [ADR-0018](decisions/0018-e2e-scope-and-charter.md) | `scripts/run-local-stack.sh up`, then the two runs in §4 |

Tests assert behaviour, not presence: there are no `Assert.True(true)` placeholders and no `Thread.Sleep` in
`tests/` (checked by search). The one `Task.Delay` in `ExecuteSecurityTests` is a slow target that waits for the
client to abort, not a timed wait. Time comes from `TimeProvider` and `FakeTimeProvider`; no test pins a calendar date.

## 2. Counts

Discovered with `dotnet test --project <p> --list-tests`, and the full run totals.

| Project | Tests |
|---|---|
| `PostmanClone.ServiceDefaults.Tests` (includes the architecture rules) | 110 |
| `PostmanClone.CollectionsService.Tests` | 457 |
| `PostmanClone.EnvironmentsService.Tests` | 86 |
| `PostmanClone.ExecutionService.Tests` | 699 |
| `PostmanClone.Web.Tests` | 567 |
| Sum of the list | 1919 |
| **Run total** (`dotnet test --solution`) | **1927** |

The run total is eight more than the list. The eight are not attributed to a project here (OI-17).

`PostmanClone.TestKit` (shared fixtures, `TestTokens`, `PostgresFixture`, `FakeHttpMessageHandler`) and `tests/Shared/validation-cases.json`
(the case list the server validator reads) contain no tests of their own.

Eleven test files take the PostgreSQL path, which runs only when `TEST_POSTGRES` is set. Without it those tests are
skipped, not failing: CI sets it, and so does any run that should prove the PostgreSQL paths.

**Skipped:** one test (run total 1927: 1926 passed, 1 skipped), `CollectionSearchTests.Search_treats_backslash_as_a_literal_character` on the InMemory provider. It
calls `Assert.Skip` with the reason *the InMemory provider's LIKE does not implement the backslash escape; the PostgreSQL run
covers it*. The PostgreSQL run of the same assertion passes.

## 3. What was run for this page

| Command | Result |
|---|---|
| `dotnet build PostmanClone.sln` (Debug) | 0 warnings, 0 errors |
| `TEST_POSTGRES=... dotnet test --solution PostmanClone.sln` (Debug) | 1927 total, 1926 passed, 1 skipped (above), 0 failed |
| `dotnet build PostmanClone.sln -c Release` and `dotnet test --solution PostmanClone.sln -c Release --no-build` | 1927 total, 1926 passed, 1 skipped, 0 failed |
| `dotnet test --project tests/PostmanClone.ServiceDefaults.Tests -c Release --no-build --filter FullyQualifiedName~TelemetryTests` | **1 failed** (see §5, OI-06) |
| `dotnet list PostmanClone.sln package --vulnerable --include-transitive` | no vulnerable packages in any of the 12 projects |
| `scripts/check-kernel-size.sh` | 756 of 800 lines |
| `scripts/check-doc-links.sh` | 101 relative links resolve |
| `scripts/gen-dev-secrets.sh`, `scripts/run-local-stack.sh up` | authservice v0.3.4, CollectionsService and ExecutionService healthy; the databases created |
| `scripts/smoke-auth.sh` | PASS against the running authservice |
| `scripts/run-local-stack.sh status` | all three report running and healthy |
| `scripts/hooks/pre-commit` (staged dummy secret) | refused, exit 1; with nothing staged, clean |
| Ad hoc sender and collections checks (curl, not committed) | collection create 201; loopback target `blocked` `ssrf_blocked_address`; `environmentId` `502 upstream_unavailable`; history list 200 |
| Ad hoc Web checks (curl and headless Chromium, not committed) | curl sign-in works with the settings in OPERATIONS §4.3; **a browser sign-in completes** and lands on `/no-workspace` (OI-01, fixed by WP18b; before it returned 403) |

| E2E, loopback set (`dotnet test --project tests/PostmanClone.Web.E2E … --filter "Mode!=strict"`) on the estate started with `E2E_SENDER_LOOPBACK=1` | **16 of 16 passed**, 1 min 27 s, serial. Each journey creates its own users (`TestUsers`) and signs in through the form. Run 2026-10-31 on the sandbox (4 vCPU) |
| E2E, strict set (`--filter "Mode!=loopback"`) on the estate started without the flag | **12 of 12 passed**, 1 min 21 s. The default refuses a loopback target with the service's reason (`ssrf_blocked_address`) and the metadata address in both sets |
| E2E prerequisites: the Web unreachable | locally the journeys **skip with the reason**; with `POSTMANCLONE_E2E_REQUIRED=1` they **fail** with the same reason; a missing Chromium is reported with its instruction |
| E2E deliberate failure (a temporary journey with a false assertion, run once, then deleted) | the run exits 2 and reports `failed: 1`, so the job cannot pass on a failing journey |

The ad hoc scripts lived in the session's scratch directory and are not part of the repository. The E2E runs above are
the repository's own journeys; the CI job of the same name is in §4.

Two findings from the runs, both recorded in OPERATIONS and the open-items register:

- **authservice's sign-in window.** authservice allows 20 auth requests per minute per client IP and queues five more until
  the window reopens. An unbudgeted run from one address waited up to a minute for a register or a login (OI-26). The suite now
  counts its own calls and keeps them at 18 a minute (`AuthBudget`).
- **The Web must run from its publish directory.** Started from another working directory, it finds no `wwwroot`, every static
  asset answers 200 with an empty body, and the circuit never starts. `run-local-stack.sh` starts it from its own folder.

## 4. What CI runs

`.github/workflows/pr-validation.yml`, on a pull request to `main` that is not a draft:

| Job | Runs | Notes |
|---|---|---|
| `detect-changes` | finds the solution, the Dockerfiles and the kernel-size script | decides which jobs run |
| `dotnet` | `dotnet restore` (fails on a vulnerable package), `dotnet build -c Release`, `dotnet test --solution -c Release` | PostgreSQL 16 service; `TEST_POSTGRES` set |
| `docker-build` | `docker build --file <Dockerfile> .` for each of the four Dockerfiles, no push | never run in a session (no Docker daemon); CI is the first real build |
| `kernel-size` | `scripts/check-kernel-size.sh` | |
| `auth-smoke` | `scripts/run-local-stack.sh up`, `scripts/smoke-auth.sh`, `down` | PostgreSQL 16 service; authservice built from its tag |
| `e2e` | `scripts/run-local-stack.sh up` with `E2E_SENDER_LOOPBACK=1`, the loopback set; `down`; `up` without the flag, the strict set; `down`. Chromium installed by `playwright.ps1 install --with-deps chromium`. `POSTMANCLONE_E2E_REQUIRED=1`. Logs uploaded on failure | PostgreSQL 16 service; runs when `src/`, `tests/`, `scripts/` or this workflow changed on a pull request |
| `docs-links` | `scripts/check-doc-links.sh` | |
| `pr-validation` | aggregate of the above | the one required status check (owner action) |

`.github/workflows/secret-scan.yml` runs gitleaks v8.19.0 in Docker over the full history, on a pull request, on a push to
`main`, and weekly. The pre-commit hook runs the same scanner locally (§5).

`tests/PostmanClone.Web.E2E` is **not in `PostmanClone.sln`**: its journeys need the running estate, and a solution run of a project with
no matching tests ends with exit code 8. The `dotnet` job therefore does not see it, and the `e2e` job builds and runs it on its own.
No job runs the AppHost.

## 5. Known problems in the test and check layers

- **OI-06, `TelemetryTests.Requests_are_traced_but_health_probes_are_filtered_out_of_traces`.** Passes in the full Debug and
  full Release runs. Run alone in Release it fails: `Assert.Contains() Failure: Filter not matched in collection`, with an empty
  collection: the server span was not captured (PR #21 reports the same on a clean `main`). It predates WP25. It is timing-dependent, and it is not skipped.
- **OI-05, the secret scanner.** The pre-commit hook runs `gitleaks git --staged`. gitleaks v8.19.0, built from its module
  source (`go install github.com/zricethezav/gitleaks/v8@v8.19.0`), fails that form with `flag accessed but not defined: source`,
  and the hook then refuses every commit (it fails closed, as designed). gitleaks from the latest module passes the hook and
  refuses a dummy secret. The hook was not modified. CI uses the v8.19.0 Docker image in its own `detect` form.
- **OI-14, the two local-stack definitions.** Changes to `Auth__*` in the AppHost and in `run-local-stack.sh` have to be made in both
  places. Nothing checks that they agree.

## 6. What has never run

| Item | Why | What would close it |
|---|---|---|
| The AppHost (`dotnet run --project src/PostmanClone.AppHost`) | no Docker daemon in sessions | a machine with Docker, once per release |
| `docker build` of any of the four Dockerfiles, here | no Docker daemon | CI's `docker-build` job (not yet seen green for this head at the time of writing) |
| The `e2e` job on a GitHub runner | the journeys ran on the sandbox only; the job's result is on the pull request | the job's first green run on the pull request head |
| The OTLP exporter against a collector | no collector; no test covers the branch | an integration test with a stub collector (OI-18) |
| Real internet egress from the sender | the SSRF tests use loopback targets and scripted resolvers; no public host was contacted | a manual check against one public host with the default settings |
| Browsers other than Chromium, and the 390 px check on a real phone | the suite uses Chromium with a 390 px viewport | a manual check on a device (not automated) |

## 7. Manual checks

TESTING-STRATEGY names four situations where a human must test, because automation cannot substitute:

| Situation | Applies here? | Why |
|---|---|---|
| Third-party DOM you do not control | no | the product does not drive other sites' pages |
| Entitlement gating where errors cost twice | no | there are no paid tiers (ADR-0001, billing out of scope) |
| Extension or native contexts | no | no extension, no native app |
| Client-side-only data, where a bug is permanent loss | no | the Web stores nothing in the browser (verified by search for `localStorage`, `sessionStorage`, JS interop); the server holds the data |

So none of the four situations requires a scheduled human pass. The manual checks that do apply are:

1. **Sign-in in a real browser**, both correct and wrong passwords, then sign-out. Headless Chromium completes a correct sign-in
   (checked 2026-10-31). A human pass is still wanted for the wrong-password message and sign-out.
2. **Invitation round trip** with two accounts, using the token from the authservice log (AUTH.md, Members and invitations).
   Done once live in WP24; repeat after any change to the flow.
3. **Layout at 390 px and in dark mode**, on the home page and the members page.
4. **The pre-commit hook** on each developer machine: a staged dummy secret must be refused.
