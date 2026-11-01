# Deploying the estate to Fly.io

This is the procedure for the shared estate on Fly.io: the Web, CollectionsService, EnvironmentsService, ExecutionService,
authservice and PostgreSQL. It is written for the owner, who runs it by hand. The decisions are in
[ADR-0026](decisions/0026-local-agent-and-fly-estate.md) and [ADR-0029](decisions/0029-fly-topology-and-manual-deployment.md).
The secrets are listed in [flyio/SECRETS.md](../flyio/SECRETS.md). The configuration is in [flyio/](../flyio/).

**Nothing in this repository deploys by itself.** The only deploy path is `.github/workflows/flyio.yml`, which starts with
`workflow_dispatch` only: a person chooses it in the Actions tab. No commit, tag, pull request or schedule starts it, and
`scripts/check-flyio-trigger.sh` fails the checks when another trigger appears. Nothing here has been run against Fly.

**Contents**

1. Prerequisites (the owner)
2. The GitHub environment and `FLY_API_TOKEN` — the checklist for when the token is added
3. Creating the apps
4. PostgreSQL: the volume, the secrets, the bootstrap
5. Every secret and setting
6. The first deploy, in order
7. Checks after a deploy
8. Cost, and the minimum machines
9. Client-address trust and the sign-in windows (OI-26)
10. Rollback
11. Teardown
12. What is not done

---

## 1. Prerequisites (the owner)

- A Fly.io organisation, and the `flyctl` CLI on your machine (`flyctl version`). The workflow installs its own copy.
- A region. The configs use `fra`. Change `primary_region` in every `flyio/*.fly.toml`, and the volume's region below, if you choose another.
- A custom domain is optional and not configured. The Web is at `https://postmanclone-web.fly.dev`. A custom domain needs
  `AllowedOrigins__0` and `FrontendBaseUrl` changed to it, on the Web and authservice, before users are sent to it.
- Pull access to `ghcr.io/konradcinkusz/authservice:v0.3.4` if that package is private. The Fly builder needs to read it.
- The app names are `postmanclone-*`. Fly app names are global. If one is taken, rename it in its toml **and** in every
  `.flycast` reference and `services__…` setting that names it (`flyio/web.fly.toml` is the main one), and in the workflow.
  `scripts/check-flyio-config.py` will tell you where.

## 2. The GitHub environment and `FLY_API_TOKEN` — the checklist for when the token is added

The workflow reads the token from the GitHub **environment** named `fly`, not from a repository secret, so the environment can
carry protection rules. The owner does these steps **last**, when everything above is ready.

1. Create an organisation-scoped token: `flyctl tokens create org <org-name>`. Copy it once.
2. In GitHub: **Settings → Environments → New environment → `fly`**. Optionally add yourself as a required reviewer, so every run
   waits for an approval.
3. In the environment `fly`, add the secret **`FLY_API_TOKEN`** with the token.
4. Make sure the secrets of §5 are already set on Fly (the workflow checks them and stops when one is missing).
5. Run the workflow: **Actions → Fly.io deploy (manual) → Run workflow**. Choose `apps`, and type `deploy-all` in `confirm`
   (or the app name for one app).

If `FLY_API_TOKEN` is missing, the first job stops with this message and nothing is deployed:
*"FLY_API_TOKEN is missing. Add it to the GitHub environment 'fly' … Nothing was deployed."*

## 3. Creating the apps

The workflow deploys into apps that already exist. It does not create them, so that a mistyped name cannot create a new app
with default settings. Create the six apps once, from your machine:

```bash
flyctl apps create postmanclone-postgres      --org <org-name>
flyctl apps create postmanclone-authservice   --org <org-name>
flyctl apps create postmanclone-environments  --org <org-name>
flyctl apps create postmanclone-collections   --org <org-name>
flyctl apps create postmanclone-execution     --org <org-name>
flyctl apps create postmanclone-web           --org <org-name>
```

