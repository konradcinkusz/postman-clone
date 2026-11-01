# Testing

What each layer proves, what runs where, what was run for this page, and what has never run. Counts and
results are from the WP28 session on the final branch (2026-10-31, .NET SDK 10.0.114, PostgreSQL 16 on localhost).

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
| End to end (browser) | `Web.E2E` (Playwright, Chromium) against the no-Docker estate | The protected flows: sign-up and sign-in through the form, collections and requests, sending to a local target, the rendering of a response, environments and secrets, import and export, isolation between workspaces, 390 px and the dark theme (ADR-0018). Since WP32, also the local agent: pairing, a send through it with a secret masked everywhere, the strict refusal, revoke, the listener's refusals, and a member's shared collection (§9). The run totals are in §3. Non-goals: [ADR-0018](decisions/0018-e2e-scope-and-charter.md) | `scripts/run-local-stack.sh up agent`, then the two runs in §4 |

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
| **Run total** (`dotnet test --solution`, WP28 final) | **2067** (2066 passed, 1 skipped) |

The per-project table is the WP27 breakdown and is not re-counted here; the WP28 run total is 2067 (§3).

`PostmanClone.TestKit` (shared fixtures, `TestTokens`, `PostgresFixture`, `FakeHttpMessageHandler`) and `tests/Shared/validation-cases.json`
(the case list the server validator reads) contain no tests of their own.

Eleven test files take the PostgreSQL path, which runs only when `TEST_POSTGRES` is set. Without it those tests are
skipped, not failing: CI sets it, and so does any run that should prove the PostgreSQL paths.

**Skipped:** one test (WP28 run total 2067: 2066 passed, 1 skipped), `CollectionSearchTests.Search_treats_backslash_as_a_literal_character` on the InMemory provider. It
calls `Assert.Skip` with the reason *the InMemory provider's LIKE does not implement the backslash escape; the PostgreSQL run
covers it*. The PostgreSQL run of the same assertion passes.

## 3. What was run for this page

| Command | Result |
|---|---|
| `dotnet build PostmanClone.sln` (Debug) | 0 warnings, 0 errors |
| `TEST_POSTGRES=... dotnet test --solution PostmanClone.sln` (Debug), WP28 final | 2067 total, 2066 passed, 1 skipped (above), 0 failed |
| `dotnet build PostmanClone.sln` (Debug), WP28 final | 0 warnings, 0 errors |
| `scripts/run-local-stack.sh up` (no Docker), then `scripts/smoke-auth.sh` | authservice contract PASS |
| E2E strict set (`Mode!=loopback`), stack with `WEB_AUTH_PERMITS_PER_MINUTE=18` | 15 total, 15 passed, 0 failed |
| E2E loopback set (`E2E_SENDER_LOOPBACK=1`, `Mode!=strict`) | 20 total, 20 passed, 0 failed |
| **WP32, final head.** `dotnet build PostmanClone.sln` (Debug) | 0 warnings, 0 errors |
| **WP32, final head.** `dotnet test --solution PostmanClone.sln` with `TEST_POSTGRES` set | 2232 total, 2229 passed, 3 skipped, 0 failed. The skips are the two `AgentScopingTests` marked `Skip` with their reasons (WP29) and one `CollectionSearchTests` case |
| **WP32, final head.** E2E loopback set: `up agent` with `E2E_SENDER_LOOPBACK=1 AUTHSERVICE_ENVIRONMENT=Development WEB_AUTH_PERMITS_PER_MINUTE=18`, then `--filter "Mode!=strict"` | **33 of 33 passed**, serial, `POSTMANCLONE_E2E_REQUIRED=1`. The agent journeys, the listener's nine tests and the sharing journey are in it |
| **WP32, final head.** E2E strict set: `up agent` without the loopback flag, then `--filter "Mode!=loopback"` | **26 of 26 passed**. The strict agent journey (the agent reaches a loopback target, the server refuses it) is in it |
| **WP32.** Listener tests alone (`--filter FullyQualifiedName~AgentListenerJourneys`), against the real agent process | 9 of 9 passed |
| **WP32.** `bash scripts/check-doc-links.sh` · `bash scripts/check-kernel-size.sh` · the workflow parsed as YAML | 202 links resolve · kernel 766 of 800 lines · parsed |
| **WP32.** Defects the journeys found and that were fixed on this branch (see OPEN-ITEMS OI-29, OI-30) | the run record never reached the history (antiforgery header), and a record with a variable in its URL was refused. Each was red before its fix and green after |
| `dotnet test --project tests/PostmanClone.ServiceDefaults.Tests -c Release --filter FullyQualifiedName~Requests_are_traced` (three runs) | 3 of 3 passed (OI-06 fixed in WP28) |
| `dotnet list PostmanClone.sln package --vulnerable --include-transitive` | no vulnerable packages in any of the 12 projects |
| `scripts/check-kernel-size.sh` | 766 of 800 lines |
| `scripts/check-doc-links.sh` | 163 relative links resolve |
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
| `e2e` | `scripts/run-local-stack.sh up agent` with `E2E_SENDER_LOOPBACK=1`, the loopback set; `down`; `up agent` without the flag, the strict set; `down`. `AUTHSERVICE_ENVIRONMENT=Development` for the whole job (the invitation token, WP32). Chromium installed by `playwright.ps1 install --with-deps chromium`. `POSTMANCLONE_E2E_REQUIRED=1`. Logs (including `agent.log`) uploaded on failure | PostgreSQL 16 service; runs when `src/`, `tests/`, `scripts/` or this workflow changed on a pull request |
| `docs-links` | `scripts/check-doc-links.sh` | |
| `pr-validation` | aggregate of the above | the one required status check (owner action) |

