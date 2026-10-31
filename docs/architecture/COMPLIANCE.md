# Compliance with the reference architecture

This page answers the checklist of [architecture-standards §3](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/architecture/00-REFERENCE-ARCHITECTURE.md#3-compliance-checklist)
item by item, for the state of `main` at WP27. Each row gives a status, the file that is the evidence, and
the gap or the reason for N/A. Status legend: **Met** (the evidence holds in the code and a test or check
covers it), **Partly** (a part is missing or unverified), **Not met** (a deliberate or accidental gap), **N/A**
(the item does not apply, with the reason).

The checklist is written per service. The services are CollectionsService, EnvironmentsService and
ExecutionService, plus the Web (which has no database), so a row answers for all of them unless it says otherwise.

Open items are in [OPEN-ITEMS.md](OPEN-ITEMS.md) and are cited by their IDs here. Deviations are recorded in
[DEVIATIONS.md](DEVIATIONS.md).

| # | Item | Status | Evidence | Gap, or why N/A |
|---|---|---|---|---|
| 1 | Declared in the AppHost with `WithReference`, `WaitFor`, `WithHttpHealthCheck` | Partly | `src/PostmanClone.AppHost/Resources/CollectionsServiceResource.cs:34-36`, `EnvironmentsServiceResource.cs:39-41`, `ExecutionServiceResource.cs:27-35`, `AuthServiceResource.cs:43-44`, `WebResource.cs:23-25` | The Web has `WaitFor` but no `WithHttpHealthCheck`. The AppHost has never been run here (OI-19). |
| 2 | Calls `AddServiceDefaults()` and `MapDefaultEndpoints()` | Met | each `src/PostmanClone.*/Program.cs` (the four services and the Web) | Checked by architecture rule R4 (`tests/PostmanClone.ServiceDefaults.Tests/Architecture/`). |
| 3 | Exposes `/health` and `/alive`; the platform health check points at `/health` | Met (platform check N/A) | `src/PostmanClone.ServiceDefaults/Health/` (`/health` readiness, `/alive` liveness); observed `/alive` and `/health` 200 on the local stack | No platform health check exists: there is no deployment (ADR-0007). |
| 4 | Emits OTLP traces, metrics and logs | Partly | `src/PostmanClone.ServiceDefaults/Telemetry/OpenTelemetryExtensions.cs:25-43` registers logs, traces and metrics; `UseOtlpExporter` only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set | The exporter branch has no test and was never run against a collector (OI-18). The Release telemetry test is timing-dependent (OI-06). |
| 5 | Owns its database; no other service connects to it | Met | `src/PostmanClone.AppHost/Program.cs:10-13` (one database per service); rule R2 in `ServiceDefaults.Tests/Architecture/` | The four databases are on one PostgreSQL server, as P3 allows. |
| 6 | Schema applied by `MigrateAsync` from provider migrations, in a hosted service | Met | `src/PostmanClone.ServiceDefaults/Database/MigrationHostedService.cs`; migrations in `src/PostmanClone.CollectionsService/Migrations` (3 files), `EnvironmentsService/Migrations` (3), `ExecutionService/Migrations` (5); rule R7 | The InMemory `EnsureCreated` branch is the only other path (R7 checks it). |
| 7 | All configuration from environment variables; no secret in source; secret scanner in CI | Met (with OI-05) | `docs/OPERATIONS.md` §2–3; `.github/workflows/secret-scan.yml`; `scripts/hooks/pre-commit`; `.gitleaks.toml`; `.gitignore` (`.local-stack/`, `secrets.env`) | The local hook needs a gitleaks version that runs its `git --staged` form (OI-05). Test fixtures that look like secrets carry `gitleaks:allow`. |
| 8 | Exactly one service holds a signing key; all others validate against its JWKS | Met | `AuthServiceResource.cs:29,41` passes `Jwt__PrivateKeyPem` to authservice only; the services get `Auth__MetadataAddress` (`CollectionsServiceResource.cs:29`, and the others) | The key is an AppHost parameter, `authservice-jwt-private-key-pem`. Production key custody is an owner decision. |
| 9 | The kernel holds no entity, DTO, enum, seed dataset, pricing constant or user-facing string | Met | rule R1; `scripts/check-kernel-size.sh` (726 of 800 lines, run in this session) | |
| 10 | Every optional integration has a working no-op or fallback | Met | `src/PostmanClone.EnvironmentsService/Secrets/UnavailableSecretProtector.cs` (secrets); OTLP is a no-op when unset (`OpenTelemetryExtensions.cs`) | SSRF protection is not optional; it is always on, and its relaxations are refused in Production (`ExecutionOptionsValidator`). |
| 11 | The health endpoint reports every optional integration; the startup banner prints the same list | Met | `src/PostmanClone.ServiceDefaults/Integrations/OptionalIntegrationHealthCheck.cs` (always healthy, lists the states as data); `Startup/StartupBanner.cs`; `SecretsIntegration.cs`, `ExecutionService/Ssrf/SsrfProtectionIntegration.cs`; `StartupBannerTests` | CollectionsService has no optional integration, so its list is empty. |
| 12 | Multi-stage Dockerfile; runtime image major equals the TFM major; listens on `:8080`; non-root | Met (not built here) | `src/*/Dockerfile` (four files: `sdk:10.0` build stage, `aspnet:10.0` runtime, `EXPOSE 8080`, `USER app`); rule R8 | No Docker daemon in sessions: no image was built here. CI's `docker-build` job is the first build (TESTING.md §4). |
| 13 | One `fly.toml`; `min_machines_running = 1` if another service calls it in-request | N/A | none | Deployment is out of scope (ADR-0007). No platform configuration exists, by decision. |
| 14 | Outbound `HttpClient`s carry the standard resilience handler with explicit timeouts | Partly | `src/PostmanClone.ServiceDefaults/Extensions.cs:31-34` adds the handler to every `HttpClient` from the factory; `EnvironmentsClientOptions.cs` sets `Timeout` 10 s; the sender builds its own `HttpClient` (`ExecutionService/Sending/Execution/RequestExecutor.cs:192-194`) with a 30 s and 60 s timeout and **no retry handler** | The sender's choice is deliberate (a user request is not retried silently), but ADR-0014 does not say so. Recorded in DEVIATIONS (2026-10-31) and in OI-08. |
| 15 | `Program.cs` is a manifest; wiring is in `ServiceCollectionExtensions` | Met | `Program.cs` files are 19–28 lines (run in this session); wiring in `CollectionsService/ServiceCollectionExtensions.cs`, `EnvironmentsService/ServiceCollectionExtensions.cs`, `ExecutionService/ExecutionServiceExtensions.cs`, `Web/Hosting/WebHostExtensions.cs`; rule R4 (≤ 150 lines) | |
| 16 | Extension points are interfaces registered in DI, not base classes | Met | `IHistoryRecorder` (`ExecutionService/Sending/Execution/HistoryDraft.cs`), `IHostResolver`, `IAddressDialer` (`Sending/Ssrf/`), `ISecretProtector` (`EnvironmentsService/Secrets/`), `IOptionalIntegration` (`ServiceDefaults/Integrations/`) | `grep` finds no `abstract class` in `src/`. |
| 17 | Has a test project; the logic-bearing layer is covered; characterisation tests before any migration | Met | five test projects, 1599 tests (TESTING.md §2); tests run green in Debug and Release (TESTING.md §3) | Characterisation tests apply to behaviour being moved; none is being moved (N/A for that part). |
| 18 | Built by the tag-driven workflow with path-based change detection | Not met (by decision) | `.github/workflows/pr-validation.yml`: pull-request CI with a `detect-changes` job that finds inputs, not paths; no tag trigger (`secret-scan.yml` says so explicitly) | ADR-0007 and PROTOCOL forbid tags and deployment. Recorded in DEVIATIONS (2026-10-31). |
| 19 | Architectural decisions recorded in `docs/` | Met | `docs/decisions/` (ADR-0001 to ADR-0016 and ADR-0020; 0017–0019 reserved for WP19, WP26, WP28); `docs/architecture/DEVIATIONS.md` | ADR-0017 (builder state) is not written yet: its work package is not merged. |

## Other standards this page applies

These come from the same standards repository. They are not part of the §3 checklist, but the review is
expected to cover them.

| Standard | Status | Evidence and gap |
|---|---|---|
| Security headers on every frontend (SECURITY-REVIEW §4): CSP, `X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy` | Partly | All four are sent (`ServiceDefaults/Security/SecurityExtensions.cs`, `Web/Hosting/ContentSecurityPolicyMiddleware.cs`). `Referrer-Policy: no-referrer` makes a browser send `Origin: null`, which the origin guard refuses, so browser sign-in fails (OI-01). |
| Strict-Transport-Security | Not met | Not set anywhere (`grep`). Right for a plain-HTTP dev stack; a TLS edge must set it (OI-20). |
| Tokens never in browser JavaScript, storage, or persisted component state (SECURITY-REVIEW §4) | Met | No `localStorage`, `sessionStorage` or JS interop in `src/PostmanClone.Web` (`grep`). Cookie holds 32 random bytes (`ServerSessionStore.NewId`). |
| Documentation staleness (REPO-BASELINE §8): one source of truth per variable | Partly | `docs/OPERATIONS.md` §1 names the authority for each place. The AppHost and the script still duplicate `Auth__*` values (OI-14). |
| Per-project dependency counts (REPO-BASELINE §9) | Met | `PackageReference` counts in the `.csproj` files: ServiceDefaults 10, CollectionsService 3, EnvironmentsService 2, ExecutionService 1, AppHost 2, Web 0 (framework reference only), test projects 3–6 each. Central versions: 22 (`Directory.Packages.props`). |
| Vulnerable-package audit | Met | `dotnet list PostmanClone.sln package --vulnerable --include-transitive`: none reported in this session. |
| Testing: each test asserts something real; no fixed sleeps | Met | No `Assert.True(true)`, no `Thread.Sleep`. One `Task.Delay` waits for a client abort (TESTING.md §1). |
| Testing: the four human-only situations | Met (N/A for all four) | TESTING.md §7. |
