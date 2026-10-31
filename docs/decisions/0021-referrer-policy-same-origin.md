# ADR-0021: Referrer-Policy same-origin, and a Referer check on unsafe requests

- **Status:** Accepted
- **Date:** 2026-10-31
- **Work package:** WP18b (a fix after WP18; the number is the next spare one in [`docs/decisions/README.md`](README.md))

## Context

The kernel's security headers (CONTRACTS §0, [SecurityExtensions.cs](../../src/PostmanClone.ServiceDefaults/Security/SecurityExtensions.cs))
sent `Referrer-Policy: no-referrer`. The origin guard (BLAZOR-WEB-APP §8) refuses any `Origin` that is not
the Web's own host or a configured origin.

With `no-referrer`, the Fetch specification serialises the `Origin` header of a request as the string `null`
(the policy removes the origin along with the referrer). Chromium does this for a form POST, even one from
the same origin. So the sign-in form, which is a same-origin POST, arrived with `Origin: null` and was
refused with `403`. No one could sign in with a browser. WP18 found this; its PR body lists it as an owner
follow-up.

## Options considered

1. **Let the origin guard accept `Origin: null`.** Rejected. `null` is what sandboxed frames and some
   redirected requests send as well. Accepting it lets a page on another site through the guard, which is the
   threat the guard exists for.
2. **Keep `no-referrer` and drop the `Origin` check for form posts.** Rejected: it weakens the guard for every
   endpoint that takes a form.
3. **`strict-origin-when-cross-origin`.** Sends the Origin on same-origin requests, so it fixes the sign-in.
   It still sends the page's origin to other sites, which `same-origin` does not.
4. **`same-origin`.** Chosen. It sends the real `Origin` and `Referer` on same-origin requests (the form POST
   and the circuit negotiation), and sends no referrer across sites. It is the strictest policy that still
   works for the Web.

## Decision

- The kernel sends `Referrer-Policy: same-origin` on every response that the security middleware covers.
- The origin guard is unchanged in its refusals: a foreign `Origin` and `Origin: null` are refused, and so is
  `Sec-Fetch-Site: cross-site` other than a top-level navigation.
- One addition, because the guard did not check `Referer` at all: on an unsafe method (anything but `GET`,
  `HEAD` or `OPTIONS`), a `Referer` that is neither this host nor a configured origin is refused, and so is a
  `Referer` that cannot be parsed. A request with no `Referer` is still allowed. A safe request is not checked
  against `Referer`, so a page load from another site still works.

## Consequences

- A browser can sign in through the form. The test suite pins the header value, the form POST with the
  browser's headers, and the refused `Origin: null`.
- The guard is no more permissive than before. The `Referer` rule can only refuse more.
- `same-origin` gives the other site no referrer. The Web's own pages do not depend on a referrer.
- The origin guard is still a mitigation and not authentication (BLAZOR-WEB-APP §8).

## Revisit when

A page on another site needs to see its own referrer, or the Web is served behind a proxy that rewrites the
host. Then the allowed set (`AllowedOrigins`) and the forwarded-header configuration are revisited, not this
policy.
