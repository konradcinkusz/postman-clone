# ADR-0030: Demo data is a seeder that calls the running APIs

- Status: Accepted
- Date: 2026-11-02

## Context

A fresh start of the product showed nothing: users registered by hand, made a workspace, and created a collection before there was
anything to look at. The owner asked that every start path (the Aspire AppHost, a compose estate, the no-Docker script) end with a
populated product, and that the demo data be safe: reproducible, removable without touching real data, and never active outside
development. [architecture-standards `DEMO-DATA-AND-SEEDING`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/DEMO-DATA-AND-SEEDING.md)
sets the pattern: a reserved namespace, reset by namespace, seed through the running system, deterministic and idempotent.

## Decision

1. **A separate project, `src/PostmanClone.Seeder`.** A console program with no package references. It is not in the kernel
   (`PostmanClone.ServiceDefaults` stays under its 800-line ceiling and holds no demo names), and it references no service code.
2. **Through the APIs, not the database.** It calls authservice (register, login, organization, invite, accept), CollectionsService and
   EnvironmentsService with the bearer tokens those services issue. A seed that could create a state the services refuse would be a lie.
   Everything it creates passes the validation a user's request passes, including the secret-variable encryption.
3. **The dataset** (`DemoData.cs`, nothing random). A cast of four, all able to sign in at once (authservice without an email provider
   needs no confirmation): `demo.owner`, `demo.admin` (invited as `Admin`) and `demo.member` (invited as `Member`) in one organization
   `Demo Workspace`, so a second browser sees what the first shares, and `demo.other` as owner of a second organization `Other Workspace`,
   so another workspace's collection answers "not found". Data: a collection `[demo] Getting started` with a folder `Echo`, a
   `GET https://example.com/`, a `POST {{base_url}}/post` with a JSON body and a `GET http://localhost:5303/health` (the server's sender
   refuses loopback, so it is the request for the local agent); an environment `[demo] Local demo` with the plain variable `base_url` and the
   secret `api_key` whose value is the obviously fake `demo-not-a-real-key`; and `[demo] Other workspace only` in the other workspace. The
   run fails unless the admin and the member see the demo collection and the other account does not.
4. **Reserved namespace.** Every collection and environment the seed creates starts with `[demo] `. `reset` signs in as the demo owner,
   and in the demo workspace only, deletes collections and environments whose name starts with that prefix, and logs the counts per
   store. It keeps the accounts and the workspace. Data in the same workspace under any other name is not touched (a test pins it).
5. **Idempotent.** Each step looks for what it would create (by exact name or address) and does nothing when it is there. A second run
   makes no state-changing call: the unit test asserts an empty write list. A partly seeded estate (an interrupted run, a
   reset that stopped half way) is completed. A pending invitation is accepted from the logged token, or re-sent if the token is gone;
   it is never invited twice.
6. **Gated three ways, and refusing otherwise.** The seeder exits with code 2 and a reason unless `Seed__Enabled` is exactly `true`,
   the environment (`DOTNET_ENVIRONMENT` or `ASPNETCORE_ENVIRONMENT`) is `Development`, and all three base URLs are local (loopback, a
   private address, `host.docker.internal`, or a single-label container name). The AppHost adds the seed resource only in Development and
   while `Seed:Enabled` is not `false` (default on). compose passes `SEED_ENABLED` (default true). `scripts/run-local-stack.sh` seeds
   unless `SEED_DEMO=0`.
7. **The invitation token comes from authservice's log.** authservice v0.3.4 returns an invitation token only in an email. With no email
   provider it writes the token to its log, and only in Development ([AUTH.md](../AUTH.md#members-and-invitations-wp24)). The seeder reads
   that log (`Seed__AuthLogPath`). The AppHost and compose run authservice in Development with its console copied to a folder
   (`exec dotnet AuthService.dll > >(tee -a /logs/authservice.log)`), and the no-Docker script already writes `authservice.log`. The
   token is never printed. This is the one place the seed reads something other than an API answer; it is the same channel the browser
   journey for invitations uses, and it exists because no API offers the token.
8. **The logins cannot be missed.** The seeder prints a table of the cast and the password last in its log (the Aspire dashboard's `seed`
   resource, `docker compose logs seed`, `.local-stack/logs/seed.log`); the README lists it first; and the Web's sign-in page shows the
   same accounts in a dev-only block, only when the Web runs in `Development` and `Seed:Enabled` is `true` (set by the AppHost, compose and
   the script only while they seed). `DevSignInHintTests` proves the block is absent in Production and Staging, and without the switch, and
   that it lists exactly the seeder's cast. The agent is never paired by the seed: pairing stays a user action on `/agent`
   (the README's five steps, and `QuickStartJourneys` runs them).
9. **The demo password is published** (`Demo-Passw0rd!`, meets authservice's rule) in the README and AUTH.md, with `gitleaks:allow` where it
   is written in code. Accounts with a published password exist only in a Development estate that the gate above enforces.

## Consequences

- Every start path ends the same way, and a developer sees collections, a shared workspace and an environment with a masked secret at once.
- authservice runs as Development in the estates that seed. That changes only what Development changes in v0.3.4: invitation tokens in
  the log, the Swagger UI, the developer exception page. The estate is a local one by construction (the gate).
- The seed depends on the log file being readable by the seeder. A setup that cannot share the log fails with a message that says what to set.
- The second-tier generator of the guide (a protocol-level generator that enters through the real front door) is the browser journeys:
  `SeededJourneys` sign in through the Web as the seeded accounts and send the demo request through the server sender.
- Not done: seeding history entries (they come from a real send), and removing the accounts on reset (authservice offers no delete that
  this seed should use).
