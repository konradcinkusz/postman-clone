# ADR-0021: Import and export in the Web: a streamed download and an in-circuit upload

- **Status:** Accepted
- **Date:** 2026-10-31
- **Work package:** WP23

## Context

CollectionsService imports and exports Postman v2.1 files (ADR-0016) and cURL commands (WP17). The Web has to offer those to a
signed-in user without giving the browser a token and without holding a copy of a file it does not need. Four questions came up:

1. How the browser downloads a collection: a page script that fetches the file with a token, or a plain navigation.
2. Whether the Postman upload goes through a Web HTTP endpoint or through the Blazor circuit that the page already uses.
3. How the answers are shown: the service's warnings can carry text from the imported file, so that text must be shown as text.
4. Where the "Copy as cURL" output appears, given there is no script to copy to the clipboard.

[ADR-0005](0005-bff-session.md) keeps the bearer on the server, and [ADR-0009](0009-dependency-budget.md) allows no JavaScript interop.
ADR-0016 caps a Postman file at 5 MB, and CONTRACTS §2 caps a cURL command at 20 000 characters.

## Options considered

**Download.** (a) A script that calls the API with a token and saves a blob. Rejected: the token would have to reach the browser,
which ADR-0005 forbids, and the script would need JavaScript interop. (b) A plain `GET /download/collections/{id}` in the Web,
which attaches the bearer on the server and streams the service's answer. Chosen. It is a navigation, so the browser's own download
takes it and nothing runs in the page.

**Upload.** (a) A multipart form posted to a Web HTTP endpoint, which then forwards the file. This copies the file a second time
on the Web and needs an antiforgery token for a file form. (b) The page's own circuit: the browser file is read as the page's
`InputFile` provides it, and the stream goes straight into the request to CollectionsService. Chosen. The file is not buffered
on the Web beyond the framework's own transfer, and the page already holds the session.

**Warnings.** Shown as text, grouped by code, in a fixed order. The text is the service's, and the page never renders it as markup.
Unknown codes are kept in a generic group rather than dropped.

**Copy as cURL.** A read-only page at `/requests/{id}/curl` that shows the service's text in a `<pre>`. One click selects all of it
through CSS (`user-select: all`), so no script is needed. Chosen over a dialog, which would need the page to hold the command in
its own state.

## Decision

- `GET /download/collections/{id}` (CONTRACTS §5) is a minimal endpoint with `RequireAuthorization`. It reads the session's id from
  the cookie, resolves the workspace from the token, and refreshes the token through `TokenRefresher`. It opens the service's
  export for streaming (headers first, body on demand) and copies the body to the response. The file name is the service's, cut to
  a safe leaf name. It sends `Cache-Control: private, no-store` and `X-Content-Type-Options: nosniff`.
- An absent id, or one that belongs to another workspace, is the service's `404`, passed through as text. A non-Guid id is refused by
  the route constraint before any call. A missing session is a redirect to sign-in, and no workspace is a redirect to the explanation
  page. The endpoint logs the collection id and the status, never the body.
- The Postman upload is `CollectionsClient.ImportPostmanAsync`, a multipart request with the `file` part streamed from the browser's
  stream. The client-side limits are the service's (5 MB, a `.json` extension or an `application/json` type, a non-empty file), and
  they are checked when the file is chosen and again at submit. The cURL import checks a non-blank command of at most 20 000
  characters before it is sent.
- Every import outcome is either a result, grouped by code, or a message for its outcome and problem code. Nothing is shown as
  markup.
- The page never writes a request body or a header value to a log, and the page keeps no token.

## Consequences

- A download holds at most one upstream response in flight. The Web does not buffer a collection's export.
- The upload is a single request. The kernel's standard resilience handler is not expected to retry an unsafe method such as POST, so
  the streamed body is not replayed; this is the framework's default, and it is not covered by a test here.
- The cURL page is a new Web route. It is recorded as an amendment in CONTRACTS §5 and adds no API route.
- The download is a navigation, so it cannot show progress. Large exports are bounded by the service's limits, not by the Web.
- A future need for a richer picker (a folder tree in the cURL form, for instance) is a Blazor change in the same page and needs no
  endpoint.

## Not verified here

- The browser's own behaviour for the download and the `user-select` rule. The tests check the headers and the markup, not the
  rendering in a browser.
- The resilience handler's default for POST. It is the framework's behaviour and is not asserted by a test.
