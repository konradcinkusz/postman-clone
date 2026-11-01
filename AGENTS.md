# Working in this repository

Guidance for coding agents and for people. This repository follows
[`architecture-standards`](https://github.com/konradcinkusz/architecture-standards) (principles
P1–P15 and the repository baseline). `.claude/settings.json` declares it as a plugin marketplace
and enables `architecture-core`. When a rule here and a rule there disagree, raise it rather than
picking one.

The build plan is the source of truth for scope and contracts:
[`docs/implementation-plan/`](docs/implementation-plan/README.md). Read `README.md`, `CONTRACTS.md`,
`PROTOCOL.md` and your work package in `WORK-PACKAGES.md` before you change anything.

## What is here

| Path | What it is |
|---|---|
| `docs/implementation-plan/` | Scope, architecture, frozen contracts, the sub-session protocol, WP00–WP28 |
| `docs/decisions/` | ADRs. Numbers are reserved per work package |
| `docs/architecture/DEVIATIONS.md` | Every departure from architecture-standards, with its reason |
| `scripts/install-hooks.sh`, `scripts/hooks/pre-commit` | The local secret scan |
| `scripts/check-doc-links.sh` | Every relative Markdown link resolves (run in CI) |
| `scripts/check-kernel-size.sh` | The shared kernel stays within 800 non-blank, non-comment lines (run in CI) |
| `scripts/run-local-stack.sh`, `scripts/gen-dev-secrets.sh` | the whole estate without Docker, against a local PostgreSQL: authservice at its pinned tag (5300), CollectionsService (5301), EnvironmentsService (5302), ExecutionService (5303) and the Web (5310); the gitignored `.local-stack/` secrets (WP03, WP04, WP06, WP26). `E2E_SENDER_LOOPBACK=1` is the test-only switch that lets the sender reach loopback (ADR-0018). `AUTHSERVICE_ENVIRONMENT=Development` makes authservice log invitation tokens locally (WP24) |
| `tests/PostmanClone.Web.E2E` | The browser journeys (WP26, ADR-0018): Playwright for .NET on Chromium, 18 journeys over the protected flows. Not in `PostmanClone.sln`; they run against the local stack, one at a time, and skip with the reason when the stack or the browser is missing (they fail in CI). See docs/TESTING.md §1 and OPERATIONS §4.5 |
| `scripts/smoke-auth.sh` | The authservice contract the services rely on: register, login, organization, refresh, claims (run in CI as `auth-smoke`) |
| `docs/AUTH.md` | How sign-in works through authservice v0.3.4: the flow, the claims, the endpoints read from source, the differences from CONTRACTS §6 |
| `.github/workflows/` | `pr-validation.yml` (the required check), `secret-scan.yml` |
| `Directory.Build.props`, `Directory.Packages.props`, `global.json` | Solution-wide settings, package versions, SDK pin, and the test runner (Microsoft.Testing.Platform) |
| `PostmanClone.sln`, `src/PostmanClone.ServiceDefaults`, `tests/PostmanClone.TestKit`, `tests/PostmanClone.ServiceDefaults.Tests` | The solution and the shared kernel (WP01) |
| `src/PostmanClone.AppHost` | The Aspire composition root, dev only: PostgreSQL with four service databases, authservice (WP03), CollectionsService (WP04), EnvironmentsService (WP05), ExecutionService (WP06) and the Web (WP07) |
| `src/PostmanClone.CollectionsService`, `tests/PostmanClone.CollectionsService.Tests` | CollectionsService (WP04, WP08): the manifest `Program.cs`, the entities, the EF model and its first migration, the Dockerfile (P6), the collections, folders and saved-request routes of CONTRACTS §2 (`/api/v1/collections`, `/api/v1/folders`, `/api/v1/requests`), the `RequestDefinitionValidator` (WP13, `Domain/`, shared with the importers WP16 and WP17, which judge definitions the same way), the Postman v2.1 import and export (WP16: `Interop/Postman/` holds the importer, the exporter and the import limits; `Api/PostmanIoEndpoints.cs` the routes; the threat model is ADR-0016), the cURL import and export (WP17: `POST /api/v1/import/curl` and `GET /api/v1/requests/{id}/export/curl`; `Interop/Curl/` is a tokenizer, a parser and a generator, and runs no shell), the OpenAPI route list (Development only), and the host tests |
| `src/PostmanClone.EnvironmentsService`, `tests/PostmanClone.EnvironmentsService.Tests` | EnvironmentsService (WP05, WP09): environments and variables on `environmentsdb`, the `/api/v1/environments` endpoints of CONTRACTS §3 (`resolve` is the only response with plaintext secrets), AES-256-GCM secret protection (ADR-0006), `/health` reports `secrets`. Its Dockerfile is reviewed, not built here (no Docker daemon in sessions) |
| `src/PostmanClone.ExecutionService`, `tests/PostmanClone.ExecutionService.Tests` | The sender and history service (WP06): sender limits and the SSRF flag with startup validation, the EnvironmentsService client skeleton, the `history_entries` migration, the Dockerfile. The SSRF guard (WP10, `Sending/Ssrf/`, ADR-0011) is wired into the sender through `SsrfHandlerFactory`. The request assembler (WP11, `Sending/Assembly/`): variable expansion, URL, headers, body, auth helper and secret masking, pure logic with no HTTP (ADR-0012). `POST /api/v1/execute` (WP14, ADR-0014) sends requests with redirects and the capped body. The history (WP15, ADR-0015) stores each send with the request template and the result masked, keeps 200 entries per user and workspace, and serves `/api/v1/history` (list, get, delete one, clear) for the caller's own entries only |