The four private apps are called by their `.flycast` names, so each needs a private IPv6 address. The Web needs a public address.
PostgreSQL is reached by `.internal` and needs no address.

```bash
for app in postmanclone-authservice postmanclone-environments postmanclone-collections postmanclone-execution; do
  flyctl ips allocate-v6 --private --app "$app"
done
flyctl ips allocate-v4 --shared --app postmanclone-web
flyctl ips allocate-v6 --app postmanclone-web
```

Without the private address the `.flycast` calls fail and the Web's sign-in does not work.

## 4. PostgreSQL: the volume, the secrets, the bootstrap

PostgreSQL is bootstrapped **once, by hand**. The workflow only checks that the app, the volume and the secrets exist, and it never
redeploys the database.

**4.1 The volume**, in the same region as the database (`initial_size` in the toml is 10 GB; it grows, and it cannot shrink):

```bash
flyctl volumes create postmanclone_pgdata --app postmanclone-postgres --region fra --size 10 --yes
```

**4.2 The secrets** the init script needs (flyio/SECRETS.md has the generation commands): the superuser password and four
role passwords. The script refuses to create anything when one is missing, so you find out at the first start.

```bash
flyctl secrets set --app postmanclone-postgres POSTGRES_PASSWORD=<…> PG_AUTH_PASSWORD=<…> \
  PG_COLLECTIONS_PASSWORD=<…> PG_ENVIRONMENTS_PASSWORD=<…> PG_EXECUTION_PASSWORD=<…>
```

**4.3 The deploy**, from the repository root:

```bash
flyctl deploy --remote-only --config flyio/postgres.fly.toml
flyctl logs --app postmanclone-postgres
```

The logs should show the four `created role … and database …` lines and then `database system is ready to accept connections`.

**If the init script failed**, the volume is partly initialised and the image will not run the script again. Destroy the volume and
start again: `flyctl machine list --app postmanclone-postgres`, stop and destroy the machine, then
`flyctl volumes destroy <volume-id> --app postmanclone-postgres --yes` and recreate it (4.1). Nothing else was stored yet.

## 5. Every secret and setting

Settings in `[env]` are in the toml files and are **public** (they appear in `flyctl config show`). Secrets are set with `flyctl secrets set`
and never appear in a file. The full list of secrets, with the commands and what happens when one is missing, is
[flyio/SECRETS.md](../flyio/SECRETS.md).

**Settings the owner must confirm before production use** (the first column is the file and key):

