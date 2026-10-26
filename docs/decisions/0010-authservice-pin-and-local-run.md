# ADR-0010: authservice is pinned to v0.3.4, and runs locally from its tagged source when there is no Docker

- **Status:** Accepted
- **Date:** 2026-10-26
- **Work package:** WP03

## Context

[ADR-0004](0004-authservice-identity-and-workspace.md) makes authservice the only identity provider.
The AppHost runs it as a container. Sessions and some developer machines have no Docker daemon, and
the E2E suite needs a running authservice without Aspire (the plan's §6, "E2E runs against
`scripts/run-local-stack.sh`"). So the same version must run two ways, and the two must not drift.

## Options considered

1. **Pull the image only** (`ghcr.io/konradcinkusz/authservice`). Rejected as the only route: it needs
   Docker, and sessions have none.
2. **Copy authservice code into this repository.** Rejected by the plan and ADR-0004. It forks the identity
   service and hides its upgrades.
3. **Clone the pinned tag, build it, and run it with `dotnet`** for local work, and keep the container for
   the AppHost. Chosen. Both routes use the same tag, and the local route needs only the .NET SDK and
   PostgreSQL.

## Decision

- The version is `v0.3.4`. It is written once in `src/PostmanClone.AppHost/Resources/AuthServiceResource.cs`
  (`ImageTag`) and once in `scripts/run-local-stack.sh` (`AUTHSERVICE_TAG`). A change to either is a change to
  the pin, made in the same pull request.
- The AppHost runs the container with `Database__SchemaMode=Migrate` and
  `Database__MigrationsAssembly=AuthService.Migrations.PostgreSQL`, and signs with RS256 from a secret
  parameter (`authservice-jwt-private-key-pem`). Its health probe is `/health/ready`.
- `scripts/run-local-stack.sh up` clones the tag into the gitignored `.local-stack/authservice-src`, builds it
  into `.local-stack/authservice-bin`, and starts it on port 5300 against the local PostgreSQL. Secrets come
  from `scripts/gen-dev-secrets.sh`, which writes `.local-stack/secrets.env` and never prints a value.
- authservice code is never copied into this repository. The clone is a build input, read-only.
- Issuer and audience are `PostmanClone`, so a token from another product cannot pass here.
- `scripts/smoke-auth.sh` checks the contract this repository depends on (see [docs/AUTH.md](../AUTH.md)). It runs
  in CI as the `auth-smoke` job against a PostgreSQL service container.

## Consequences

- Upgrading authservice is a deliberate change: bump the tag in two places, run `auth-smoke`, and record
  any contract difference in `docs/AUTH.md` and `CONTRACTS.md` §Amendments.
- The local route builds authservice from source, so it needs the .NET 10 SDK and network access to GitHub
  and NuGet. The container route needs Docker and access to `ghcr.io`.
- Until the image is pullable by CI and developers, the AppHost route is unverified. If the package is
  private, the owner has to grant access (an owner action listed in the plan, §6).

## Revisit when

authservice's HTTP contract changes in a way that cannot be expressed as an amendment, or the
deployment decision (ADR-0007) is reopened.
