# ADR-0006: Secret variables: encrypted at rest, referenced by name, masked everywhere

- **Status:** Accepted
- **Date:** 2026-10-26
- **Work package:** WP00

## Context

An API client holds credentials: bearer tokens, passwords, API keys. Users keep them in
environments and reference them from requests as `{{name}}`. The product has to store them, send
them on the user's behalf, and show them to nobody who should not see them. Postman solves this
with environment secrets; this product follows the same model.

The secrets pass through several places that must not keep them: the database, the UI, the request
history, the logs and the URL bar of a shared link.

## Options considered

1. **Plaintext variables in the database.** Rejected: a database dump becomes a credential dump.
2. **Secrets in an external vault**, read by each service. Rejected for now: a second infrastructure
   dependency for a local-first product, and the vault's access policy would still need the same
   masking rules. It stays a revisit option.
3. **Encrypted at rest with AES-256-GCM, bound to their location, with references only in the
   auth helpers, and masking everywhere the value could appear.** Chosen.

## Decision

- Secret values are encrypted with AES-256-GCM. The key comes from `Secrets:EncryptionKey`
  (base64, 32 bytes). The stored form is `v1:<base64 nonce|tag|ciphertext>`.
- The additional authenticated data binds each ciphertext to its place:
  `"{workspaceId}|{environmentId}|{key}"`. A ciphertext copied to another variable or environment
  fails to decrypt.
- Bearer token, password and API key values in the auth helpers accept only a `{{variable}}`
  reference, matched exactly by `^\s*\{\{[A-Za-z0-9_.\-]{1,100}\}\}\s*$`. Anything else answers
  `400 secret_literal_not_allowed`.
- `GET` endpoints never return a secret value. `resolve` is the only endpoint that returns
  plaintext, for ExecutionService, and it writes an audit entry without values.
- Secrets are masked as `••••••` in the UI, the history and the logs, and in any URL shown back.
- A missing or short key answers `503 secrets_unavailable` for secret writes, while non-secret
  features keep working (P8).

## Consequences

- Stealing the database alone does not reveal secrets. Stealing the key does, which is why the key
  lives in the platform's secret store, not in the tree or in the database.
- Users cannot paste a literal token into an auth field. That is an intentional friction, and the UI
  points them to a variable.
- `resolve` is not a confidentiality boundary against other workspace members, who can read a
  secret through the request they send. This matches Postman and is stated in the README.
- Rotating the key needs a re-encryption step. WP00 does not build it; the work package that builds the
  environments service decides how rotation is done, in its own ADR.

## Revisit when

A secret must be shared outside a workspace, a key-rotation requirement arrives, or a vault becomes
part of the platform. Each is a new ADR.
