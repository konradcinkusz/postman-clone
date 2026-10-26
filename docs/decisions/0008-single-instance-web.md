# ADR-0008: The Web keeps sessions and open tabs in server memory and runs as one instance

- **Status:** Accepted
- **Date:** 2026-10-26
- **Work package:** WP00

## Context

[ADR-0005](0005-bff-session.md) keeps each user's tokens on the server. It leaves open where that
server-side state lives. A Blazor interactive server application also holds a circuit per open tab,
with component state that must survive between round trips. Putting the session and that state into
a shared store (Redis, a database) is a larger design: it needs serialization of component state and
a cache-invalidation story. Neither is needed for an internal tool with a handful of users.

## Options considered

1. **A distributed session store** (Redis or a database) so that several Web instances can share
   sessions. Rejected for this build: it adds an infrastructure dependency, a serialization contract
   for the session and a second place a token can leak from, all for scale the product does not have.
2. **Sticky sessions behind a load balancer.** Rejected: circuits are already sticky, so this only
   moves the failure mode; it still needs a balancer that does not exist.
3. **One instance, sessions and open tabs in its memory.** Chosen. A restart ends sessions and users
   sign in again.

## Decision

- The Web holds sessions and the open-tab state in its own memory.
- The Web runs as a single instance. The Aspire AppHost and the local stack start exactly one.
- A restart ends every session; the user signs in again through authservice. No state is lost in the
  services, which hold all the data.
- The store is behind an interface, so a distributed implementation can replace it without touching the
  pages.

## Consequences

- No second infrastructure dependency and no session serialization to get wrong.
- A deploy, a crash or a restart signs every user out. Users notice and sign in again.
- Horizontal scaling is not possible until a distributed store exists. The limit is stated in the README.
- Memory use grows with the number of open tabs. The circuit lifetime is bounded by the five-minute
  revalidation in [ADR-0005](0005-bff-session.md), and the idle timeout for the store is decided with the circuit lifetime (WP12).

## Revisit when

The product needs more than one Web instance, or sign-out-on-restart becomes a user complaint. Either
reopens the store decision as a new ADR.
