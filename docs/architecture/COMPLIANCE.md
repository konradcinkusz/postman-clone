# Compliance with the reference architecture

This page answers the checklist of [architecture-standards §3](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/architecture/00-REFERENCE-ARCHITECTURE.md#3-compliance-checklist)
item by item, for the state of `main` after WP28 (the final review; [ARCHITECTURE_REVIEW.md](../ARCHITECTURE_REVIEW.md) has the reasoning).
Each row gives a status, the file that is the evidence, and the gap or the reason for N/A. Status legend: **Met** (the evidence holds in the
code and a test or check covers it), **Partly** (a part is missing or unverified), **Not met** (a deliberate or accidental gap),
**N/A** (the item does not apply, with the reason).

The checklist is written per service. The services are CollectionsService, EnvironmentsService and ExecutionService, plus the Web
(which has no database), so a row answers for all of them unless it says otherwise.

Open items are in [OPEN-ITEMS.md](OPEN-ITEMS.md) and are cited by their IDs here. Deviations are recorded in [DEVIATIONS.md](DEVIATIONS.md).

| # | Item | Status | Evidence | Gap, or why N/A |
|---|---|---|---|---|
| 1 | Declared in the AppHost with `WithReference`, `WaitFor`, `WithHttpHealthCheck` | Met (AppHost never run) | `src/PostmanClone.AppHost/Resources/CollectionsServiceResource.cs`, `EnvironmentsServiceResource.cs`, `ExecutionServiceResource.cs`, `AuthServiceResource.cs`, `WebResource.cs` (health check added in WP28) | The AppHost has never been run here (OI-19). |
| 2 | Calls `AddServiceDefaults()` and `MapDefaultEndpoints()` | Met | each `src/PostmanClone.*/Program.cs` | Checked by architecture rule R4 (`tests/PostmanClone.ServiceDefaults.Tests/Architecture/`). |
| 3 | Exposes `/health` and `/alive`; the platform health check points at `/health` | Met (platform check N/A) | `src/PostmanClone.ServiceDefaults/Health/` | No platform health check: there is no deployment (ADR-0007). |
| 4 | Emits OTLP traces, metrics and logs | Partly | `src/PostmanClone.ServiceDefaults/Telemetry/OpenTelemetryExtensions.cs` registers logs, traces and metrics; `UseOtlpExporter` only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set; the sender's targets are not traced (`OutboundTelemetry.cs`, DEVIATIONS) | The exporter has no test and was never run against a collector (OI-18, ARCHITECTURE_REVIEW AR-05). |
| 5 | Owns its database; no other service connects to it | Met | `src/PostmanClone.AppHost/Program.cs` (one database per service); rule R2 | The four databases are on one PostgreSQL server, as P3 allows. |
| 6 | Schema applied by `MigrateAsync` from provider migrations, in a hosted service | Met | `src/PostmanClone.ServiceDefaults/Database/MigrationHostedService.cs`; migrations in `CollectionsService/Migrations`, `EnvironmentsService/Migrations`, `ExecutionService/Migrations`; rule R7 | The InMemory `EnsureCreated` branch is the only other path (R7 checks it). |
| 7 | All configuration from environment variables; no secret in source; secret scanner in CI | Partly | `docs/OPERATIONS.md` §2–3; `.github/workflows/secret-scan.yml`; `scripts/hooks/pre-commit`; `.gitleaks.toml`; `.gitignore` (`.local-stack/`, `secrets.env`) | The hook runs with gitleaks `v8.28.0` (documented; verified in WP28). CI's image is `v8.19.0`, not run here (OPEN-ITEMS §0, owner). |
| 8 | Exactly one service holds a signing key; all others validate against its JWKS | Met | `AuthServiceResource.cs` passes `Jwt__PrivateKeyPem` to authservice only; the services get `Auth__MetadataAddress` | Production key custody is an owner decision (ADR-0007). |
| 9 | The kernel holds no entity, DTO, enum, seed dataset, pricing constant or user-facing string | Met | rule R1; `scripts/check-kernel-size.sh` (766 of 800 lines, WP28) | The kernel is close to its ceiling (ARCHITECTURE_REVIEW AR-08). |
| 10 | Every optional integration has a working no-op or fallback | Met | `src/PostmanClone.EnvironmentsService/Secrets/UnavailableSecretProtector.cs`; OTLP is a no-op when unset | SSRF protection is not optional; its relaxations are refused outside Development (WP28). |
| 11 | The health endpoint reports every optional integration; the startup banner prints the same list | Met | `src/PostmanClone.ServiceDefaults/Integrations/OptionalIntegrationHealthCheck.cs`; `Startup/StartupBanner.cs`; `StartupBannerTests` | CollectionsService has no optional integration, so its list is empty. |
| 12 | Multi-stage Dockerfile; runtime image major equals the TFM major; listens on `:8080`; non-root | Met (not built here) | `src/*/Dockerfile` (four files); rule R8 | No Docker daemon in sessions: no image was built here. CI's `docker-build` job is the first build. |
| 13 | One `fly.toml`; `min_machines_running = 1` if another service calls it in-request | N/A | none | Deployment is out of scope on `main` (ADR-0007). Planned, not built: a manual Fly workflow with the two services that call in-request kept at `min_machines_running = 1` (ADR-0026, WP33, DEVIATIONS 2026-11-01). |
| 14 | Outbound `HttpClient`s carry the standard resilience handler with explicit timeouts | Met, with a recorded exception | `src/PostmanClone.ServiceDefaults/Extensions.cs:33-56` (every factory client; retries off for unsafe methods, ADR-0023); `EnvironmentsClientOptions.cs` sets 10 s; the Web's execution client has its own timeout | The sender (`ExecutionService/Sending/Execution/RequestExecutor.cs`) has no resilience handler on purpose: a user's request is not resent (ADR-0019 §8, DEVIATIONS). |
| 15 | `Program.cs` is a manifest; wiring is in `ServiceCollectionExtensions` | Met | `Program.cs` files are 19–28 lines (WP28 count); wiring in `ServiceCollectionExtensions.cs` and `*Extensions.cs`; rule R4 | |
| 16 | Extension points are interfaces registered in DI, not base classes | Met | `IHistoryRecorder`, `IHostResolver`, `IAddressDialer`, `ISecretProtector`, `IOptionalIntegration` | `grep 'abstract class' src` finds none. |
| 17 | Has a test project; the logic-bearing layer is covered; characterisation tests before any migration | Met | five test projects; 2067 tests in the solution run (TESTING.md §2–3) | No behaviour is being migrated, so the characterisation part is N/A. |
| 18 | Built by the tag-driven workflow with path-based change detection | Not met (by decision) | `.github/workflows/pr-validation.yml`; `secret-scan.yml` (no tag trigger) | Not met on `main`. Planned as a manual `workflow_dispatch` only, with no tag trigger (ADR-0026 Decision 10, WP33; DEVIATIONS 2026-11-01). ARCHITECTURE_REVIEW AR-04 describes the state at WP28. | |
| 19 | Architectural decisions recorded in `docs/` | Met | `docs/decisions/`: ADR-0001 to ADR-0025 (0019 is WP28's review outcomes); `docs/architecture/DEVIATIONS.md` | |

## Other standards this page applies

These come from the same standards repository. They are not part of the §3 checklist, but the review covers them.

| Standard | Status | Evidence and gap |
|---|---|---|
| Security headers on every frontend (SECURITY-REVIEW §4): CSP, `X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy` | Met (HSTS aside, OI-20) | All four are sent (`ServiceDefaults/Security/SecurityExtensions.cs:12-17`, `Web/Hosting/ContentSecurityPolicyMiddleware.cs:13-14`). The CSP has no `unsafe-inline`. `Referrer-Policy` is `same-origin` (ADR-0022). |
| Strict-Transport-Security | Not met | Not set anywhere. Right for a plain-HTTP dev stack; a TLS edge must set it (OI-20, owner). |
| Tokens never in browser JavaScript, storage, or persisted component state (SECURITY-REVIEW §4) | Met | No `localStorage`, `sessionStorage` or JS interop in `src/PostmanClone.Web` (`grep`). The cookie holds 32 random bytes; `HttpOnly`, `SameSite=Strict`. |
| Rate limits (SECURITY-REVIEW §9.3): every endpoint that takes a credential or writes | Met in code | Sign-in, register and accept-invitation: `auth` (per address). Invitations and workspace creation: `api` (per user). Every service API group: `api` or `execute` (`grep RequireRateLimiting`). The edge window is an owner item (OI-26). |
| CORS (SECURITY-REVIEW §9.1) | N/A | The services have no CORS: only the Web calls them, server to server. No browser calls a service directly. |
| Documentation staleness (REPO-BASELINE §8): one source of truth per variable | Partly | `docs/OPERATIONS.md` §1 names the authority for each place. The AppHost and the script still duplicate `Auth__*` values (OI-14). |
| Per-project dependency counts (REPO-BASELINE §9) | Met | `PackageReference` counts in the `.csproj` files: ServiceDefaults 10, CollectionsService 3, EnvironmentsService 2, ExecutionService 1, AppHost 2, Web 0 (framework reference only), test projects 3–6 each. Central versions in `Directory.Packages.props`. |
| Vulnerable-package audit | Met | `dotnet list PostmanClone.sln package --vulnerable --include-transitive`: none reported (WP28). |
| Testing: each test asserts something real; no fixed sleeps | Met | No `Assert.True(true)`. Waits are bounded polls with a deadline (`TelemetryTests`, `AuthBudgetWindowTests`). |
| Testing: the four human-only situations | N/A for all four | TESTING.md §7. |