`.github/workflows/secret-scan.yml` runs gitleaks v8.19.0 in Docker over the full history, on a pull request, on a push to
`main`, and weekly. The pre-commit hook runs the same scanner locally (§5).

`tests/PostmanClone.Web.E2E` is **not in `PostmanClone.sln`**: its journeys need the running estate, and a solution run of a project with
no matching tests ends with exit code 8. The `dotnet` job therefore does not see it, and the `e2e` job builds and runs it on its own.
No job runs the AppHost.

## 5. Known problems in the test and check layers

- **OI-06, `TelemetryTests.Requests_are_traced_but_health_probes_are_filtered_out_of_traces`. Fixed in WP28.** The test read the
  server span right after the response: a server span ends after the response is written, so it could be missing. The test now
  waits, bounded at ten seconds, for the span it asserts on. Run alone in Release, three runs passed after the change. The failure
  before the change was reproduced in WP26 and WP27; it was not re-run on the old code in WP28.
- **OI-05, the secret scanner.** The pre-commit hook runs `gitleaks git --staged`. gitleaks v8.19.0 fails that form with
  `flag accessed but not defined: source`, and the hook then refuses every commit (it fails closed, as designed).
  WP28 ran `go install github.com/zricethezav/gitleaks/v8@v8.28.0` into `$HOME/.local/bin`: `gitleaks git --help` works, the hook's
  form scans the staged index, and a token-shaped dummy is found (exit 2). The hook and the installer now name v8.28.0.
  CI uses the v8.19.0 Docker image with `detect`; it is not the hook's form and was not run here (no Docker). An owner decision (OPEN-ITEMS §0).
- **OI-14, the two local-stack definitions.** Changes to `Auth__*` in the AppHost and in `run-local-stack.sh` have to be made in both
  places. Nothing checks that they agree.

## 6. What has never run

| Item | Why | What would close it |
|---|---|---|
| The AppHost (`dotnet run --project src/PostmanClone.AppHost`) | no Docker daemon in sessions | a machine with Docker, once per release |
| `docker build` of any of the four Dockerfiles, here | no Docker daemon | CI's `docker-build` job (not yet seen green for this head at the time of writing) |
| The `e2e` job on a GitHub runner | the journeys ran on the sandbox only; the job's result is on the pull request | the job's first green run on the pull request head |
| The agent journeys from an HTTPS page, and the Private Network Access / Local Network Access prompt of Chrome | the stack and the Web are on `http://localhost:5310`; a headless Chromium does not show the prompt | a manual check on a desktop Chrome and on an HTTPS origin, once a public origin exists (WP32, §9) |
| The agent in Safari and Firefox | the journeys run Chromium only (ADR-0018) | a manual check in each browser (WP32, §9) |
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