| Key | Where | Value now | What it does | Owner decision |
|---|---|---|---|---|
| `Auth__Issuer`, `Auth__Audience`, `Jwt__Issuer`, `Jwt__Audience` | every app's toml | `PostmanClone` | The token check. Authservice issues with these; every consumer must match them (AUTH.md). A different value rejects every token. | None (constant). |
| `Auth__MetadataAddress` | the services and the Web | `http://postmanclone-authservice.flycast/.well-known/openid-configuration` (the Web uses the logical name `authservice`, resolved by its `services__authservice__http__0`) | Where the keys are fetched. | None. |
| `Auth__RequireHttpsMetadata` | the services and the Web | `false` | Lets the discovery document and the JWKS be read over plain HTTP on 6PN. A deviation, recorded in DEVIATIONS.md and ADR-0029. | Owner: revisit when authservice serves HTTPS. |
| `Jwt__PublicBaseUrl` | authservice | `http://postmanclone-authservice.flycast` | The address the JWKS URL is built from. The consumers must be able to reach it. | None. |
| `FrontendBaseUrl` | authservice | `https://postmanclone-web.fly.dev` | The base of invitation links. | Change with a custom domain. |
| `services__authservice__http__0`, `services__collections-service__http__0`, `services__environments-service__http__0`, `services__execution-service__http__0` | the Web | `http://postmanclone-<…>.flycast` | Service discovery. The Web's code names its backends by these logical names (`Web/Api/BackendServices.cs`); without these keys the names do not resolve. | None. |
| `services__environments-service__http__0` | execution-service | `http://postmanclone-environments.flycast` | Where ExecutionService resolves environments. | None. |
| `AllowedOrigins__0` | the Web | `https://postmanclone-web.fly.dev` | **Required for forms to work.** Behind the Fly proxy the Web sees the request as `http://…`, so a browser's `https://…` Origin is compared with this list (`OriginGuardMiddleware`). Without it every sign-in and save is refused with 403. A custom domain is added here. | Change with a custom domain. |
| `ForwardedHeaders:KnownProxies` | the Web | **not set** | Lists the addresses whose forwarded headers the Web trusts. The Fly proxy has no fixed address published for this estate, and the Web trusts only loopback by default. Unset, the Web's remote address stays the Fly proxy's, so every user shares one address (see §9). Listing a proxy address here changes the address the forms window reads, together with `Proxy:TrustForwardedFor`. | Owner: set it only to addresses Fly documents for your region. |
| `Proxy__TrustForwardedFor` | the Web | `false` | The rate limiter reads the client address from `X-Forwarded-For` when `true`. `false` is the safe default and gives one window per edge address (OI-26). | **Owner, OI-26 — see §9.** |
| `RateLimiting__AuthPermitsPerMinute` | the Web | default `10` | The Web's sign-in and invitation forms share this window per client address. | Owner, with OI-26. |
| `Secrets__EncryptionKey` | environments-service (**secret**) | generated, §5 of SECRETS.md | Encrypts environment secret values (ADR-0006). | Owner keeps a copy outside Fly. |
| `Execution__Ssrf__AllowLoopback`, `Execution__Ssrf__AllowPrivateRanges` | execution-service | **not set** (defaults) | The server sender refuses local and private targets. Setting either is refused in Production. | **Do not set.** Local targets go through the agent (ADR-0026). |
| `ASPNETCORE_ENVIRONMENT` | every app | `Production` | Enables the production checks, the secure cookie and the refusals above. | Do not set it to Development. |
| `AUTHSERVICE_ENVIRONMENT` / `ASPNETCORE_ENVIRONMENT=Development` on authservice | authservice | **never set** | Development makes authservice write invitation tokens to its log (WP24). | **Never.** |
| `Agent:AllowedWebOrigins` | environments-service | to set when WP30 is merged | The origin the local agent accepts (ADR-0026 Decision 5). The Web's origin is `https://postmanclone-web.fly.dev`. | Set from WP30. |

**The sign-in and token checks depend on each other.** `Auth__Issuer`, `Auth__Audience` and the authservice `Jwt__*` values must
be identical on every app. A mismatch shows as every request answering 401 after a healthy deploy.

## 6. The first deploy, in order

Run this once, after §3 to §5 are done. The workflow runs the same order on every later deploy.

1. **PostgreSQL** — by hand (§4). Its check is `verify-postgres` in the workflow.
2. **authservice** — the pinned image, migrates its schema after the listener starts. Health: `/health/ready` (503 until it has migrated).
3. **EnvironmentsService** — migrates `environmentsdb`. Health: `/health`.
4. **CollectionsService** — migrates `collectionsdb`. Health: `/health`.
5. **ExecutionService** — migrates `executiondb`. Health: `/health`. It scales to zero, so its first deploy leaves it stopped until the first call.
6. **Web** — health `/health`. The public origin is checked after it deploys.

To run it: **Actions → Fly.io deploy (manual) → Run workflow**, `apps` = `all`, `confirm` = `deploy-all`. The workflow runs the jobs in
this order. It stops at the first failure, and the later jobs do not run. A failed job's log names the missing item.

To deploy one app later, choose it in `apps` and type its name in `confirm`. The jobs that depend on it are not run automatically.

## 7. Checks after a deploy

The health checks prove that each app answers. They do not prove that the estate is correct. Do these by hand after the first
deploy, and after any change to the tokens or the URLs:

