# ADR-0029: Fly topology and manual deployment

- **Status:** Accepted. Implements the Fly half of [ADR-0026](0026-local-agent-and-fly-estate.md) (Decisions 2 and 10).
- **Date:** 2026-11-01
- **Work package:** WP33

## Context

ADR-0026 decided that the shared estate (Web, CollectionsService, EnvironmentsService, ExecutionService, authservice and
PostgreSQL) runs on Fly.io, and that it is deployed by hand only. The owner has no Fly token yet. This ADR records how the estate
is laid out on Fly, how a deployment is started, and what each choice costs. The step-by-step procedure is in
[docs/FLY_DEPLOYMENT.md](../FLY_DEPLOYMENT.md). The secrets are in [flyio/SECRETS.md](../../flyio/SECRETS.md).

Two facts about the code shape the topology:

- The Web reaches its four backends by logical names (`http://authservice`, `http://collections-service`, and so on). They are
  resolved by service discovery from `services__<name>__http__0` settings (`src/PostmanClone.Web/Api/BackendServices.cs`). On Fly
  those settings hold the private `.flycast` addresses, so no code changes.
- Every service validates tokens against authservice's discovery document and JWKS (ADR-0004). The validators must reach
  authservice from wherever they run.

## Options considered

1. **One app per service, private `.flycast` networking, only the Web public.** Chosen. Each service has its own machine, its own
   image and its own secrets, and the database and the services are not reachable from the internet.
2. **One app with every process in it.** Rejected. One failure, one scale and one set of secrets for everything, and the database
   would share a machine with the services.
3. **Every service public.** Rejected. A public EnvironmentsService would expose the secret store to the internet to answer
   requests that only the estate makes. The owner's topology is that only the Web is public.
4. **The JWKS and the discovery document over public HTTPS.** Considered for authservice. It needs a public authservice, which
   option 3 rejects. The internal route is kept and recorded as a deviation (Decision 4).
5. **Fly's managed PostgreSQL (`postgres-flex`).** Rejected. It is bootstrapped with `fly postgres create`, not a plain deploy, and
   the estate needs one instance with four databases and per-service roles (FLY-IO-DEPLOYMENT §14). The vanilla image
   `postgres:16-alpine` with a `PGDATA` subdirectory is what a plain deploy initialises.
6. **A tag-driven deployment (the reference pipeline).** Rejected by ADR-0026 Decision 10. A tag is one accidental `git tag` away from
   a deployment, and the owner wants every deployment to be an act.
7. **A manual workflow with a typed confirmation, deploy-only.** Chosen. It has `workflow_dispatch` as its only trigger, an input that
   selects the app, a confirmation that must equal the app name (or `deploy-all`), and a preflight that stops when the token or an
   app's secrets are missing. It does not create apps, volumes or secrets: those are bootstrapped by hand, once, and documented.

## Decision

1. **Six apps, named `postmanclone-<name>`**, with the logical names of the code unchanged: `web` (public), `authservice`,
   `environments-service`, `collections-service`, `execution-service` and `postgres` (private). Each has its own `flyio/*.fly.toml`.
2. **Private addresses for service-to-service calls.** Each service is called at `http://postmanclone-<name>.flycast`. The Fly
   proxy routes the call and starts a stopped machine. `force_https` is off on the private apps, so the proxy does not redirect the
   HTTP call to an HTTPS listener that is not served.
3. **Minimum machines.** One machine for the Web, authservice, EnvironmentsService and CollectionsService, because each sits on a
   synchronous path (a sign-in, a token check, a resolve, a collection call). ExecutionService scales to zero: the server sender is
   the second choice, and the first send after a quiet period waits for a cold start (the Web waits 120 s, `BackendServices.cs`).
   FLY_DEPLOYMENT §8 has the cost of each.
