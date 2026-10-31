# ADR-0016: Import threat model for Postman collections

- **Status:** Accepted
- **Date:** 2026-10-31
- **Work package:** WP16

## Context

CollectionsService reads a Postman Collection v2 file that a user uploads, and writes one back out (CONTRACTS §2:
`POST /import/postman`, `GET /collections/{id}/export/postman`). The file is untrusted input from anyone who can reach the
upload. Postman collections can carry scripts, variables, and literal credentials, and they are often pasted from other people.
Three things must not happen: a literal secret reaches the database, a script or a network call runs, or a hostile file takes the
service down or fills its memory. The importer also must not give a different answer from the API's own validation, or a
collection could be imported that the builder cannot save.

[ADR-0006](0006-secret-variables.md) fixes where a secret may be written, and CONTRACTS §1 fixes the `RequestDefinition` rules.
ADR-0014 and ADR-0015 cover what the sender and history keep; neither reads a file.

## Options considered

1. **Run the file's scripts and resolve its variables at import.** Rejected. It is a remote-code path with no purpose here: the
   service never runs scripts, and an imported variable is a value the user sets later in an environment.
2. **Keep literal secrets as they are and warn.** Rejected. A warning is read once, and the database keeps the secret for good
   (ADR-0006: secrets are encrypted at rest and never returned by a `GET`).
3. **Refuse the whole file at the first literal secret.** Rejected. A user with a real collection would be stuck, and the file
   could never be imported without editing it first.
4. **Replace each literal with a generated `{{variable}}`, name it in a warning, and never store the literal.** Chosen. The
   collection imports, the references work once the user sets the variables, and no literal is kept anywhere.
5. **Validate imported requests with a second, looser rule set.** Rejected. A definition must be judged the same way wherever it
   comes from, so the importer calls `RequestDefinitionValidator`, the one the API uses.

## Decision

**Limits.** Each limit is checked before the work it bounds.

| Limit | Value | Where | Answer |
|---|---|---|---|
| File size | 5 MB (the multipart envelope adds 64 KB) | Before the body is read, and again while it is read | `413 import_too_large` |
| JSON nesting | 64 levels | The parser (`JsonDocumentOptions.MaxDepth`) | `400 import_invalid` |
| Folder nesting | 20 levels | The walker | `400 import_invalid` |
| Folder depth in the model | 8 levels | The walker: a folder deeper than 8 is not created, and its contents go into the level-8 folder | Warning `depth_flattened` |
| Folders and requests in the file | 5500 | The walker, counted as it goes | `413 import_too_large` |
| Requests in a collection | 5000 (CONTRACTS §2) | The walker | `413 import_too_large` |
| Folders in a collection | 500 (CONTRACTS §2) | The walker | `413 import_too_large` |
| Rows per list (headers, query, form fields) | 100 (CONTRACTS §1) | The reader | `400 import_invalid` |
| Each request definition | 1.5 MB of JSON, and the validator's limits | The shared validator | `413` or `400` as the validator says |
| Collections per workspace | 200 (CONTRACTS §2) | Inside the import transaction | `409 conflict` |

The file is read into memory once, after the size check, and then parsed into a `JsonDocument`. "Streaming" in the WP text is
therefore a bounded read, not a streaming parser: the memory is at most 5 MB of file plus its parsed form, and the depth limit
is applied by the parser before the walk.

**Refusals.** A broken rule refuses the whole file, never part of it. Wrong JSON types (a text field that is an object, an array
where an object is expected, a null item), a non-UTF-8 file, a file that is not JSON, a schema that is not Postman v2, and every
rule of the shared validator are `import_invalid`, and the answer names each path that broke a rule. Nothing is written for a
refused file.

