# ADR-0022: Response viewer: text only, one send per circuit, and per-client limits on the kernel's handler

- **Status:** Accepted
- **Date:** 2026-10-31
- **Work package:** WP20

## Context

The response to a send is the one piece of content this product displays that a stranger wrote: the target server chose its
body and its headers. A browser that rendered it as markup would run the target's script inside the Web's origin, with the
signed-in user's session behind it. The sender also holds one connection per send, and the Web waits for it. This record
fixes how the response is shown, how one circuit's sends relate to each other, and the limits the execute client waits under.

## Options considered

1. **Render an HTML body in a sandboxed frame (Preview).** Rejected for this work package. It needs an iframe and a
   content-security policy that allows nothing from the response. The page would then carry the response's markup in its
   own tree, and the rule "no markup from a response" would have an exception to test. Preview is shown disabled, with its
   reason, so the question stays open for an owner to decide.
2. **Parse the body into a tree of elements and render that.** Rejected. A parser is a second surface to attack, and the
   output is still the target's text. Plain text, encoded by Razor, has no such surface.
3. **Chosen: every value from the response is text, encoded by Razor.** The page never holds a `MarkupString` or a
   `RenderFragment` built from a response, and the JSON colours are spans around encoded text. A test renders a body and a
   header with markup and asserts that no element is created.
4. **Send each request in its own circuit-free task, with the newest result winning by timestamp.** Rejected. Two sends
   from one circuit can finish in either order, so a sequence number is the only reliable test.
5. **Chosen: one send per circuit, with a sequence.** A new send cancels the one before it. A result for a send that is no
   longer the latest is dropped, so an old answer never overwrites a newer one. Cancelling cancels the call and leaves the
   page idle, with no exception to observe.
6. **Give the execute client its own resilience handler with longer limits.** Rejected by architecture rule R9, which allows
   one handler, the kernel's. Named options on the library's handler were also not applied to a single client: in a test a
   send was cut at the 30 s default and a POST was retried.
7. **Chosen: the kernel's one handler reads a per-client total timeout from `HttpClientTimeouts`, keyed by the client's name.**
   The Web sets the `execution` entry to 120 s. The sender's deadline is 60 s (CONTRACTS §4), and the Web waits longer so
   that it does not give up on a send that is still working.

## Decision

- **Text only.** `ResponseView` and `BodyView` hold strings. The panel writes them with Razor's encoding. The JSON scanner
  (`JsonPretty`) returns tokens of kind text, never markup. A body that is not valid JSON is shown as raw text, and the
  page says so. Binary (base64) is never shown: the page states its size, and its download is disabled.
- **Preview** is disabled, with the reason written beside it.
- **Large bodies.** Lines are drawn 400 at a time, behind "show more lines". A token longer than 4096 characters is split, so
  no single node holds the body. The JSON scanner is iterative (an explicit stack), so a body nested ten thousand levels deep
  is read without recursion; its indentation stops growing at 64 levels.
- **Messages.** Each refusal code the sender returns has its own sentence (`ssrf_blocked_address`, `ssrf_blocked_scheme`,
  `ssrf_blocked_port`, `dns_failed`, `too_many_redirects`, `invalid_url`). A code this page does not know gets a sentence
  that names the category, and an unknown outcome is named as unknown, never as success. The service's message is shown
  beside it as text; the service has masked it (CONTRACTS §4).
- **Sends.** `SendCoordinator` is scoped per circuit. The environment comes from `EnvironmentSelection`, which is always
  none until WP21, so a send carries no variables. The token is the circuit's, read through `SessionExecutionGateway`, and
  never reaches the page.
- **Limits.** `HttpClientTimeouts` maps client names to total timeouts. The kernel's `ApplyLimits` sets the attempt timeout,
  the total timeout and the circuit breaker's sampling (which the library requires to be at least twice the attempt timeout)
  for each listed client. For every client, the kernel disables retries for unsafe methods: a retried POST sends the same
  write, or a third party's request, twice.

## Consequences

- Every backend client now refuses to retry a POST or a PUT. Before this, the library's default retried them. That is a
  deliberate change for all services, and the Web's other calls are unaffected in every other respect.
- The panel renders a body up to 5 MB, which is what the sender keeps. A 5 MB JSON body is formatted in one pass and drawn
  400 lines at a time; the formatting holds the tokens for the body in memory until the send is replaced or the circuit ends.
- A send that the sender is still working on can be cancelled from the page, and the sender then sees the connection close.
  The sender's own deadline still applies to the target.

## Not verified here

- The panel was rendered in bUnit, not in a browser: the layout at 390 px, the token colours in both themes, and the
  keyboard focus were not looked at.
- No real sender was reached. The execute path was exercised against fakes and a recording handler, through the kernel's
  real handler chain. The end-to-end journey (WP26) is the first run against the real service.
- The 120 s wait was not measured against a slow real target.

## Revisit when

- Preview is wanted. It needs a separate record: a sandboxed frame, a policy that allows no script and no request out of it,
  and a test that proves each.
- The sender streams bodies, or the Web needs more than 5 MB at once.
- A client needs a different retry policy for unsafe methods. That is a per-client option on `HttpClientTimeouts`, decided here.