| `src/PostmanClone.Sending` | The shared sending code (WP30, ADR-0026, ADR-0027): the assembler, masking and caps, the outbound executor, the destination rules and the SSRF guard (`Ssrf/`). ExecutionService and the agent both reference it. It holds no database and no endpoint |
| `src/PostmanClone.Agent`, `tests/PostmanClone.Agent.Tests` | The local agent, the tool `postmanclone-agent` (WP30, ADR-0026, ADR-0027): the loopback listener and its guard (`Local/`), the server client (`Remote/`), the send (`Send/`), the 0600 config (`Config/`), and the commands (`Cli/`). Its tests run a real listener on loopback. CI builds its packages as artifacts (`agent-artifacts`) |
| `src/PostmanClone.Web`, `tests/PostmanClone.Web.Tests` | The Blazor Web App shell (WP07): interactive server root, token stylesheet, navigation, status strip, origin guard and CSP on the host, `Dockerfile` (built by CI, never pushed). `docs/architecture/UI-STYLE.md` is its design contract |
| `tests/PostmanClone.ServiceDefaults.Tests/Architecture/` | The architecture and tenancy rules R1–R10 (WP25, ADR-0020): kernel and service isolation, manifests, route authorization read from each host, the isolation matrix (`Scoping_<METHOD>_<route>` for every `{id}` route), `EnsureCreated` guard, Dockerfiles (P6), one resilience handler, exact package versions. Each rule has a fixture that shows it fails |
| `src/PostmanClone.Web/Membership`, `Components/Pages/Workspace.razor`, `Components/Pages/AcceptInvitation.razor` | The members page and the invitation flow (WP24): the workspace and its members, invite and accept as antiforgery form posts, the invitation rules and messages. `docs/AUTH.md` (Members and invitations) records what authservice does |
| `src/PostmanClone.Web/Collections`, `Components/Shell`, `Components/Pages/WorkspaceHome.razor`, `Api/CollectionsClient.cs`, `Api/CollectionsDtos.cs` | The workspace shell (WP18): the home page at `/`, the tree of collections, folders and requests (`Collections/CollectionTreeModel.cs` is the pure model; `Collections/ShellTree.cs` the flat rows), the context actions, the open request tabs (`OpenTabs`, at most 20), and the typed client for CollectionsService. The copies of its types are pinned by `tests/PostmanClone.Web.Tests/Api/CollectionsRoundTripTests.cs` |
| `src/PostmanClone.Web/Builder`, `Components/Builder`, `Collections/RequestWorkbench.cs` | The request builder (WP19, ADR-0017): `RequestDraft` (the editable copy; the query lives in the table, the URL is written without it), `UrlParamSync` (the URL ↔ table sync and the RFC 3986 encoding), `DraftValidator` (the client mirror of the service's rules, judged against it by a parity test), `RequestEditor` (one open request: version, dirty, conflict), `BulkRows`, `JsonBeautifier`, `VariableTokens`. The editors (`RequestBuilder`, `UrlBar`, `KeyValueEditor`, `BodyEditor`, `AuthEditor`) edit the draft in place. `RequestWorkbench` holds the circuit's editors and makes the save, reload, overwrite, save-as and create calls |
| `src/PostmanClone.Web/Auth`, `Api/AuthServiceClient.cs`, `Hosting/WebAuthExtensions.cs` | The sign-in flow (WP12): the opaque session cookie and the server-side session store, token verification against authservice's JWKS, single-flight refresh, the five-minute circuit revalidation, the `ReturnUrl` policy, the form endpoints (`/sign-in`, `/register`, `/sign-out`, `/no-workspace/create`) and workspace routing. `docs/AUTH.md` records what authservice does; `docs/decisions/0013-circuit-lifetime-and-session-store.md` records the lifetimes |
| `src/PostmanClone.Web/Components/Pages/Import.razor`, `CollectionCurl.razor`, `Collections/ImportRules.cs`, `Collections/CollectionDownloadEndpoints.cs` | The import and export UI (WP23): `/import` (a Postman file streamed to the service, a cURL command into a collection or folder, the warnings grouped by code), `/requests/{id}/curl` (Copy as cURL), and the BFF download `GET /download/collections/{id}` (ADR-0021). `ImportRules.cs` holds the checks, the grouping and the messages, tested without rendering |
| `src/PostmanClone.Web/Response`, `Components/Response/ResponsePanel.razor`, `Api/ExecutionClient.cs`, `Api/ExecutionDtos.cs` | The sender client and the response viewer (WP20, ADR-0023): `ExecutionClient` (`POST /api/v1/execute`, the copies of its types round-trip-tested against the service), `SendCoordinator` (one send per circuit; a superseded answer is dropped; cancelling stops the call), `ResponseView` and `BodyView` (pure: outcome words, status family, sizes, body lines), `JsonPretty` (an iterative JSON scanner that never recurses), `ResponsePanel` (every value written as encoded text; Preview disabled). The per-client timeouts are `HttpClientTimeouts` in the kernel (`Resilience/`) |
| `src/PostmanClone.Web/Environments`, `Components/Environments`, `Components/Pages/Environments.razor`, `Api/EnvironmentsClient.cs`, `Api/EnvironmentsDtos.cs`, `Response/EnvironmentSelection.cs` | The environments UI (WP21, ADR-0025): `/environments` (list, create, rename, delete, variables), the top-bar `EnvironmentSelector` that chooses the environment for the sends, and the typed client for EnvironmentsService. A secret's value is write-only: `VariableRow` holds only "a value is stored" and what was typed since, and the input has no value attribute. `EnvironmentRules` mirrors the service's limits and patterns, and a parity test runs the service's own attributes on the same inputs |
| `src/PostmanClone.Web/History`, `Components/Pages/History.razor`, `Components/Shell/HistoryPanel.razor`, `Api/HistoryClient.cs`, `Api/HistoryDtos.cs` | The request history (WP22, ADR-0024): `/history` (the caller's entries, paged, filtered on the masked URL, detail through `ResponsePanel`, delete one and clear all with a two-step confirm), the workspace shell's history panel, and *Open in builder* (`/?reopen=<id>`), which opens an unsaved draft that cannot be saved by id and is refused by send and save while it still holds the mask. `HistoryPaging`, `RelativeTime` and `HistoryReopen` are the pure rules; `HistoryChangeSignal` (`IHistoryChanged`) reloads the panel after a recorded send |
| `src/PostmanClone.Web/Agent`, `Components/Agent`, `Components/Pages/AgentPage.razor`, `Api/AgentClient.cs`, `Api/AgentDtos.cs`, `wwwroot/agent-client.js` | The Web's agent client (WP31, ADR-0028): the pairing page at `/agent`, the top-bar status, the sender choice in the builder, the one hand-written script (the only script of the Web's own), the run record `POST /agent/runs` (antiforgery, the name-based mask, the session bearer), and the CSP `connect-src` with exactly the loopback origins of `Agent:Port` and `Agent:ConnectOrigins`. The pairing token lives in the circuit only |
| `flyio/`, `.github/workflows/flyio.yml`, `docs/FLY_DEPLOYMENT.md`, `flyio/SECRETS.md` | The Fly estate (WP33, ADR-0029): six `*.fly.toml` files (the Web public, the others private), the manual deploy-only workflow with its token check, and the owner's procedure and secret list. Nothing here has been deployed |
| `scripts/check-flyio-trigger.sh`, `scripts/check-flyio-config.py`, `scripts/flyio-preflight.sh` | The Fly checks (run by `pr-validation` as `flyio-config`), and the per-app preflight the workflow calls before a deploy. `docs/USER_GUIDE.md` is the user's guide |

Still a placeholder: the Web's `/about` page. The plan's README, §3 lists the target layout.

The documentation that describes the system as it is: `README.md`, `docs/ARCHITECTURE.md` (diagrams and flows), `docs/SECURITY_REVIEW.md` and `docs/ARCHITECTURE_REVIEW.md` (the final review, WP28; ADR-0019), `docs/OPERATIONS.md` (configuration, running locally), `docs/TESTING.md` (what runs and what has never run), `docs/PRIVACY.md` (personal data and erasure), `docs/architecture/COMPLIANCE.md` (the reference checklist) and `docs/architecture/OPEN-ITEMS.md` (what is still open). Keep them true when a change touches what they describe.

## Build and test

```bash
dotnet build PostmanClone.sln                  # warnings are errors; restore fails on vulnerable packages
dotnet test --solution PostmanClone.sln         # PostgreSQL-backed tests run when TEST_POSTGRES is set. The browser journeys are
                                                # not in the solution (they need the running stack): see below
```

`--solution` is required: `global.json` selects Microsoft.Testing.Platform, and the .NET 10 SDK takes the solution as a flag there.

What runs now:

```bash
bash scripts/check-doc-links.sh   # relative Markdown links resolve
bash scripts/check-kernel-size.sh # the kernel stays within 800 lines
scripts/install-hooks.sh          # once per clone: the pre-commit secret scan
scripts/gen-dev-secrets.sh        # once per clone: .local-stack/secrets.env (never printed)
scripts/run-local-stack.sh up     # the estate on :5300-:5303 and the Web on :5310, local PostgreSQL (no Docker)
scripts/smoke-auth.sh             # against the running authservice: the sign-in contract
scripts/run-local-stack.sh down
dotnet run --project src/PostmanClone.Web   # the Web on http://localhost:5310; backends not running show as unreachable

# The browser journeys (ADR-0018). Two runs, because the sender's loopback switch is part of the local stack:
E2E_SENDER_LOOPBACK=1 scripts/run-local-stack.sh up
dotnet test --project tests/PostmanClone.Web.E2E/PostmanClone.Web.E2E.csproj -c Release --filter "Mode!=strict"
scripts/run-local-stack.sh down
scripts/run-local-stack.sh up
dotnet test --project tests/PostmanClone.Web.E2E/PostmanClone.Web.E2E.csproj -c Release --filter "Mode!=loopback"
scripts/run-local-stack.sh down
```

The SDK is pinned by `global.json` (.NET 10.0.100, `rollForward: latestFeature`). Package versions
live only in `Directory.Packages.props`; projects reference packages without a version.

## Rules that are easy to break

- **Frozen contracts.** `CONTRACTS.md` is frozen. Do not rename, remove, tighten a limit or change a
  status code. The only allowed change is an optional field or a new endpoint, appended under
  `## Amendments` in the same pull request. A breaking need is `BLOCKED: contract`, not a silent change.
- **Tenant isolation.** The workspace comes from the token's `organization` claim and nowhere else:
  never from a route, a query or a body. Every endpoint that takes an id loads the row, compares its
  workspace, and answers `404 not_found` when the row is absent or belongs to another workspace. Never
  `403` for a foreign row. Every id endpoint has a scoping test named `Scoping_<METHOD>_<route>`.
- **Tokens never reach browser JavaScript**, component fields the framework persists, logs, URLs or error
  messages. The browser holds an HttpOnly session cookie; the Web keeps the token pair on the server
  ([ADR-0005](docs/decisions/0005-bff-session.md)). Antiforgery protects form posts.
  One exception, by design: the agent's **pairing token** (not the session token) is passed to `agent-client.js` for each call to the
  user's own agent on loopback. It is held in the circuit, never stored in the browser, never rendered and never in a URL
  ([ADR-0026](docs/decisions/0026-local-agent-and-fly-estate.md), [ADR-0028](docs/decisions/0028-web-agent-client.md)).
- **Secrets never enter the tree.** Secret variables are encrypted at rest and never returned by a
  `GET`; the only plaintext is the `resolve` call for ExecutionService
  ([ADR-0006](docs/decisions/0006-secret-variables.md)). Local secrets come from
  `scripts/gen-dev-secrets.sh` into the gitignored `.local-stack/`. A test fixture that looks like a
  secret carries a `gitleaks:allow` comment.
- **Masking.** Secrets appear as `••••••` in the UI, the history, the logs and every URL shown back.
  The `Authorization`, `Cookie`, `Set-Cookie`, `X-Api-Key` and `X-Auth-Token` headers, and any name
  containing `token`, `secret`, `password` or `apikey`, are masked (CONTRACTS §4).
- **Logging.** Log the method, the host, the status, the sizes and the durations. Never log a request
  or response body, a header value or a variable value.
- **Rate limits.** Every endpoint that takes a credential or writes carries a policy (`RequireRateLimiting`). The sign-in forms share
  the `auth` window per address; the Web's window is `RateLimiting:AuthPermitsPerMinute`.
- **Time.** Inject `TimeProvider`. No test depends on today's date or pins a calendar date; use
  `FakeTimeProvider` relative to now.
- **Schema.** EF Core migrations (Npgsql), applied by `MigrateAsync` after Kestrel starts. Never
  `EnsureCreated`, except on the InMemory test path. The model must have no pending changes against the
  last migration snapshot.
- **Program.cs is a manifest** (under about 120 lines). Wiring lives in extension methods. Interfaces and
  DI, not base classes. One `HttpClient` resilience handler, the kernel's.
- **Tests assert something real.** No `Assert.True(true)`, no guard-then-return. An unfinished test is
  `[Fact(Skip = "reason")]` and says so under "Not verified".
- **The kernel stays small.** `PostmanClone.ServiceDefaults` is at most 800 lines, with no entities,
  DTOs, enums or user-facing strings in it.
- **Web.** Interactive server render mode; colours only through CSS tokens; both themes; works at 390 px
  width; no JavaScript interop and no JavaScript or CSS library ([ADR-0009](docs/decisions/0009-dependency-budget.md)), except the one hand-written script `agent-client.js` that calls the user's local agent ([ADR-0028](docs/decisions/0028-web-agent-client.md)).
- **Single instance.** The Web runs as one instance, and sessions live in its memory
  ([ADR-0008](docs/decisions/0008-single-instance-web.md)).
- **Deployment** ([ADR-0007](docs/decisions/0007-deployment-out-of-scope.md) is superseded for this product by
  [ADR-0026](docs/decisions/0026-local-agent-and-fly-estate.md); [ADR-0029](docs/decisions/0029-fly-topology-and-manual-deployment.md)). The Fly
  configuration is `flyio/`. The only deploy path is `.github/workflows/flyio.yml`, which has `workflow_dispatch` as its only trigger;
  `scripts/check-flyio-trigger.sh` enforces it, and `scripts/check-flyio-config.py` checks the configs without a Fly account. The steps are in
  `docs/FLY_DEPLOYMENT.md`. No tag trigger, no push trigger, no registry push outside that workflow, and no other cloud configuration.
- **The local agent** sends requests to local and private targets from the user's computer (ADR-0026). It is the only place a resolved
  secret value may be held outside the service, and only in memory for one send. Its local listener is `127.0.0.1` only, and it checks `Origin`,
  `Host` and the pairing token on every request (CONTRACTS A2.6).

## Workflow

- Branch `wp/NN-slug` from `main`. One work package per branch, one pull request.
- Pull requests use `.github/pull_request_template.md`. The `pr-validation` check must be green.
  `secret-scan` runs alongside it.
- Commit messages: an area prefix (`docs:`, `build:`, `ci:`, `auth:`, `collections:`, `environments:`,
  `exec:`, `web:`, `test:`), an imperative summary, and a body that says why.
- Never rewrite published history and never push tags. Do not change repository settings; list the owner
  actions in the pull request body instead.
- Record every departure from architecture-standards in `docs/architecture/DEVIATIONS.md`.
