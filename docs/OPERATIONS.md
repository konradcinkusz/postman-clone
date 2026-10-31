# Operations

How the system is configured, started, checked, and what degrades when a part is missing. Every value
below was read from the code or the scripts on `main`; the file named in each row is the one to change.

Deployment is out of scope ([ADR-0007](decisions/0007-deployment-out-of-scope.md)). Nothing here describes a
production environment, and nothing in the repository deploys.

## 1. Single source of truth

Configuration exists in three places. [REPO-BASELINE §8](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/REPO-BASELINE.md)
asks for one authoritative place per variable, so this is the rule:

| Place | Authoritative for | Notes |
|---|---|---|
| `src/PostmanClone.AppHost/Resources/*.cs` | the containerized development topology (Docker) | Sets every service's `Auth__*`, `Secrets__*`, `ConnectionStrings__*`. Authoritative when the two routes disagree. |
| `scripts/run-local-stack.sh` | the no-Docker route (processes on fixed ports) | **Copies** a subset of the AppHost values. A change in one must be made in the other. There is no check that they agree (open item OI-14). |
| `src/*/appsettings*.json` and option classes | defaults that apply everywhere | The option class default is what runs when nothing sets the value. |

## 2. Configuration reference

Keys use .NET configuration syntax. In the environment, `:` becomes `__`.

### Shared by the services (kernel)

| Key | Default | Set by | Meaning |
|---|---|---|---|
| `DATABASE_PROVIDER` | `PostgreSQL` | script, tests (`InMemory`) | `PostgreSQL` or `InMemory`. Any other value fails at startup. `ServiceDefaults/Database/DatabaseExtensions.cs` |
| `ConnectionStrings__collectionsdb` / `environmentsdb` / `executiondb` | none (required) | AppHost (`WithReference`), script | The service's own database. |
| `ConnectionStrings__DefaultConnection` | none (required) | AppHost, script (authservice) | authservice's `authdb`. |
| `Auth__MetadataAddress` | none (required) | AppHost, script | Discovery document of authservice, for the JWKS. Local value: `http://localhost:5300/.well-known/openid-configuration`. |
| `Auth__Issuer` / `Auth__Audience` | none (required) | AppHost, script | Both `PostmanClone`. A token from another product is refused. |
| `Auth__RequireHttpsMetadata` | `true` | AppHost sets `false` (containers on HTTP); script sets `false` | Must stay `true` anywhere that is not the loopback stack. |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | unset | operator | Exports traces, metrics and logs over OTLP when set. Unset means no export. Not exercised by any test (OI-18). |
| `AllowedOrigins` | empty | operator | Origins other than the service's own that the origin guard accepts. Array. `ServiceDefaults/Security/OriginGuardMiddleware.cs` |
| `Proxy:TrustForwardedFor` | `false` | operator | When `true`, the rate limiter reads the client address from `X-Forwarded-For`. Only behind one trusted proxy. |
| `ForwardedHeaders:KnownProxies` | empty | operator | Web only. Addresses whose forwarded headers the Web trusts. |

### CollectionsService and EnvironmentsService

| Key | Default | Meaning |
|---|---|---|
| `Secrets:EncryptionKey` (EnvironmentsService) | none | Base64 of 32 bytes (AES-256-GCM, ADR-0006). Missing or invalid: the service starts, secret writes answer `503 secrets_unavailable`, `/health` reports `secrets: unavailable`. Set by AppHost parameter `environments-encryption-key`. |

### ExecutionService

The options class is `src/PostmanClone.ExecutionService/Options/ExecutionOptions.cs` (section `Execution`).
Each field is a limit of CONTRACTS §4 and can be overridden with `Execution__<Field>`.

| Key | Default | Meaning |
|---|---|---|
| `Execution:RequestBodyMaxBytes` | 1 048 576 | Request body cap. |
| `Execution:MaxHeaders` | 100 | Header rows per request. |
| `Execution:UrlMaxLength` | 8192 | URL length. |
| `Execution:ResponseReadCapBytes` | 5 242 880 | Response body read cap; the rest is `truncated`. |
| `Execution:RefuseDeclaredLengthBytes` | 52 428 800 | A declared `Content-Length` above this is refused without reading. |
| `Execution:DefaultTimeoutSeconds` | 30 | Per-request timeout when the caller gives none. |
| `Execution:MaxTimeoutSeconds` | 60 | Upper bound for a caller's timeout. |
| `Execution:MaxRedirects` | 5 | Redirect hops followed by the sender. |
| `Execution:HistoryRetentionPerUserWorkspace` | 200 | History entries kept per user and workspace; oldest removed on insert. |
| `Execution:HistoryBodyMaxBytes` | 65 536 | Response body kept in a history entry. |
| `Execution:Ssrf:AllowLoopback` | `false` | Lets requests reach loopback. **Refused at startup in Production.** Used by tests and the local stack. |
| `Execution:Ssrf:AllowPrivateRanges` | `false` | Lets requests reach private ranges. **Refused at startup in Production.** |
| `Execution:Ssrf:BlockedPorts` | 22, 25, 110, 143, 465, 587, 993, 995, 3306, 5432, 6379, 9200, 27017 | Destination ports refused. Each must be 1–65535. |
| `Environments:BaseAddress` | `http://environments-service` | Where ExecutionService resolves environments. Service discovery name by default. |
| `Environments:Timeout` | 10 s | Timeout for the resolve call. |