## 8. The Web's agent client (WP31, ADR-0028)

**In CI (`dotnet test --solution`):** `Agent/` in `tests/PostmanClone.Web.Tests` (bUnit with a fake browser and the real
`AgentConnection`; the Agent page with a fake client; the run route and the CSP through `WebHostFactory`; the boundary rules read
from disk), and `AgentClientTests` (the routes, the bearers, the outcomes).

**Run by hand in WP31 (Chromium, not in CI):** the built agent on `127.0.0.1:47631` configured with the Web's origin
`http://localhost:5310`, the Web on that origin, and a stand-in server for the pairing route. The page script probed, paired, sent to a local
target through the agent, and received the response with `Set-Cookie` masked; a wrong pairing token got its 401. An origin that the CSP does not
name was refused by the browser. Not run: an HTTPS page, Firefox, Safari, and the Local Network Access prompt (ADR-0028, "Not verified").

## 9. The local agent in the browser journeys (WP32, ADR-0026, ADR-0028)

These run in the E2E project against the whole estate on this machine: the real authservice, EnvironmentsService and
ExecutionService, the Web, and the agent built and started by `scripts/run-local-stack.sh up agent` on `127.0.0.1:47631`.
Chromium only. Each check reads the page, the server history, or the agent's log (`.local-stack/logs/agent.log`, its stdout and
stderr), so a secret that reached any of them fails the journey.

| Journey | What it shows |
|---|---|
| `Pairing_from_the_agent_page_shows_Connected_in_the_top_bar` | A code is created on `/agent` and paired from the page; the top bar reads Connected and the page lists one installation |
| `Send_through_the_agent_uses_the_environment_and_shows_the_secret_masked_everywhere` | The environment's variable and its secret are used. The target receives the real secret (`TargetServer.LastAuthorization`); the panel says the agent sent it and shows `Authorization=Bearer ••••••`; the run record is posted (201) and listed in the server history with source `agent` and the masked template; the history page and the agent's log hold no secret |
| `With_the_agent_connected_the_agent_reaches_a_loopback_target_and_the_server_path_refuses_it` (strict set) | The agent sends to the loopback target while the server's choice refuses it with `ssrf_blocked_address`; the top bar still reads Connected |
| `Revoking_the_installation_makes_the_next_agent_send_ask_for_a_new_pairing` | After the revoke, the top bar reads Pair again, and a send through the agent says so |
| `AgentListenerJourneys` (nine tests, raw HTTP, no browser) | A foreign Origin is refused with 403 and no allow header; no Origin is refused; a rebinding Host gets 421; a send without the pairing token gets 401, and with a wrong one 401; `text/plain` gets 415; the Private Network Access preflight from the Web's origin gets 204 with that exact origin and the private-network header; a foreign preflight gets no allow header; a second listener on the same port exits 1 with "in use" |
| `SharedCollectionJourneys` | The owner invites a member (WP24); the invitation token is read from `authservice.log` (Development only); the member accepts and is redirected to `/workspace?notice=joined`; the member's tree shows the owner's collection |

**Found by these journeys and fixed in WP32** (OPEN-ITEMS OI-29, OI-30): the agent's run record was never stored, because the page
sent the antiforgery token in a header the framework does not read; and a record with a variable in its URL or secret was refused
by the send rules, so a send through an environment never reached the history.

**What the journeys do not do.** Collections have no variables, so the "collection variable" of the plan is an environment variable
here (OI-28). Each pairing adds an agent row and a reload needs a new code (OI-31). Neither is changed here.

**Not verified:** the agent journeys on a GitHub runner (the `e2e` job's first run on the pull request), an HTTPS origin calling
`http://127.0.0.1`, the Private and Local Network Access prompts (headless Chromium does not show them), and browsers other than
Chromium. §6 lists them.
