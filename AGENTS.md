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
| `scripts/run-local-stack.sh`, `scripts/gen-dev-secrets.sh` | authservice at its pinned tag without Docker, against a local PostgreSQL; the gitignored `.local-stack/` secrets (WP03) |
| `scripts/smoke-auth.sh` | The authservice contract the services rely on: register, login, organization, refresh, claims (run in CI as `auth-smoke`) |
| `docs/AUTH.md` | How sign-in works through authservice v0.3.4: the flow, the claims, the endpoints read from source, the differences from CONTRACTS §6 |
| `.github/workflows/` | `pr-validation.yml` (the required check), `secret-scan.yml` |
| `Directory.Build.props`, `Directory.Packages.props`, `global.json` | Solution-wide settings, package versions, SDK pin, and the test runner (Microsoft.Testing.Platform) |
| `PostmanClone.sln`, `src/PostmanClone.ServiceDefaults`, `tests/PostmanClone.TestKit`, `tests/PostmanClone.ServiceDefaults.Tests` | The solution and the shared kernel (WP01) |
| `src/PostmanClone.AppHost` | The Aspire composition root, dev only: PostgreSQL with four service databases, authservice (WP03) and EnvironmentsService (WP05) |
| `src/PostmanClone.EnvironmentsService`, `tests/PostmanClone.EnvironmentsService.Tests` | EnvironmentsService (WP05): environments and variables on `environmentsdb`, AES-256-GCM secret protection (ADR-0006), `/health` reports `secrets`. Its Dockerfile is reviewed, not built here (no Docker daemon in sessions) |

Not yet present: the service projects (`src/PostmanClone.CollectionsService`, `ExecutionService`,
`Web`) and their tests. They are in the plan's README, §3.

## Build and test

```bash
dotnet build PostmanClone.sln                  # warnings are errors; restore fails on vulnerable packages
dotnet test --solution PostmanClone.sln         # PostgreSQL-backed tests run when TEST_POSTGRES is set
```

`--solution` is required: `global.json` selects Microsoft.Testing.Platform, and the .NET 10 SDK takes the solution as a flag there.

What runs now:

```bash
bash scripts/check-doc-links.sh   # relative Markdown links resolve
bash scripts/check-kernel-size.sh # the kernel stays within 800 lines
scripts/install-hooks.sh          # once per clone: the pre-commit secret scan
scripts/gen-dev-secrets.sh        # once per clone: .local-stack/secrets.env (never printed)
scripts/run-local-stack.sh up     # authservice v0.3.4 on :5300 against local PostgreSQL (no Docker)
scripts/smoke-auth.sh             # against the running authservice: the sign-in contract
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
  width; no JavaScript interop and no JavaScript or CSS library ([ADR-0009](docs/decisions/0009-dependency-budget.md)).
- **Single instance.** The Web runs as one instance, and sessions live in its memory
  ([ADR-0008](docs/decisions/0008-single-instance-web.md)).
- **Deployment is out of scope** ([ADR-0007](docs/decisions/0007-deployment-out-of-scope.md)). No
  deploy workflow, no registry push, no tags, no Fly.io or cloud configuration.

## Workflow

- Branch `wp/NN-slug` from `main`. One work package per branch, one pull request.
- Pull requests use `.github/pull_request_template.md`. The `pr-validation` check must be green.
  `secret-scan` runs alongside it.
- Commit messages: an area prefix (`docs:`, `build:`, `ci:`, `auth:`, `collections:`, `environments:`,
  `exec:`, `web:`, `test:`), an imperative summary, and a body that says why.
- Never rewrite published history and never push tags. Do not change repository settings; list the owner
  actions in the pull request body instead.
- Record every departure from architecture-standards in `docs/architecture/DEVIATIONS.md`.
