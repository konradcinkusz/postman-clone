# postman-clone

An API client in the spirit of Postman, written from scratch as a **Blazor Web App (interactive server, no
WebAssembly, no Node)**. It has three domain services behind a server-side session, an authentication
service ([`authservice`](https://github.com/konradcinkusz/authservice), pinned to v0.3.4), a sender with SSRF
protection, and an Aspire AppHost for development. It is built to
[`architecture-standards`](https://github.com/konradcinkusz/architecture-standards).

Deployment is deliberately absent ([ADR-0007](docs/decisions/0007-deployment-out-of-scope.md)).

## Status

The work packages are in [`docs/implementation-plan/WORK-PACKAGES.md`](docs/implementation-plan/WORK-PACKAGES.md).
This page describes `main` at WP27.

| Area | State |
|---|---|
| Sign-in, register, sign-out, workspace resolution (BFF session) | Built (WP12). Browser sign-in works (WP18b, OI-01 fixed). |
| Members page and invitations | Built (WP24) |
| Collections, folders, saved requests API | Built (WP08, WP13) |
| Postman v2.1 and cURL import and export, API and UI | Built (WP16, WP17, WP23) |
| Environments and secret variables API | Built (WP09). EnvironmentsService is not in the no-Docker stack (OI-13). |
| Sender (`POST /api/v1/execute`) with SSRF guard, redirects and caps | Built (WP10, WP11, WP14) |
| History API (masked, 200 entries per user and workspace) | Built (WP15) |
| Workspace shell, collection tree, request builder, response viewer | Built (WP18, WP18b, WP19, WP20) |
| Environments page, history page | In progress (WP21, WP22). Placeholders on `main`. |
| About page | Placeholder |
| Legal pages (`/legal/privacy`, `/legal/terms`) | Placeholders (OI-04) |
| Browser end-to-end tests | Built (WP26): `tests/PostmanClone.Web.E2E`, 17 journeys over the protected flows, run by the `e2e` CI job; ADR-0018 |
| Final review and the review document | Not started (WP28) |

Known open items, with their evidence, are in [`docs/architecture/OPEN-ITEMS.md`](docs/architecture/OPEN-ITEMS.md).
The one that blocks deployment is OI-02: the sign-in and invitation forms are not rate-limited by this application.

## What you can do today

- **Sign in** through authservice; the browser holds only an HttpOnly cookie, the tokens stay on the server.
- **Work in one workspace** (an authservice organization). Members share its collections and environments.
- **Collections**: create, rename, delete; folders (depth 8); saved requests; move, duplicate; the tree in the shell.
- **Build and save a request**: method, URL with query sync, headers, body (raw, form fields), auth helpers that take
  `{{variable}}` references only.
- **Send a request** from the builder: the sender runs on the server, with redirects, timeouts and size caps. The viewer
  shows status, timing, headers and the body (pretty or raw); binary bodies are not rendered.
- **Import** a Postman v2.1 file or a cURL command into a collection; **export** a collection as Postman v2.1, and a
  request as cURL.
- **History**: each send is recorded with the masked request and result; the API lists, reads, deletes and clears it.
- **Members**: an Owner or Admin invites by email; the invitee accepts at `/accept-invitation`.
- **Secrets**: environment variables marked secret are encrypted at rest and never returned by a `GET`.

Every route, the limits and the error codes are in [`docs/implementation-plan/CONTRACTS.md`](docs/implementation-plan/CONTRACTS.md).

## Run it locally

### Without Docker (the route the sessions use)

Prerequisites: .NET SDK 10, PostgreSQL 16 at the default `PG_CONNECTION`, and `git`, `openssl`, `psql`, `curl`, `base64`.

```bash
scripts/install-hooks.sh                  # once per clone: the pre-commit secret scan (needs gitleaks)
scripts/run-local-stack.sh up             # authservice (5300), CollectionsService (5301), ExecutionService (5303)
scripts/smoke-auth.sh                     # the authservice contract the services rely on
env services__authservice__http__0=http://localhost:5300 \
    services__collections-service__http__0=http://localhost:5301 \
    services__execution-service__http__0=http://localhost:5303 \
    services__environments-service__http__0=http://localhost:5302 \
    Auth__MetadataAddress=http://localhost:5300/.well-known/openid-configuration \
    ASPNETCORE_ENVIRONMENT=Development \
    dotnet run --project src/PostmanClone.Web      # the Web on http://localhost:5310
scripts/run-local-stack.sh down           # stops the services; the databases are kept
```

The Web needs those discovery and metadata settings because outside the AppHost nothing resolves the service names.
EnvironmentsService is not started by the script, so a send that names an environment answers `502`.
[`docs/OPERATIONS.md`](docs/OPERATIONS.md#4-running-locally) has the details, the logs, and the configuration reference.

### With the AppHost (needs Docker)

```bash
dotnet run --project src/PostmanClone.AppHost
```

It starts PostgreSQL, the authservice container, the three services and the Web on port 5310. It needs the two AppHost
parameters described in [OPERATIONS.md §3](docs/OPERATIONS.md#3-secrets-and-how-to-generate-them). **The AppHost has never
been run in a development session**, because sessions have no Docker daemon.

## Test it

```bash
dotnet build PostmanClone.sln                                  # warnings are errors
TEST_POSTGRES="Host=localhost;Username=postgres;Password=postgres" \
    dotnet test --solution PostmanClone.sln                    # PostgreSQL-backed tests run only with TEST_POSTGRES
bash scripts/check-doc-links.sh
bash scripts/check-kernel-size.sh
```

On 2026-10-31 (WP27, merged tree): 1927 tests in the run, 1926 passed, 1 skipped, in Debug and in Release. One
test is timing-dependent when run alone in Release (OI-06). What each layer proves, what CI runs, and what has never
run are in [`docs/TESTING.md`](docs/TESTING.md).

## Layout

```
src/PostmanClone.AppHost/              the Aspire composition root (development, needs Docker)
src/PostmanClone.ServiceDefaults/      the shared kernel: health, telemetry, JWT, rate limits, origin guard, database plumbing
src/PostmanClone.CollectionsService/   collections, folders, saved requests, Postman and cURL import and export
src/PostmanClone.EnvironmentsService/  environments, secret variables (AES-256-GCM), the resolve call
src/PostmanClone.ExecutionService/     the sender (SSRF guard, assembly, redirects, caps) and the history
src/PostmanClone.Web/                  the Blazor Web App: session (BFF), workspace shell, builder, viewer, import and export UI
tests/                                 one test project per service, the Web, the kernel, TestKit
docs/                                  this page's companions: ARCHITECTURE, OPERATIONS, TESTING, PRIVACY, AUTH, decisions, architecture
scripts/                               run-local-stack.sh, smoke-auth.sh, gen-dev-secrets.sh, install-hooks.sh, hooks/, check scripts
.github/                               CI (pr-validation, secret-scan), CODEOWNERS, Dependabot, templates
```

The kernel holds no domain (rule R1), and no service references another (R2); both are tests, see
[ARCHITECTURE.md §8](docs/ARCHITECTURE.md#8-architecture-rules-as-tests).

## Documentation

| Document | What it covers |
|---|---|
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Topology, bounded contexts, the sign-in, send and import flows, the secret flow |
| [`docs/OPERATIONS.md`](docs/OPERATIONS.md) | Configuration reference, secrets, running locally, health, what degrades |
| [`docs/TESTING.md`](docs/TESTING.md) | Layers, counts, what CI runs, what has never run, manual checks |
| [`docs/PRIVACY.md`](docs/PRIVACY.md) | Personal data per store, masking, retention, erasure (incomplete) |
| [`docs/AUTH.md`](docs/AUTH.md) | How sign-in works with authservice v0.3.4, verified facts and differences from the contract |
| [`docs/architecture/COMPLIANCE.md`](docs/architecture/COMPLIANCE.md) | The reference-architecture checklist, item by item |
| [`docs/architecture/OPEN-ITEMS.md`](docs/architecture/OPEN-ITEMS.md) | Open items, owner actions, residual risks |
| [`docs/architecture/DEVIATIONS.md`](docs/architecture/DEVIATIONS.md) | Each departure from the standards, dated, with its reason |
| [`docs/architecture/UI-STYLE.md`](docs/architecture/UI-STYLE.md) | The design contract of the Web |
| [`docs/implementation-plan/`](docs/implementation-plan/README.md) | The build plan: scope, contracts, protocol, work packages |
| [`AGENTS.md`](AGENTS.md) | The rules that are easy to break, for coding agents and people |

## Decisions

| ADR | Decision |
|---|---|
| [0001](docs/decisions/0001-product-scope.md) | Product scope and non-goals |
| [0002](docs/decisions/0002-blazor-web-app-over-nextjs.md) | Blazor Web App (interactive server) over Next.js; WebAssembly rejected |
| [0003](docs/decisions/0003-service-boundaries.md) | Three domain services and a Web, one database per service |
| [0004](docs/decisions/0004-authservice-identity-and-workspace.md) | authservice is the only IdP; a workspace is one organization |
| [0005](docs/decisions/0005-bff-session.md) | Backend-for-frontend session: opaque HttpOnly cookie, tokens on the server |
| [0006](docs/decisions/0006-secret-variables.md) | Secret variables: encrypted at rest, referenced by name, masked everywhere |
| [0007](docs/decisions/0007-deployment-out-of-scope.md) | Deployment is out of scope for this build |
| [0008](docs/decisions/0008-single-instance-web.md) | The Web keeps sessions in server memory and runs as one instance |
| [0009](docs/decisions/0009-dependency-budget.md) | Dependency budget |
| [0010](docs/decisions/0010-authservice-pin-and-local-run.md) | authservice pinned to v0.3.4; run from its tagged source when there is no Docker |
| [0011](docs/decisions/0011-sender-threat-model.md) | Sender threat model and the SSRF guard |
| [0012](docs/decisions/0012-variable-semantics.md) | Variable semantics and request assembly |
| [0013](docs/decisions/0013-circuit-lifetime-and-session-store.md) | Circuit lifetime and session store |
| [0014](docs/decisions/0014-execution-limits-and-redirects.md) | Execution limits, redirects and the send path |
| [0015](docs/decisions/0015-history-retention-and-masking.md) | History retention, masking and the stored request |
| [0016](docs/decisions/0016-import-threat-model.md) | Import threat model for Postman collections |
| [0017](docs/decisions/0017-builder-state-model.md) | Builder state model |
| [0020](docs/decisions/0020-architecture-fitness-tests.md) | Architecture and tenancy rules as tests |
| [0021](docs/decisions/0021-import-export-in-the-web.md) | Import and export in the Web |
| [0022](docs/decisions/0022-referrer-policy-same-origin.md) | Referrer-Policy same-origin, so browser sign-in passes the origin guard |
| [0018](docs/decisions/0018-e2e-scope-and-charter.md) | End-to-end journeys: scope, the two sender modes, and what the suite may not do |
| [0023](docs/decisions/0023-response-viewer-rendering.md) | Response viewer rendering |

Number 0019 (review outcomes) is reserved for WP28. The index is in
[`docs/decisions/README.md`](docs/decisions/README.md).

## Working on it

Read [`AGENTS.md`](AGENTS.md) first: it lists the rules that are easy to break (tenant isolation, tokens, secrets,
masking, frozen contracts). Commits use an area prefix (`web:`, `exec:`, `docs:` …) and say why.

The pre-commit hook refuses a commit when no secret scanner runs, by design. Install gitleaks, or Docker for the
gitleaks image. On 2026-10-31, gitleaks v8.19.0 built from source failed the hook's `git --staged` form, and the
latest module release passed it (OI-05). `git commit --no-verify` bypasses the hook; do not use it.

## Security

See [`SECURITY.md`](SECURITY.md). Do not report vulnerabilities in a public issue.

## Licence

MIT. See [`LICENSE`](LICENSE).
