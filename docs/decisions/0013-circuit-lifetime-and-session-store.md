# ADR-0013: Circuit lifetime and session store

- **Status:** Accepted
- **Date:** 2026-10-30
- **Work package:** WP12

## Context

[ADR-0005](0005-bff-session.md) keeps the token pair on the server behind an opaque cookie, and
[ADR-0008](0008-single-instance-web.md) keeps sessions in the Web's memory. Two questions were left open when
the sign-in flow was built: what the cookie is, and how long a session and its interactive circuit live. A third
question came with them: what happens when a token cannot be refreshed.

## Options considered

1. **The cookie middleware with a server-side `ITicketStore`.** Rejected. The cookie then carries a Data
   Protection envelope around a key, not a bare random id, and the framework decides its format.
2. **A custom authentication scheme whose cookie is the random id.** Chosen. The cookie holds 256 random bits
   and nothing else. The principal is built on the server from the verified claims.
3. **A JWT in the cookie.** Rejected by ADR-0005: the bearer would then be in the browser.

For the lifetimes, the options were a fixed session length, a sliding one, and a per-circuit lifetime. A fixed
length was rejected because an active user would be signed out in the middle of work. A purely sliding length was
rejected because an open tab left idle for days would never end.

## Decision

- **Cookie.** `pc.session` in Development over plain http, `__Host-pc.session` everywhere else. It is `HttpOnly`,
  `SameSite=Strict`, `Path=/`, and `Secure` except in Development over plain http, where browsers would refuse it.
  Its value is a 256-bit random id, base64url, 43 characters. A test asserts that no JWT shape appears in it.
- **Session lifetime.** A session ends after **eight hours without a request** (sliding) or at its **absolute
  lifetime of seven days**, which is authservice's default refresh-token lifetime. After seven days no refresh can
  succeed anyway.
- **What counts as activity.** An HTTP request that carries the cookie, and a backend call made from a circuit. A
  revalidation does not count, so an idle open tab still ends after eight hours.
- **Circuit revalidation.** Every five minutes the circuit asks its session whether it still stands. A dead session
  makes the circuit's principal invalid, so the user's pages stop and they are sent to sign in.
- **Access token refresh.** A token with less than two minutes left is refreshed before it is handed out. Refresh is
  serialized per session with a gate, because refresh tokens are single-use: concurrent callers wait, and the first
  one's result serves them all. The call to authservice is not cancelled midway, so a spent refresh token always
  leaves a stored new pair.
- **Failure.** A refresh that authservice refuses, or cannot answer, ends the session. The user signs in again.
- **Logout.** Revokes the user's refresh tokens at authservice (every session of that user, as docs/AUTH.md records),
  then forgets the session here. The cookie is cleared with the attributes it was set with.
- **Sign-in forms.** Sign-in, register, sign-out and create-workspace are minimal endpoints behind antiforgery, not
  component handlers: a response that sets a cookie must be an HTTP response, and a native form post needs no
  JavaScript (ADR-0009). A ReturnUrl is kept only as a local path (`ReturnUrlPolicy`).

## Consequences

- A browser that lost its cookie, or a Web restart, signs the user in again (ADR-0008).
- An open tab left idle for eight hours is signed out at its next check. This is intended.
- A short authservice outage during a refresh signs users out, because a session whose tokens cannot be refreshed
  cannot be shown to continue. The alternative keeps a session alive on a guess. Revisit if users report it.
- Signing out on one browser revokes the user's refresh tokens everywhere. Their other sessions fail at their next
  refresh, which is within an hour, and end then.
- Antiforgery tokens are bound to the signed-in user. A form on a signed-in page takes its token from that page; a
  token minted on the anonymous sign-in page is refused there (found while building WP12, covered by the flow tests).

## Revisit when

The Web needs more than one instance (the store must move out of memory: a new ADR, as ADR-0008 says), authservice
changes its refresh-token rotation or lifetime, or a refresh outage signing users out becomes a complaint.
