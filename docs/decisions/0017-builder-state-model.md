# ADR-0017: Builder state model: the query lives in the table, drafts live in the circuit, saves are versioned

- **Status:** Accepted
- **Date:** 2026-10-31
- **Work package:** WP19

## Context

The request builder edits a `RequestDefinition` that the sender later assembles (ExecutionService, WP11). Three
questions had to be settled before the editor could be written, and each one changes what a user can break.

The first is where the query of a URL lives. The stored definition has a `url` and a `params` list, and the sender
**appends** the enabled params to the URL, keeping any query the URL already carries (`UrlBuilder`). The Postman importer
(WP16) and the cURL generator follow the same rule: the query is taken off the URL and the rows carry it. The cURL
importer (WP17) keeps the query in the URL and leaves the rows empty. A builder that shows the query in the URL bar and
also writes it into the URL would send every parameter twice.

The second is where an edit lives until it is saved. The Web keeps sessions and open tabs in server memory, one instance
(ADR-0008), and nothing an editor holds may reach the browser's storage.

The third is what a save does when someone else saved the same request in between. The service has an optimistic
version (`If-Match`, CONTRACTS §2), and the builder must not overwrite a newer version without the user choosing to.

## Options considered

1. **Keep the query in the URL and mirror it into the table.** Rejected: the sender would append the rows to a URL that
   already carries them, so the builder would send each parameter twice.
2. **Keep the query in the URL, and let the table hold only the rows the URL does not show.** Rejected: two sources of
   truth for one list, and the URL bar would have to show both. The table is the one place a row can be edited.
3. **The table holds the query; the URL bar shows the URL with the enabled rows and writes the base back.** Chosen. The stored
   URL has no query, and a URL that arrives with one (a cURL import) has its pairs moved into the table on load, in front of
   the rows, which is the order the sender uses.
4. **Drafts in the browser (local storage, or a hidden field).** Rejected: a draft is not a per-viewer convenience, it is
   unsaved work, and ADR-0008 keeps state on the server.
5. **Drafts in the circuit, per request, dropped when the tab closes or the request is deleted.** Chosen. An open tab keeps
   its draft while the user works in other tabs; a restart loses it, as it loses every open tab (ADR-0008).
6. **Last write wins.** Rejected: a save over someone else's change is silent data loss.
7. **Optimistic version (`If-Match` on the version the editor read); a conflict is shown with two choices.** Chosen. Reload
   takes the service's version. Overwrite reads the current version and writes the draft over it, so the replacement is
   deliberate.

## Decision

- **Stored form.** A definition is saved with the base of its URL (and the fragment, which the sender drops) and every query
  pair in `params`. `RequestDraft.ToDefinition()` writes that form. A definition loaded with a query in its URL is moved into
  the table on load, and the saved baseline is taken after the move, so a request that was only moved is not dirty.
- **The URL bar.** The text is the one the user typed. A table edit rewrites the query part and keeps the base and the
  fragment. A URL edit reads the query into the table: the enabled rows with a key take the parsed pairs, in order, reusing
  the ids of the rows at the same place. A disabled row or a row with an empty key is not in the URL, so it keeps its index.
  A pair with an empty key is never written, so an incomplete row does not put `?=` into the URL.
- **Encoding.** A query key or value is UTF-8 percent-encoded, except RFC 3986 unreserved characters, the sub-delimiters
  other than `&`, `=` and `+`, `:`, `@`, `/`, `?`, and the braces of a `{{variable}}`. A `+` stays a literal plus both ways.
  An escape that cannot be read, or whose bytes are not UTF-8, is kept as written.
- **Dirty.** A draft is dirty when its canonical definition differs from the saved one. The canonical form keeps only the
  fields of the chosen body and auth type, so a value typed under one type is never sent under another.
- **Saves.** A save sends the version the editor read. A success replaces the editor with the service's copy. A conflict
  marks the editor, and the user chooses reload (the draft is discarded) or overwrite (the current version is read and the
  draft is written over it). A rename or a move made from the tree gives a clean editor the service's copy, and a dirty one
  keeps its draft and its version, so its next save is the conflict the user decides on.
- **Save as.** A new request gets its own editor. The source tab keeps its draft and stays dirty, so a save as never discards
  work.
- **Validation.** `DraftValidator` mirrors the service's rules with the same field paths and codes. `DraftValidatorTests`
  reads `tests/Shared/validation-cases.json`, which the service reads too, and judges 1500 seeded definitions with both
  validators. The service stays the boundary: the page only saves a round trip.
- **Secrets.** A secret field accepts one `{{name}}` reference. A literal is refused beside its field and never sent, and
  the message never repeats it (ADR-0006).
- **Bulk text.** A key/value list may be edited as `key: value` lines. A disabled row starts with `//`, the way Postman writes
  it. The first colon splits, so a name with a colon cannot be written in bulk mode, and the editor says so.

## Consequences

- A request that was saved with its query in the URL (a cURL import) is saved with the query in the table. The sent request
  is the same, except for one case below.
- **Known difference.** The sender encodes table values strictly (`Uri.EscapeDataString`), so a literal `+` that a cURL
  import left in a URL query is sent as `%2B` after the first save from the builder, where the import sent a raw `+`. A server
  that reads `+` as a space would then read it differently. This is recorded as a follow-up for WP11 and WP17; the builder
  does not change the sender's encoding.
- A URL typed with a bad escape (`100%`) is kept as typed and encoded on save (`100%25`), which the server reads as the same value.
- The draft lives in server memory. A restart, or a closed tab, drops an unsaved draft. The tab says so before it closes
  (a dirty tab stays open unless the user discards it).
- The variable preview under the URL bar is a line of spans, not highlighting inside the input, because an `<input>` cannot
  hold markup. Unresolved variables are marked only once an environment is selected (WP21 passes `Known`).

## Not verified here

- The layout at 390 px was not checked in a browser in this session (no running backend, no sign-in). The stylesheet
  contract and the mobile grid rule are pinned by reading, not by a screenshot.
- The sender's handling of a `+` in a table value is taken from its code, not run against a live target.

## Revisit when

- The sender changes how it encodes table rows or keeps a URL query, which would change the stored form.
- The Web runs as more than one instance: the drafts and the open tabs would have to move out of memory (a new ADR, as
  ADR-0008 says).
- Environments (WP21) need the URL bar to show resolved values, not only the unresolved marks.
