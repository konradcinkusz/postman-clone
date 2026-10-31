# ADR-0012: Variable semantics and request assembly

- **Status:** Accepted
- **Date:** 2026-10-30
- **Work package:** WP11

## Context

A saved request holds `{{name}}` references, and the sender must turn it into one concrete request. Three
rules are easy to get wrong, and each one leaks something or breaks a request when it is wrong:

- a value that itself looks like a reference must not be expanded again (a secret could expand into another
  name, or a user value could smuggle one in);
- a secret can reach the URL, a header or a body in more than one encoded form, so a mask over the raw text
  alone misses it;
- a header or a query value built from user text can carry a line break and a second header.

[ADR-0006](0006-secret-variables.md) fixes where secrets may be written and where they must be masked. This
record fixes what the substitution, the encoding and the masking do, so that the sender (WP12, WP14) and the
history (WP15) can rely on one implementation.

## Options considered

1. **Expand repeatedly until no reference is left.** Rejected: a value containing `{{x}}` changes meaning, and a
   cycle needs a guard. One pass is simpler and has no cycle.
2. **Expand in the endpoint, with the rules inside the HTTP code.** Rejected: the rules are then tested only
   through the wire. They live in plain types, and the endpoint maps onto them.
3. **Mask the raw secret text only.** Rejected: a secret with a space or an `&` appears encoded in the URL, and a
   base64 `Authorization` carries it encoded too.
4. **Single-pass expansion, a recorded list of every inserted value, and masking of every encoded form, with
   the limits and the header rules enforced where the request is assembled.** Chosen.

## Decision

- **Syntax** is CONTRACTS §1: `{{name}}`, the name `[A-Za-z0-9_.-]{1,100}`, case-sensitive. The expansion is one
  pass over each string; an inserted value is never expanded again. An unknown name stays as written and is
  reported, and the request is still sent.
- **Recording.** `VariableInterpolator` keeps every value it inserted, with its secret flag, for the whole request.
  `SecretMasker` is built from those values, so it knows exactly what to hide.
- **Encoding.** A query name or value is percent-encoded (RFC 3986 unreserved characters pass). A form body
  encodes a space as `+`. The URL keeps its own query as written and drops its fragment. A body is UTF-8.
- **Masking.** A secret is masked in four forms: as written, percent-encoded, form-encoded and base64. The longest
  form is replaced first. A header or query value whose name is sensitive (CONTRACTS §4, and `api_key`) is
  replaced whole. A secret shorter than four characters is masked and reported as a warning, because the
  over-masking it causes is visible to the user.
- **Headers.** Only enabled rows count. A name must be an HTTP token. A value may not contain a control
  character or a line break, and this is checked after expansion, so a line break smuggled through a variable is
  refused. The sender's own headers (`Host`, `Content-Length`, `Connection`, `Transfer-Encoding`, `Upgrade`,
  `TE`, `Expect`, `Proxy-*`) are refused.
- **Auth helper.** Its secret fields accept one reference only (ADR-0006). A literal is refused with
  `secret_literal_not_allowed`, and the refusal never echoes the text. A helper header replaces a row of the same
  name and the replacement is reported as a warning.
- **Body.** A raw body gets a default content type for its language, unless the user wrote one. A multipart body
  takes its boundary from an injected random source, which is the cryptographic one in production, so a test can
  repeat it. The boundary is 128 bits.
- **Limits** (`Execution:` options, CONTRACTS §4): the body is at most 1 MB in bytes, at most 100 headers counted
  after the helper and the content type, and the assembled URL is at most 8192 characters.
- **Errors** carry a stable code and a message that names a field and never a value (ADR-0006).

## Consequences

- The masking rules have one implementation. The history and the logs receive only masked text, and the request
  sent on the wire keeps the real values, which only `RequestAssembler` produces.
- Literal secrets in the auth helper are refused in the assembler as well as the endpoint, so the rule holds even
  for a caller that skips validation.
- A secret that contains the mask character cannot be guaranteed hidden. It is not expected from users, and no
  special case is added for it.
- A URL with no host is refused as `invalid_url`, so `javascript:` is refused here. Scheme allow-listing is the
  sender's (WP10): an `ftp://` URL with a host reaches it and is refused there.
- An empty header value is sent as an empty header. An empty auth secret applies nothing.

## Revisit when

- A new field of a request can hold a secret. It then needs the same reference rule, the same masking and a test.
- The sender needs percent-encoding of a path segment. It is not done here, because the URL is written as the
  user wrote it.