4. **Plain HTTP between the apps, with `Auth__RequireHttpsMetadata=false`.** The discovery document and the JWKS are read over 6PN
   (WireGuard-encrypted). This is a deviation from OPERATIONS §2 and FLY-IO-DEPLOYMENT §6; it is recorded in
   [DEVIATIONS.md](../architecture/DEVIATIONS.md) (2026-11-01). The alternative is a public authservice, which the topology rejects.
5. **PostgreSQL is a vanilla image, bootstrapped by hand.** It has no public listener. The init script creates one role and one
   database per service, owned by that role, and revokes `CONNECT` from `PUBLIC`. The services connect with `SSL Mode=Disable` over
   6PN, because the image has no certificate. The workflow checks that the app, its volume and its secrets exist; it never redeploys
   the database.
6. **The manual workflow** (`.github/workflows/flyio.yml`) has these properties, each checked by a test in CI:
   - the only trigger is `workflow_dispatch` (`scripts/check-flyio-trigger.sh`, which also proves it rejects a push trigger, a tag
     filter, a schedule, a `workflow_call` and an inline `on:` list);
   - the preflight stops the whole run, with a message, when `FLY_API_TOKEN` is missing, and before any deploy when the
     confirmation does not match;
   - each deploy names the secrets it needs and stops before deploying when one is missing (names only, never values);
   - the jobs run in dependency order, and a job runs only when its upstream succeeded or was skipped;
   - `concurrency` lets one run at a time deploy.
7. **The configuration is checked without a Fly account.** `scripts/check-flyio-config.py` parses every TOML file, requires a build
   source and a health check path for each app, a subdirectory `PGDATA`, the minimum machines, no secret-looking value in `[env]`,
   and cross-checks the `.flycast` names, the discovery names, the workflow and `SECRETS.md`. Its self-test breaks copies and requires
   each break to fail.
8. **`flyctl` is not run in CI.** A CLI validation needs a token and a network path to Fly. The offline check above is what runs.

## Consequences

- The estate has a fixed monthly cost. FLY_DEPLOYMENT §8 gives the per-app reasoning: four always-on services and the Web, the
  database, and volumes. The figures there are list-price estimates to verify on the pricing page before the owner commits.
- A deploy of the Web signs every user out: the sessions are in one machine's memory (ADR-0008), and the strategy is `immediate`.
- The sign-in windows are shared per address (OI-26). Behind the Fly edge, authservice sees the Web's private address, so its
  window is one for all users. The Web's own windows are per address too, and `Proxy:TrustForwardedFor` stays `false` until the owner
  has decided OI-26 and confirmed how the Fly proxy writes `X-Forwarded-For`.
- The workflow's action pin (`superfly/flyctl-actions/setup-flyctl`) is a version tag, not a commit SHA. The owner pins a SHA before
  the first run.
- Deployments need the owner's Fly organisation, the GitHub environment `fly`, `FLY_API_TOKEN`, a region choice, the pull access to
  the authservice image if it is private, and the production values. FLY_DEPLOYMENT §1 lists them.

## Not verified here

- Nothing was deployed, and no Fly app was created. The workflow has never run. There is no Fly token and no `flyctl` binary (the
  download is refused by the proxy in this session).
- Whether Fly's proxy redirects an HTTP call on a `.flycast` address when `force_https` is on: the configuration turns it off on the
  private apps, and the first deploy checks that the calls return 200, not 3xx.
- The behaviour of authservice with no `Jwt__PrivateKeyPem` (its own, not verified).
- The image digest of authservice v0.3.4 (the tag is used).

## Revisit when

- The owner decides OI-26 (client-address trust) or chooses a per-host confirmation for private targets.
- authservice is served over HTTPS, internally or publicly: then `Auth__RequireHttpsMetadata` returns to `true`.
- Fly's per-app cost of the always-on machines is too high: the first candidate is one shared machine for the three private
  services, which changes the isolation of their processes, not of their data.
- The owner wants a release channel for the agent (ADR-0026, Consequences). A release is not a Fly deployment, and it needs its own
  decision, not a tag in this workflow.
