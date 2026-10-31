# ADR-0015: History retention, masking and the stored request

- **Status:** Accepted
- **Date:** 2026-10-31
- **Work package:** WP15

## Context

Each send through `POST /api/v1/execute` is recorded by `IHistoryRecorder` (ADR-0014), and the caller can re-open an entry
into the builder, delete it, or clear the history (CONTRACTS §4). The history is the one place where a sent request is kept
after the request has gone, so it must hold no secret value, must stay bounded per user, and must not let one user read or
change another user's entries. Two rules are easy to get wrong: a stored request must keep its `{{name}}` references, or it
can no longer be re-opened as a template, and a secret can appear in the request as a reference, as a literal the user typed,
or in an encoded form of either.

[ADR-0006](0006-secret-variables.md) fixes where secrets may be written and masked. [ADR-0012](0012-variable-semantics.md)
fixes the four forms a secret takes in text, which the sender's masker already searches for.

## Options considered

1. **Store the request after variables are applied.** Rejected. It writes the secret values into the row.
2. **Store the template as the user wrote it, redacted with the sender's own masker, and the result the sender already
   masked.** Chosen. The template keeps its references, the masker covers the four forms, and the result is masked before
   the history sees it.
3. **Store nothing for a request that uses a secret.** Rejected. It would hide most of the history of the users who need it
   most, and it would still need a rule for a literal secret typed in the request.
4. **Keep history by age (for example, 30 days).** Rejected for this work package. CONTRACTS §4 fixes a count of 200 per user
   and workspace, and a count bounds storage without a clock rule to test.

## Decision

- **What a row holds.** Method, the masked template URL, the response status, the outcome, the total time, the response size,
  the masked template request as JSON, the masked result as JSON, and the saved-request and environment ids the caller named.
  Every column is text the sender's masker has already seen (see the masking rules below).
- **Masking of the template** (`HistoryRedaction`). Every secret form is masked in the method, the URL, the parameter, header
  and form-field text, and the raw body. A header, query or form name that is sensitive (CONTRACTS §4, and any name containing
  `api_key`) has its whole value replaced. The auth helper keeps its type, its key name and where the key goes, and drops the
  bearer token, the username, the password and the API key value, because CONTRACTS §4 says auth-helper values are never
  stored.
- **Masking of the result.** The sender masked it before the history sees it (ADR-0014): the final URL, the redirect locations,
  the response header names and values, and the text body. The history does not mask it a second time, because a second pass
  over JSON text would also rewrite its structure.
- **Body cap.** The stored result's body is at most 64 KB (`HistoryBodyMaxBytes`). The cut is made on the masked text, between
  two characters for UTF-8 and on a four-character group for base64. The cut body is marked `truncated`. The caller's answer
  is not affected, and the stored `sizeBytes` is the size the sender kept.
- **Retention.** At most 200 entries per user and workspace (`HistoryRetentionPerUserWorkspace`). The oldest by `ExecutedAt`
  are removed, ties broken by id, in the same transaction as the insert. On a relational provider the transaction runs
  inside the provider's execution strategy, so a transient failure is retried as a whole. The row is re-marked as added on
  every attempt, so a retry does not trust a state left by a rolled-back attempt.
- **Scope.** The workspace is the token's `organization` claim and the user is the token's `sub`, which must be a user id.
  Every history query starts from one scope function, so no query can drop either half. A row of another user or another
  workspace is `404 not_found`, never `403`. A `sub` that is not a GUID has no history: the history endpoints answer `403`,
  and the send's recorder fails and is logged.
- **Failure.** A recorder failure does not fail the send. The caller gets the result with `historyId` null, and the log names the
  exception type and nothing else (ADR-0006, CONTRACTS §4 logging rule).
- **Search.** `q` matches the masked URL template, case-insensitively, so a search cannot reveal a secret.

## Consequences

- Nothing that the sender masks can reach a stored column. The rule is tested on both providers, and on PostgreSQL it is
  tested by reading every column of every row.
- A re-opened entry loses the value of a sensitive header or form field, even when the value was a `{{name}}` reference,
  because the rule masks the whole value. The entry still shows the reference's place. A reference is not a secret, so this
  costs usability, not safety. It is the cost of following the contract's rule without a special case.
- The request body is not capped in the history. The 1 MB raw-body limit of the sender applies, so 200 entries can hold up to
  about 200 MB per user in the worst case. The response body is capped at 64 KB.
- Two sends by the same user that race at the limit can leave 201 entries until the next insert trims the extra one. Neither
  send fails, and the count is still bounded.
- On the InMemory provider, which only the tests use, deletes go through the change tracker, because the provider cannot run
  a bulk delete. Production runs one `DELETE` statement.

## Not verified here

- The `sub` format is verified in authservice v0.3.4 source only: its users are `IdentityUser` values, whose default id is a
  GUID string. It is not verified against a live token, because no authservice runs in this session (no Docker).
- Retention under real concurrent sends. The race described above is reasoned, not measured.
- Storage use under load. The worst case is computed from the limits, not measured.

## Revisit when

- The history stores bodies beyond 64 KB, or searches them.
- The request body needs a cap in the history as well, because the worst-case storage above matters for a deployment.
- A second instance can write the same user's history, which would need the trim to take a lock or a serial key.
- Re-opening a sensitive header is needed. A reference-preserving rule, which keeps a `{{name}}` reference and masks only literal
  values, would be a new record that supersedes this one.
