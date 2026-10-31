# ADR-0020: Architecture and tenancy rules as tests

- **Status:** Accepted
- **Date:** 2026-10-31
- **Work package:** WP25

## Context

The estate's rules are stated in prose: services share no database (P3), the kernel holds no domain (P2), `Program.cs` is a
manifest (P9), every id endpoint is scoped to the caller's workspace (CONTRACTS §0, the security review), no service adds a
second HTTP resilience handler (P2), and package versions are exact. Until WP25 nothing checked them. A review finds a
violation only when someone reads the right file, and a rule that can never fail is indistinguishable from one that passes.

## Decision

`tests/PostmanClone.ServiceDefaults.Tests/Architecture/` holds ten rules (R1–R10, numbered as in WP25), each a pure function
over plain data in `ArchitectureRules.cs`:

| Rule | Checked by | Input |
|---|---|---|
| R1 kernel holds no domain | project references, referenced assemblies, declared namespaces | csproj, loaded kernel assembly |
| R2 services isolated | project references, referenced assemblies, source text outside comments | csproj, loaded service assemblies, sources |
| R3 Web references only the kernel | project references, referenced assemblies | csproj, loaded Web assembly |
| R4 manifests | line count ≤ 150, calls `AddServiceDefaults` and `MapDefaultEndpoints` | `Program.cs` text |
| R5 routes authorized | endpoint metadata and the host's fallback policy | `EndpointDataSource` of each host |
| R6 isolation matrix | every `{param}` route has a `Scoping_<METHOD>_<route>` test | the route table, the test sources |
| R7 no `EnsureCreated` outside InMemory | each call preceded by an `IsInMemory` check | source text |
| R8 Dockerfiles (P6) | base image major equals the TargetFramework major, `EXPOSE 8080`, a named non-root `USER` | Dockerfile text |
| R9 one resilience handler | `AddStandardResilienceHandler` in the kernel, exactly once | source text |
| R10 exact versions | `Directory.Packages.props` and project files | props and csproj text |

Every rule has two facts: it holds on the repository, and it fails on a deliberately broken sample held in memory. The broken
samples are never committed as code, so the repository stays green and each rule is still shown to bite.

The route table is read from each composition root, not from source: the four roots are referenced with extern aliases (each
is a global `Program`), hosted in memory in the Production environment with a dead authority address, and their
`EndpointDataSource` is read. No socket is opened and no backend is contacted.

### Decisions inside the rules

1. **R6 reads test sources, not compiled test assemblies.** The architecture project references no test project, so a service
   test project can change without rebuilding the architecture project. A `Scoping_` method declared in a comment does not count.
2. **R5 accepts the framework's own endpoints.** Static web assets (their `StaticAssetDescriptor`), the static-file fallback
   (`FallbackMetadata`) and the Blazor runtime (`/_blazor`, `/_framework/`) are classified by metadata and are not judged.
   The framework grants them anonymously by design: a circuit is established before any component checks its own
   authorization, and asset file names carry a build fingerprint, so an allow-list by name would fail on every build. Every
   application route is still judged.
3. **The anonymous allow-list is short and named.** `/health` and `/alive` (the kernel), the Web's form endpoints `/sign-in`,
   `/register` and `/sign-out` (antiforgery-protected, ADR-0005), and the public legal pages `/legal/privacy` and
   `/legal/terms`. An anonymous route outside that list fails R5.
4. **CollectionsService states the kernel in its manifest.** Its `Program.cs` now calls `AddServiceDefaults` and
   `MapDefaultEndpoints` itself, like the other services, so R4 can read them there. Its `/api/v1` group also calls
   `RequireAuthorization`, so its trust level is visible in the route table. Under the kernel's fallback policy the
   behaviour is unchanged: an unauthenticated request was already refused.
5. **Scoping gaps found by R6 are added, not allow-listed.** The first run found none: every id route already has a
   scoping test with the matching name.

## Consequences

- A new service, route or Dockerfile is judged by the tests with no change to them, unless it is a new allow-list entry or a
  new framework-owned prefix. Either must be added in a reviewed change, which is the point.
- The architecture tests boot four hosts, which adds a few seconds to a test run. The hosts use the InMemory provider and
  start no listener.
- R6 relies on the naming convention. A scoping test with the right name but no assertions still passes R6. The per-test
  quality bar (TESTING-STRATEGY) is a review question, not a structural one.
- R8 duplicates the single-service Dockerfile check in ExecutionService's tests. The duplicate is kept: the architecture
  tests cover every image, the service test covers its own image's content in more detail.

## Revisit when

- A service needs a route that is neither authorized nor a framework endpoint (the allow-list grows, or the rule changes).
- The Web adds a second render path, or a static-asset path that does not carry the descriptor metadata.
- The architecture-standards repository changes P2, P3, P6 or P9.