`/health` reports `ssrfProtection`: `enforced`, or `loopback-allowed (non-production)` when a relaxation is on.

### Web

| Key | Default | Meaning |
|---|---|---|
| `Auth:Issuer`, `Auth:Audience`, `Auth:MetadataAddress`, `Auth:RequireHttpsMetadata` | `appsettings.json`: `PostmanClone`, `PostmanClone`, `http://authservice/...`, `true`. Development sets `RequireHttpsMetadata` to `false`. | Sign-in and token verification. |
| `services__authservice__http__0` | unset | Service-discovery address of authservice. Needed to run the Web outside the AppHost (see §4). |
| `services__collections-service__http__0` | unset | Service-discovery address of CollectionsService. Same reason. |
| `ASPNETCORE_ENVIRONMENT` | `Production` (or `Development` from `launchSettings.json`) | Development allows the session cookie over plain HTTP (ADR-0013). |

### authservice (pinned v0.3.4, set by AppHost or the script)

These are authservice's own keys. The values this repository sets are in `src/PostmanClone.AppHost/Resources/AuthServiceResource.cs`
and `scripts/run-local-stack.sh`: `Jwt__PrivateKeyPem` (RS256 PKCS#8 key), `Jwt__Issuer`, `Jwt__Audience`,
`Jwt__PublicBaseUrl`, `FrontendBaseUrl`, `Database__SchemaMode=Migrate`, `Database__MigrationsAssembly`,
`ASPNETCORE_ENVIRONMENT`. [AUTH.md](AUTH.md#configuration-this-repository-sets) explains each one.

### Script and test variables

| Variable | Default | Used by |
|---|---|---|
| `AUTH_PORT` / `COLLECTIONS_PORT` / `EXEC_PORT` / `WEB_PORT` | 5300 / 5301 / 5303 / 5310 | `run-local-stack.sh` |
| `PG_CONNECTION` | `Host=localhost;Username=postgres;Password=postgres` | `run-local-stack.sh`, `smoke-auth.sh`. Development password of a local server, not a credential. |
| `AUTHSERVICE_ENVIRONMENT` | `Production` | `run-local-stack.sh`. `Development` makes authservice write the invitation token to its log (AUTH.md, Members and invitations). |
| `TEST_POSTGRES` | unset | `dotnet test`. Set it to run the PostgreSQL-backed tests; unset, they are skipped. |

## 3. Secrets and how to generate them

| Secret | Where it lives | How to make it |
|---|---|---|
| authservice RS256 private key | `.local-stack/secrets.env` (`JWT_PRIVATE_KEY_PEM_B64`); AppHost parameter `authservice-jwt-private-key-pem` | `scripts/gen-dev-secrets.sh` (OpenSSL, RSA 2048 PKCS#8). Never printed. |
| Environment encryption key | `.local-stack/secrets.env` (`SECRETS_ENCRYPTION_KEY`); AppHost parameter `environments-encryption-key` | `scripts/gen-dev-secrets.sh` (32 random bytes, base64). |
| Development database password | `PG_CONNECTION` default | Not secret outside a developer's machine. |

Rules that apply to every secret (P5, [ADR-0006](decisions/0006-secret-variables.md)):

- Secrets are never in a tracked file. `.gitignore` covers `.local-stack/`, `.env`, `secrets.env`.
- The pre-commit hook (`scripts/hooks/pre-commit`, installed by `scripts/install-hooks.sh`) scans staged changes with gitleaks
  and **refuses the commit** if the scanner is missing or fails. `git commit --no-verify` bypasses it; do not use it.
- Losing the JWT key signs every user out. Leaking it lets the holder mint tokens for anyone
  ([AUTH.md](AUTH.md#limits-adr-0004)).
- Losing the encryption key makes every stored secret unreadable (`SecretUndecryptable` warnings in the log).

Production secret management is an owner decision and is not described here.

## 4. Running locally

### 4.1 Prerequisites

- .NET SDK 10 (`global.json` pins 10.0.100 with `rollForward: latestFeature`).
- PostgreSQL 16, reachable at `PG_CONNECTION`.
- `git`, `openssl`, `psql`, `curl`, `base64`, and for the hook `gitleaks`.

### 4.2 The no-Docker stack (`scripts/run-local-stack.sh`)

```bash
scripts/install-hooks.sh                  # once per clone
scripts/run-local-stack.sh up             # generates the dev secrets if missing, clones authservice v0.3.4,
                                          # builds it, starts authservice (5300), CollectionsService (5301)
                                          # and ExecutionService (5303); waits for each /health
scripts/run-local-stack.sh status
scripts/smoke-auth.sh                     # the authservice contract the services rely on
scripts/run-local-stack.sh down           # stops the processes; databases and .local-stack/ are kept
```

Logs: `.local-stack/authservice.log`, `collections.log`, `execution.log`, and the build logs next to them.

**What the stack does not run:** EnvironmentsService (port 5302 in the plan) and the Web. The consequences
were checked against the live stack on 2026-10-31:

- A request to `POST /api/v1/execute` with an `environmentId` answers `502 upstream_unavailable`, because nothing
  is listening where ExecutionService looks for EnvironmentsService.
- The Web is started separately (§4.3).

### 4.3 The Web (`dotnet run`)

The Web reaches its backends by service-discovery names (`http://authservice`, `http://collections-service`) and
reads its token metadata from `Auth:MetadataAddress`. Outside the AppHost, nothing resolves those names, so start
the Web with the addresses of the local stack:

```bash
env services__authservice__http__0=http://localhost:5300 \
    services__collections-service__http__0=http://localhost:5301 \
    Auth__MetadataAddress=http://localhost:5300/.well-known/openid-configuration \
    dotnet run --project src/PostmanClone.Web
```

Without these settings the Web answers the sign-in form with `error=unavailable`: the sign-in POST reaches
authservice, but the token check fails because the metadata address does not resolve. The Web then listens on
`http://localhost:5310` (`launchSettings.json`).

Checked on 2026-10-31: with these settings, a sign-in POST sent with curl (no `Origin` header) and the right password
redirects to `/` and sets `pc.session`. **A POST sent by Chromium is refused with 403**: the browser sends
`Origin: null` under `Referrer-Policy: no-referrer`, and the origin guard refuses it. Browser sign-in does not work on `main`
(OI-01).

### 4.4 The AppHost (`src/PostmanClone.AppHost`, needs Docker)

```bash
dotnet run --project src/PostmanClone.AppHost
```

It starts PostgreSQL with four databases, the authservice container (`ghcr.io/konradcinkusz/authservice:v0.3.4`), the
three services and the Web on port 5310. It needs Docker and the two AppHost parameters (§3). **It has never been run
in a development session**, because sessions have no Docker daemon (see [TESTING.md](TESTING.md#4-what-has-never-run)).

## 5. Health endpoints

| Service | `GET /alive` | `GET /health` | Notes |
|---|---|---|---|
| authservice | `/alive` | `/health` | Readiness is `GET /health/ready`: 200 once the schema is migrated. |
| CollectionsService | 200 | 200 when the database and migrations are ready | Checks `database` and `migrations` (ServiceDefaults). |
| EnvironmentsService | 200 | 200, includes `secrets` | `secrets: unavailable` does not fail readiness: non-secret features work (P8). |
| ExecutionService | 200 | 200, observed `{"status":"healthy","ssrfProtection":"enforced"}` | `ssrfProtection` is the optional-integration status. |
| Web | 200 | 200, observed `{"status":"healthy"}` | The Web's health does not check its backends; the status strip on each page does. |

Startup prints the same optional-integration list (`StartupBanner`).

## 6. What degrades, and how it shows

| Missing | Behaviour | Where it shows |
|---|---|---|
| Encryption key | Secret writes `503 secrets_unavailable`; plain variables still work | EnvironmentsService `/health`, startup banner |
| EnvironmentsService | Requests with `environmentId` answer `502 upstream_unavailable`; requests without one still send | ExecutionService responses |
| Relaxed SSRF setting in Production | Startup fails, naming the setting | ExecutionService log |
| OTLP endpoint | No export; logging still runs | nowhere |
| authservice | Sign-in shows "could not be reached" (`error=unavailable`); signed-in sessions continue until refresh fails | Web sign-in page, status strip |
| Database | Health fails; the service does not serve | `/health` |

## 7. Ports and processes

| Process | Local stack | AppHost |
|---|---|---|
| authservice | 5300 | container, port 8080 inside |
| CollectionsService | 5301 | floats |
| EnvironmentsService | not run by the script (5302 in the plan) | floats |
| ExecutionService | 5303 | floats |
| Web | 5310 (`dotnet run`) | 5310 (fixed) |
| PostgreSQL | 5432 | container |

## 8. What an owner would have to add before any deployment

None of this exists in the repository, by decision (ADR-0007):

- A hosting target and a deploy workflow. Tags are not used.
- Production values for `Auth__*`, the authservice key, the encryption key, and the Web's `AllowedOrigins` and forwarded-header settings.
- TLS at the edge, and the forwarded-header configuration that goes with it.
- A network egress policy for ExecutionService. The SSRF guard in this repository is not the only control
  (ADR-0011 names the network policy as the backstop).
- A decision on scale. The Web keeps sessions in memory and runs as one instance (ADR-0008).
- A privacy notice (the page `/legal/privacy` is a placeholder; see [PRIVACY.md](PRIVACY.md)).
- Fixes for the blocking items in [OPEN-ITEMS.md](architecture/OPEN-ITEMS.md).
