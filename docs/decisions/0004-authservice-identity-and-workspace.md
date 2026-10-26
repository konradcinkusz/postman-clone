# ADR-0004: authservice is the only identity provider; a workspace is one organization

- **Status:** Accepted
- **Date:** 2026-10-26
- **Work package:** WP00

## Context

The product needs accounts, sign-in, sharing between people, and roles. The estate already runs
[`authservice`](https://github.com/konradcinkusz/authservice), a separate identity service with
users, organizations, tokens and invitations. Building a second identity system here would
duplicate it and create a second place to get tokens wrong.

authservice issues access tokens with a `sub`, an `organization` claim per membership, and an
`organization:{id}:role` claim. It has no organization-scoped token and no workspace-switch
endpoint. That shapes how "workspace" can be defined.

## Options considered

1. **A local user table** in the Web or a service. Rejected: a second identity system, with its own
   password storage, reset and two-factor flows.
2. **Use authservice's organizations as the workspace, with a workspace switcher** that
   re-issues tokens per organization. Rejected: authservice does not support it, so a switcher
   would need a new token type in the external service.
3. **Use authservice as the only IdP. A workspace is the single `organization` claim in the
   token.** Chosen.

## Decision

- authservice is the only identity provider. Its image is pinned (v0.3.4) and never copied into
  this repository.
- Workspace is the single `organization` claim. The kernel's `WorkspaceClaims.Resolve` returns
  `Active(workspaceId, role)`, `None` or `Multiple`.
- With **zero** organizations, the user goes through onboarding: create one through authservice's
  `POST /api/v1/organizations` (the creator becomes Owner), then refresh to receive the claim.
- With **two or more**, every domain endpoint fails closed with `403 multiple_workspaces`, and the
  Web shows an explanation page at `/no-workspace`. There is no switcher.
- Workspace is never read from a route, query string or body.

The limits are recorded in `docs/AUTH.md`, written by WP03, and any difference from the pinned
authservice source is an amendment to [`CONTRACTS.md`](../implementation-plan/CONTRACTS.md), not a
silent change.

## Consequences

- A user who belongs to two organizations cannot use the product until one membership is removed.
  This is a known limit, stated in the README, and the fail-closed answer is intentional.
- Sharing is membership: everyone in the organization sees the same collections and environments.
  Per-resource roles do not exist ([ADR-0001](0001-product-scope.md)).
- Roles are only Owner, Admin and Member, which map to the table in the README §2.
- Token verification depends on authservice's JWKS endpoint being reachable from the services.
  Health reports it, and the services fail closed when it cannot be reached.

## Revisit when

authservice adds organization-scoped tokens or a workspace-switch endpoint, or the product needs
per-resource roles. Either is a new ADR and a contract amendment.
