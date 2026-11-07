# ADR-0031: The container estate, and what was measured about Aspire on a GitHub runner

- Status: Accepted
- Date: 2026-11-07

## Context

The AppHost (ADR-0004: authservice as the pinned image `ghcr.io/konradcinkusz/authservice:v0.3.4`) had never been run, and every CI and
local path built authservice from source and ran it as a process (`scripts/run-local-stack.sh`). The owner asked that the authservice
container really work, that a CI job exercise containers, and that it be measured whether Aspire's orchestrator (DCP) works on GitHub
runners (earlier findings in sister repositories said it does not).

## Decision

1. **`compose.yaml` and `scripts/compose-up.sh`** run the whole estate as containers: PostgreSQL 16 (four databases), the pinned authservice
   image, CollectionsService, EnvironmentsService, ExecutionService and the Web built from their Dockerfiles, and the one-shot seed
   (ADR-0030). `AUTHSERVICE_IMAGE` replaces the pinned image where a host cannot pull from `ghcr.io`; the default is the pinned one.
2. **What the container needs, found by running it.** The signing key is a PKCS#8 RSA PEM in `Jwt__PrivateKeyPem` (its presence selects RS256),
   generated into the gitignored `.local-stack/` by `scripts/gen-dev-secrets.sh` (`openssl genpkey`, never PKCS#1). The data-protection keys
   sit on a volume. `Jwt__PublicBaseUrl` is `http://authservice:8080`, the address the other containers fetch the keys from. authservice
   runs as `Development` while the seed is on, with its console copied to a folder (`exec dotnet AuthService.dll > >(tee -a /logs/authservice.log)`)
   because v0.3.4 returns an invitation token only in an email. Health is probed by a hand-written request in bash, because the
   ASP.NET runtime image has no curl.
3. **The AppHost mirrors compose.** It generates the signing key and the encryption key on the first run into this project's user secrets
   (`DevSecrets`), copies authservice's console to `.local-stack/aspire-logs`, keeps the data-protection keys on a volume, adds the seed
   after the services, and runs **every consumer of authservice's tokens as a container** (the Web and ExecutionService were host processes).
4. **A test compares the two** (`tests/PostmanClone.AppHost.Tests`). The AppHost model is built in memory with `Aspire.Hosting.Testing` and
   never started, so it needs no container runtime and runs with the unit tests. It pins: the same image reference and tag; the same
   environment variable names and fixed values on authservice, the Web and the three services; the same key handling (a secret
   parameter in the AppHost, a required `${...:?}` reference in compose, never a literal, PKCS#8); the same mounts and entrypoint; the
   same seed variables and the seed's waits; the switch that removes the seed. Each was shown to fail on a changed tag and a changed audience.
5. **CI.** `compose-e2e` (part of the `pr-validation` aggregate) pulls the pinned image, runs `scripts/compose-up.sh`, checks authservice's
   health and JWKS, runs the seed a second time (it must create nothing), and runs the `Estate=compose` journeys in Chromium: the seeded
   sign-ins, the demo environment, the demo GET through the server sender, and the invitation journey. The journeys that need a loopback
   target or the local agent stay on the local-stack `e2e` job, because a container cannot offer its host's loopback.
6. **`aspire-apphost-probe`** is an informational job (not in the aggregate): it runs the real AppHost with `dotnet run` and signs in as the
   seeded owner through the Web.

## What was measured

- **Aspire DCP works on a GitHub-hosted `ubuntu-latest` runner.** The probe (`scripts/aspire-probe.sh`, job `aspire-apphost-probe`) started
  DCP (`Starting DCP with arguments: start-apiserver`), which pulled `ghcr.io/konradcinkusz/authservice:v0.3.4` and `postgres:18.3`, built the
  data services from their Dockerfiles, started them, and ran the seed project. On the DCP-started authservice container, a login as
  `demo.owner@postmanclone.test` answered 200 and the console held the invitation lines; after the fix in point 3, the Web sign-in through the AppHost
  succeeded (`PROBE-RESULT: works` in the job log, runs for heads `869162d`, `98ab035` and `497bb0d` of PR #38). It contradicts the earlier finding
  from the sister repositories; this repository's runs are the evidence for this repository.
- **The first runs failed, and the failure was the AppHost's, not DCP's.** The Web and ExecutionService ran as host processes, which cannot reach the
  address authservice advertises inside the container network, so the Web's sign-in was refused although authservice itself answered. The Web also had
  no `Auth__MetadataAddress` and no discovery names for three backends. Both are fixed and pinned by the parity test.
- **The authservice container runs.** On the runner (compose-e2e): the pinned image pulled in three seconds, `/health/ready` answered 200 with
  the database migrated, `/.well-known/jwks.json` held one RS256 key, the seed created the cast through it, and the journeys signed in against
  it. In an agent session, the same was shown with an image built from the same tag (below).
- **Authservice's sign-in window caused a family of flaky timeouts once the seed existed.** It allows 20 register, login and refresh calls per
  minute per address, in a fixed window with a queue of five; a request past it waits (a refusal measured right after the seed took 3 to 70 ms, so
  authservice was not slow). The seed uses about ten calls, and several journeys make more than one call per counted action, so the suite starts with
  the window treated as spent (`E2E_AUTH_WINDOW_PRESPENT=18`, one minute once).

## What was not shown, and why

- In an agent session the pull of the pinned image fails at the blob host: `failed to copy: httpReadSeeker: failed open: failed to do request: Get
  "https://pkg-containers.githubusercontent.com/ghcrblobs06/blobs/sha256:5169b7b7...": Forbidden`. The manifest comes from `ghcr.io`; the layers are refused
  by the sandbox proxy. An image built from the tag's source (`dotnet publish` on the host, the final stage of authservice's Dockerfile) stood in
  for the compose run in the session. Inside `docker build` the same proxy breaks `dotnet restore` (an untrusted root), so the service images were
  built the same way. CI builds them from their Dockerfiles.
- The AppHost was not started in a session (the above).
- The probe is a measurement, not a gate. Making it required is an owner decision.

## Consequences

- Two descriptions of the container estate exist; the parity test is what keeps them one.
- The AppHost no longer runs the Web or ExecutionService as processes. A developer who wants the Web under a debugger uses `scripts/run-local-stack.sh`.