1. **The Web answers at its public origin.** The workflow's last step does this. Open `https://postmanclone-web.fly.dev` in a browser.
2. **authservice publishes its keys.** The JWKS must return keys, or every service rejects every token while looking healthy:
   ```bash
   flyctl proxy 8081:8080 postmanclone-authservice     # leave it running in another terminal
   curl -fsS http://localhost:8081/.well-known/jwks.json | head -c 300
   ```
   The output must contain `"keys"` with at least one key. (The proxy reaches the machine over the private network.)
3. **Sign-in works end to end.** Register, create a workspace, and create a collection. Each step exercises a different service.
4. **A server send to a public URL works**, and a send to `http://localhost` is refused with `ssrf_blocked_address`. That refusal is correct.
5. **The logs carry no secret.** `flyctl logs --app postmanclone-authservice` and the others must not show tokens or secret values.
   Look for an invitation token in authservice's log; there must be none.

**Not verified until the first deploy:** whether the `.flycast` calls return 200 with `force_https` off (ADR-0029); the `flyctl`
command flags in this guide (check each with `flyctl help <command>` on the version you use); the JWKS check above on a real machine.

## 8. Cost, and the minimum machines

Fly bills each machine while it runs, and each volume by its size. A stopped machine costs nothing but its volume. The list
prices are on the Fly pricing page; the figures are not stated here, because they change, and a wrong number is worse than none.
Check the page before you commit.

| App | Machines when idle | Memory | Keeps a machine running because | Scales to zero? |
|---|---|---|---|---|
| `postmanclone-web` | 1 | 1 GB | A browser enters here, and a Blazor circuit is a live WebSocket. A cold start is a slow first page. The circuits are in memory, so the size is a guess until a load run. | No |
| `postmanclone-authservice` | 1 | 512 MB | Every service checks tokens against its keys, and every sign-in calls it. A stopped machine would turn every request into a failure. | No |
| `postmanclone-environments` | 1 | 512 MB | The local agent resolves secrets through it (ADR-0026 Decision 4), and ExecutionService calls it for every server send. | No |
| `postmanclone-collections` | 1 | 512 MB | Every collection call from the Web. | No |
| `postmanclone-execution` | 0 | 512 MB | Only a server send uses it, and the agent is the default sender. A cold start is paid by the first server send after a quiet period. | **Yes** |
| `postmanclone-postgres` | 1 | 1 GB | The database. Never scales. | No (one instance, one volume) |

**The cost, in one line:** six machines run around the clock, plus a 10 GB volume and the shared IPv4 address. The private IPv6 addresses
and the `.internal` names are free. The Web's public IPv4 is shared, which is free; a dedicated address costs extra.

**Cheaper options, and what each costs:**

- *Scale EnvironmentsService to zero.* The first agent send after a quiet period waits for a cold start, and the agent's call may time
  out. It saves one machine. Take it only if the first-send delay is acceptable to the team.
- *Scale CollectionsService to zero.* The first page after a quiet period waits for the cold start. The Web's timeouts allow it, but the user sees a slow load.
- *Scale the Web to zero.* The first visit waits for the Web to start. It is the cheapest saving with the most visible cost, and it breaks no request.

**Off the table, so nobody re-proposes them:** a public PostgreSQL listener; `force_https = false` on the Web; one database shared
between services; scaling the Web or PostgreSQL out, which the in-memory sessions and the single volume rule out (ADR-0008).

## 9. Client-address trust and the sign-in windows (OI-26)

Two rate limits protect the sign-in. Both see the wrong client address on Fly, and that is the open decision.

- **authservice's own sign-in window:** 20 requests a minute per client address (AUTH.md). Every sign-in from the Web comes from the
  Web's private address, so all users share one window. A team of ten signing in together will hit it.
