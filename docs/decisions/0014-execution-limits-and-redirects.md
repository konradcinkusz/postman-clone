# ADR-0014: Execution limits, redirects and the send path

- **Status:** Accepted
- **Date:** 2026-10-30
- **Work package:** WP14

## Context

`POST /api/v1/execute` is the one endpoint that makes outbound requests on a user's behalf. The guard
([ADR-0011](0011-sender-threat-model.md)) and the assembler ([ADR-0012](0012-variable-semantics.md)) decide what may be
sent and what it contains. This record fixes the parts they leave open: who follows a redirect, how a response is
read within its limits, what counts as a refusal and what as a failure, and how the send is tied to the caller.

## Options considered

1. **Let `HttpClient` follow redirects and decompress.** Rejected. A redirect would reach the connection without the
   URL checks on each hop, and the response would be decompressed before its size is counted.
2. **One shared `SocketsHttpHandler` for every send.** Rejected for this work package. The timing breakdown (DNS and
   connect) is measured by wrapping the guard's resolver and dialer, and a shared pool cannot tell one send's connections
   from another's. A handler per send costs a connection per send, which is acceptable for a user-driven tool.
3. **Read the whole body, then cap it.** Rejected. The body must be cut at the cap while it is read, so the memory held is
   bounded whatever the server sends.
4. **Chosen:** the sender follows redirects itself, one hop at a time; each hop passes the URL check and the connection
   guard. A per-send handler carries the guard's settings. The body is decoded and capped while it is read. One deadline
   covers the whole send.

## Decision

- **Order.** Shape check (400), then the environment's variables through `EnvironmentsClient` with the caller's bearer
  (404 when absent or in another workspace, 502 when EnvironmentsService fails), then assembly (400 or 413), then the URL
  check (an outcome, not an error), then the send. The workspace is the token's; the body names none.
- **Redirects.** At most `MaxRedirects` (5) hops are followed. The sixth redirect response is refused with
  `too_many_redirects`. 303 becomes GET (HEAD stays HEAD); 301 and 302 turn a POST into GET; 307 and 308 keep the method
  and body. A hop that changes the method drops the body and its content headers. A cross-origin hop drops the
  credential headers (ADR-0011). Every hop is checked by `TargetValidator` and then by the connection guard.
  `followRedirects: false` returns the redirect answer itself.
- **Body.** The read stops at 5 MB of decoded bytes (`ResponseReadCapBytes`) and reports `truncated`. One byte past the cap
  is probed to tell "exactly the cap" from "cut off". Content-Encoding (gzip, deflate, br) is decoded inside the read, so a
  gzip bomb stops at the cap of the output. A response that declares a length above 50 MB is refused with `tooLarge`
  without being read. A body with several codings is passed through as received.
- **Text.** A body is `utf8` when its type is a text type, or when no type is declared, and its bytes are valid UTF-8.
  Otherwise it is `base64`. Masking applies to text only.
- **Deadline.** One `CancellationTokenSource` covers every hop and the body. Its expiry is `timeout`. The send's token
  is cancelled before the last response is disposed, so an unread body is aborted rather than drained.
- **Outcomes.** A response of any status is `completed`. A refusal from the guard or the redirect planner is `blocked`
  with the code from CONTRACTS §4 and a message that names at most the category. A failure to connect or read is
  `networkError` with `network_error`. A failure to decode is `networkError` with `invalid_body_encoding`.
- **Masking.** Every string the caller sees is masked: the final URL, redirect locations, response header names and
  values (sensitive names are replaced whole), and text bodies. The logs carry the method, the masked host, the status,
  the sizes and the durations.
- **Caller.** `ExecutionCaller` holds the bearer only for the EnvironmentsService call. It has no string form that
  would print it.
- **History.** `IHistoryRecorder` is called once per execution. `NullHistoryRecorder` returns no id until WP15.

## Consequences

- No more than the 5 MB cap is held in memory for a body, and no redirect reaches the connection unchecked.
- A secret that the server echoes is replaced in the text body, the headers and the URL. A secret inside binary bytes is
  not searched for, and the response carries it as base64. A secret cut in half by the cap is not masked; it is
  visible only as the prefix that was kept.
- The JSON request body may be up to 8 MB, so JSON escapes do not turn a 1 MB raw body into a false 413. The 1 MB limit
  applies to the raw body itself (`payload_too_large`).

## Not verified here

- No real internet address is reached, and no DNS rebinding is observed: the tests use loopback targets and, for the
  redirect-to-loopback case, a resolver and dialer that answer with a public address and then dial the loopback target.
- Memory use under sustained load and the timing of the 1 GB gzip stream beyond the test run.

## Revisit when

- The sender gains a response-size control beyond the cap, or streams bodies to the Web.
- A secret can be split across the read cap in a way the masker cannot see (a longer masked form than the kept prefix).
- The history stores bodies (WP15); its 64 KB limit is applied there, not here.