**Literal secrets.** A value is a literal when it is not a `{{name}}` reference. Literals are replaced, never stored:
- auth helpers: the bearer token, the basic password, and the API key value (ADR-0006: an auth-helper secret is a reference only);
- a header, a query parameter or a form field whose name is sensitive (CONTRACTS §4: `Authorization`, `Proxy-Authorization`,
  `Cookie`, `Set-Cookie`, `X-Api-Key`, `X-Auth-Token`, or a name containing `token`, `secret`, `password`, `apikey`, `api-key`
  or `api_key`). A header value that already contains a reference (`Bearer {{token}}`) is kept;
- a query parameter written into the URL itself, when the file has no query rows for it.

Each replacement is named by a generated variable (`imported_<kind>_<n>`, unique within the import, matching CONTRACTS §1's name
syntax) and by a `secret_not_imported` warning that names the field and the variable, and never the value. The import is stricter
than CONTRACTS §1 for headers: that section does not police header values, but this one does, because an imported file is the
most common place a literal `Authorization` header enters.

**Scripts and variables.** A `event` (scripts) or `variable` block at any level is never run and never stored. Each one is named
by `scripts_ignored` or `variables_ignored`. Auth set on a folder or on the collection is not applied, and it leaves
`unsupported_auth`. An auth type other than the four helpers (`oauth2`, `digest`, and so on) leaves the same warning and the
request is saved without auth.

**Bodies.** `raw` (with its language), `urlencoded` and `formdata` text fields are imported. A form-data file part, `graphql`,
`file` and any unknown mode are dropped and named by `unsupported_body`.

**Names and descriptions.** A name is trimmed, cut to its limit (100 for a collection or folder, 200 for a request), or set to a
fallback when empty. A description longer than 500 is cut. Each change is a warning.

**No execution, no network.** The importer reads bytes. It does not evaluate, fetch, resolve a host or open a file named in the
collection.

**Transaction.** The collection, its folders and its requests are written by one `SaveChanges` inside the collections transaction
(`CollectionsTransactions.RunAsync`). The workspace's collection count is read under the workspace lock. A failure leaves nothing;
the PostgreSQL test in `PostmanIoEndpointTests` checks it with an interceptor that fails the insert of the last table.

**Warning codes.** The codes an import can return (CONTRACTS §2 names some as examples): `secret_not_imported`, `scripts_ignored`,
`variables_ignored`, `unsupported_body`, `unsupported_auth`, `name_adjusted`, `text_truncated`, `depth_flattened`, `row_skipped`,
`item_skipped`, and `warnings_omitted` (the count of warnings past 500). The cURL import (WP17) uses the same codes for the same things.

**Export.** A deterministic Postman v2.1 file: `info._postman_id` is the collection id, folders and requests follow their sort
orders and then their ids, and the schema URL is fixed. A request's query parameters are written after its URL in `raw`, and the
`query` list carries the same rows. The importer takes the query text off `raw` when the enabled rows account for it, so an
export imports back to the same structure. A `{{variable}}` is written as it is. A file exported and imported again has the same
structure (the round-trip tests check it on every realistic fixture).

## Consequences

- A hostile file costs at most the limits above, and each refusal is a stable code. The hostile tests cover a 10 MB file, nesting of
  5000, 100k items, wrong types, nulls, and a giant URL and name.
- The variables a literal leaves behind must be set by the user. Until they are, the variable stays in the request as text and is
  reported as unresolved (CONTRACTS §1 and §4, `unresolvedVariables`).
- The importer keeps no state between imports. Its warnings are capped at 500 and the rest are counted, so one file cannot produce
  an unbounded answer.
- A Postman raw URL that carries a disabled query parameter is kept as it is, so the parameter is sent. Postman's own export never
  writes that, so the case is rare; it is not repaired silently.
- The URL path variables of a Postman request (`variable` in the URL object) are ignored without a warning. They have no place in
  the model.
- The cURL import (WP17) shares the result shape, `ImportResult`, and the same validator and transaction. Its literal-secret rule
  must match this one.

## Not verified here

- The sender's answer for a generated variable that is still unset. It is the sender's rule, tested in WP14, not here.
- A file exported by Postman itself (as opposed to this exporter) is not tested; only fixtures written in the Postman v2.1 shape are.