- **The Web's forms window:** `RateLimiting:AuthPermitsPerMinute` (default 10). It reads the client address from the connection,
  unless `Proxy:TrustForwardedFor` is `true`, in which case it reads `X-Forwarded-For`. Behind the Fly proxy the connection is the proxy,
  so the default gives one window for all users.

The safe defaults are in the toml: `Proxy__TrustForwardedFor = "false"`. Setting it to `true` changes the rate limiter to trust
`X-Forwarded-For`. That header is safe to trust **only if the proxy overwrites a client's own value**. If a client can send its own
`X-Forwarded-For`, a client can pick any address and bypass the limit. The owner must check that before setting it: send a request
with a made-up `X-Forwarded-For` through the Web and confirm the window is not reset by the value.

authservice's setting for a forwarded client address is not documented in [docs/AUTH.md](AUTH.md). Its source is not attached to this
session, so the name is not stated here. Read it in the authservice repository (v0.3.4) before you choose.

**The decision, for the owner:** keep one shared window (safe against guessing, lock-out risk for a team), or trust the forwarded
address on both sides (per-user windows, and the header must be verified first). Record the choice in OPEN-ITEMS OI-26.

## 10. Rollback

Each deploy creates a release. The releases list is `flyctl releases --app <app>`. To go back, deploy the image of the release
you want: take its image reference from `flyctl releases --app <app> --image` (check the flag with `flyctl help releases`), then
`flyctl deploy --image <reference> --config flyio/<app>.fly.toml`.

Database migrations are not reversed by a rollback. An older image runs against the newer schema. Migrations in this estate add
tables and columns; they do not remove them. A rollback of a release that added a required column fails at its first request. In
that case restore from a snapshot (below) rather than rolling back the code.

**Volume snapshots.** Fly takes snapshots of volumes automatically; the retention is set by Fly, so check it on your plan. Check `flyctl volumes snapshots list postmanclone_pgdata --app postmanclone-postgres`.
A restore creates a new volume from a snapshot; it does not replace the existing one. No backup policy beyond Fly's snapshots is
configured (owner decision, §12).

## 11. Teardown

Teardown is not automated. It removes data, so it is a deliberate act by the owner, in this order (dependants first):

```bash
flyctl apps destroy postmanclone-web --yes
flyctl apps destroy postmanclone-execution --yes
flyctl apps destroy postmanclone-collections --yes
flyctl apps destroy postmanclone-environments --yes
flyctl apps destroy postmanclone-authservice --yes
flyctl volumes destroy <volume-id> --app postmanclone-postgres --yes    # the data
flyctl apps destroy postmanclone-postgres --yes
```

Destroying PostgreSQL's volume deletes every collection, environment, secret value and history entry. Export each collection (the
Postman v2.1 export in the product) first. No tool in this repository exports environments or history, so decide whether those are
needed before you destroy anything. `flyctl apps destroy` of an app with a volume also removes the volume.

## 12. What is not done

- **No custom domain.** The Web is at `postmanclone-web.fly.dev`. A domain needs `AllowedOrigins__0`, `FrontendBaseUrl` and the TLS edge.
- **No backup policy** beyond Fly's default volume snapshots. Restore is tested only when someone runs it.
- **No `Strict-Transport-Security` header** at the edge (OI-20). The Fly proxy terminates TLS; the header is an owner decision.
- **No email sender** configured for authservice (SR-11, OPEN-ITEMS §0): accounts may be active without verification.
- **No Data Protection key persistence** in the Web. A restart signs everyone out, which is the same as the in-memory sessions (ADR-0008).
- **No pinned digest** for the authservice image; the tag is used. Add the digest when the owner has it.
- **No pinned action SHA.** The workflow uses `superfly/flyctl-actions/setup-flyctl@v1.5`. The owner pins a commit SHA.
- **No `flyctl config validate` in CI.** It needs a token. The offline check (`scripts/check-flyio-config.py`) runs instead.
- **Nothing has been deployed.** This guide and the configs were written and checked offline.
