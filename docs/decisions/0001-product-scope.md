# ADR-0001: Product scope and non-goals

- **Status:** Accepted
- **Date:** 2026-10-26
- **Work package:** WP00

## Context

postman-clone is an API client in the spirit of Postman. Postman is large: collection runner,
test scripts, mock servers, monitors, GraphQL, gRPC, WebSocket and SSE clients, OAuth flows,
team billing. Building all of it would not finish, and each feature added is a new attack
surface. Some features are also dangerous to build in this form: a script runner executes
untrusted code, and a monitor makes outbound calls on a schedule with no user present.

The implementation plan has to say, in one place, what is in and what is not, so that a
sub-session does not "improve" the product by adding a feature nobody asked for.

## Options considered

1. **Full Postman parity**, delivered in phases. Rejected: the phases would never end, and the
   script runner alone would dominate the security review.
2. **A minimal request sender** with no workspaces, no collections and no import. Rejected: it
   would not exercise the multi-service design the plan exists to show, and it would be a toy.
3. **A bounded product** with collections, environments, a sender, history and import/export
   inside one workspace, and a written list of non-goals. Chosen.

## Decision

The in-scope set is the table in [`docs/implementation-plan/README.md`](../implementation-plan/README.md#1-product-scope):
sign-in, workspaces, collections, the request builder, environments with secret variables, the
server-side sender, the response viewer, history, and import/export of Postman v2.1 and cURL.

The following are out of scope and are not built or stubbed:

- mock servers, monitors and scheduled runs;
- the collection runner and test scripts (`pre-request` and `tests` are ignored and reported on
  import, never executed);
- GraphQL, gRPC, WebSocket and SSE clients;
- file upload in form-data, a cookie jar, proxy settings, client certificates;
- OAuth 2.0 flows beyond the four auth helpers (none, bearer, basic, API key);
- public sharing links, cloud sync beyond the workspace, live co-editing;
- per-collection and global variables (environment variables only);
- per-resource ACLs or viewer roles (authservice has Owner, Admin and Member only);
- two-factor sign-in (the Web shows "not supported yet");
- keyboard shortcuts that need JavaScript interop;
- deployment ([ADR-0007](0007-deployment-out-of-scope.md)) and billing.

## Consequences

- Every work package has a fixed boundary. A sub-session that finds itself building a
  non-goal stops and reports it.
- Import of a Postman collection is tolerant, not complete: unsupported parts become warnings in
  the import result, so a user learns what did not come across.
- The security review is bounded: no script execution, no outbound calls except the sender, and
  no background work.
- Users who expect Postman parity will find gaps. The README states the limits, so the gap is
  visible before it is a surprise.

## Revisit when

A non-goal is requested by a real user with a concrete use case, or a dependency that the
non-goal relied on (for example, the lack of a script engine) changes. Any change is a new ADR
that supersedes this one; the list is not edited in place.
