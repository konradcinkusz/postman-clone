# ADR-0005: Backend-for-frontend session: an opaque HttpOnly cookie, tokens kept on the server

- **Status:** Accepted
- **Date:** 2026-10-26
- **Work package:** WP00

## Context

The browser must never hold an access token or a refresh token. A token in JavaScript-reachable
storage, in a URL, or in persisted component state is one cross-site script away from theft. The
access token is a bearer credential for every domain service. The refresh token is long-lived and
rotates on use.

The Web therefore sits between the browser and the services as a backend-for-frontend (BFF). The
design question is what the browser holds and where the tokens live.

## Options considered

1. **Tokens in the browser** (local storage or a JavaScript-readable cookie), called from the page.
   Rejected: any injected script reads the token, and the browser boundary in `AGENTS.md` forbids it.
2. **A JWT in an HttpOnly cookie**. Rejected: the token itself is then in the browser, sent with
   every request, and a stolen cookie is a usable bearer token until it expires.
3. **An opaque session id in an HttpOnly, `SameSite=Strict` cookie. The ticket and the token pair
   live in a server-side store.** Chosen.

## Decision

- The browser holds only a random session id in an HttpOnly,
  `SameSite=Strict` cookie.
- The server-side store holds the authservice ticket and token pair for that session.
- Refresh is serialized per session. Refresh tokens rotate and are single-use, so two concurrent
  requests must not both spend the same one.
- Circuits are revalidated every five minutes: a session whose tokens can no longer be refreshed
  ends, and the user is sent to sign in again.
- Session and open-tab state live in the Web's memory ([ADR-0008](0008-single-instance-web.md)).
- Access tokens are passed to each service call per request, through `HttpRequestMessage.Options`,
  and never through a scoped service in a `DelegatingHandler`.

## Consequences

- A stolen session cookie is useless off the server: it is a random id, bound to the session that
  owns the token pair. Stealing it still allows a session hijack for its lifetime, which is why
  the cookie is `SameSite=Strict` and the state is HttpOnly.
- Restarting the Web ends every session. Users sign in again. This is accepted, and [ADR-0008](0008-single-instance-web.md)
  records it.
- Concurrent requests need a lock per session around refresh. That lock is a correctness concern
  and gets its own test.
- Responses that set session cookies carry `Cache-Control: private, no-store`, so a shared cache
  never stores them.

## Revisit when

The Web needs more than one instance (the session store would have to move out of memory, which
is a new ADR), or authservice changes its refresh-token rotation rules.
