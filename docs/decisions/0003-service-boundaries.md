# ADR-0003: Three domain services and a Web, one database per service

- **Status:** Accepted
- **Date:** 2026-10-26
- **Work package:** WP00

## Context

The product has four kinds of data with different owners and different risks: collections (a
large document model, read often), environments (variables, some of them secret), sender
execution (outbound network access and untrusted response bodies), and history (per user). The
reference architecture (P3, P4) asks for a bounded context per service and a database per owner,
and it asks that each separation earn its place.

## Options considered

1. **A monolith** with one database and modules inside it. Simple to build and deploy. Rejected:
   the sender's outbound network capability and its untrusted response bodies would sit in the same
   process as the secret store, so a flaw in one is a flaw in both.
2. **Two services**: a persistence service and the Web. Rejected: environments and execution would
   still share a failure and egress domain.
3. **Three domain services and the Web**: CollectionsService (`collectionsdb`),
   EnvironmentsService (`environmentsdb`) and ExecutionService (`executiondb`), with the Web as a
   backend-for-frontend holding no data. Chosen.

## Decision

| Service | Owns | Why it is separate |
|---|---|---|
| CollectionsService | collections, folders, saved requests, import and export | The core document model, high read volume, no secret material (auth helper fields hold `{{variable}}` references only) |
| EnvironmentsService | environments, variables, encrypted secrets, the encryption key | Secret lifecycle and key custody get their own blast radius and audit log. Variables are always read per environment, so they stay together (P3 data cohesion) |
| ExecutionService | the sender and request history | The only outbound network capability and the only holder of untrusted response bodies. A separate service is a separate failure and egress domain. It calls EnvironmentsService over HTTP for variables and never reads its database |
| PostmanClone.Web | nothing persistent | Backend-for-frontend and UI. Holds sessions in memory ([ADR-0008](0008-single-instance-web.md)) |

Each service has its own PostgreSQL logical database. Cross-service calls are synchronous HTTP
against [`CONTRACTS.md`](../implementation-plan/CONTRACTS.md). There is no event bus (reference
architecture §4). Web DTOs are copies of the services' types, guarded by round-trip tests.

## Consequences

- Five logical databases on one PostgreSQL server locally: one per owner, so a migration in one
  service cannot break another's schema.
- An extra network hop for every request that needs a variable: the sender calls EnvironmentsService
  before it sends. Latency is small on a local stack and is measured later, not assumed.
- Copies of DTOs in the Web can drift. The round-trip tests make drift a failing test instead of
  a runtime surprise.
- Each service needs its own migrations, health endpoint and test project. The kernel
  (`PostmanClone.ServiceDefaults`) keeps the shared plumbing so the cost stays bounded.

## Revisit when

A second place needs to call the sender, or the sender's outbound capability is needed by a
non-execution feature. Either would strain the split and would be a new ADR, not an edit here.
