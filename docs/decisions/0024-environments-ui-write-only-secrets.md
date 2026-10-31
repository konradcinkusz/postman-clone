# ADR-0024: Environments in the Web: secrets are write-only in the page, the chosen environment lives in the circuit, and the page checks what the service checks

- **Status:** Accepted
- **Date:** 2026-10-31
- **Work package:** WP21

## Context

ADR-0006 keeps a secret's value out of every response the service sends, except the one ExecutionService makes. The Web is
the first place a person sees an environment and edits it, so it is where a secret would most easily reach a page: in a
rendered input, in a re-render after a failed save, in a log, or in a URL. The page also has to say which variables a
request's `{{name}}` will resolve to, and it must send the chosen environment with each send (CONTRACTS §4), without
putting the choice in the browser where another tab or a stale cookie could change it.

## Options considered

1. **Show a secret's value in the editor, masked by CSS or `type=password`.** Rejected. The value is then in the markup, in
   the browser's form state and in any re-render, and a masked input is still a value the page sent out.
2. **Load secret values into the page and send back whatever the user leaves.** Rejected. The service answers no value
   for a secret (CONTRACTS §3), so the page would need one it does not have, and it would have to hold the plaintext.
3. **Write-only secrets: the page knows only that a value is stored.** Chosen. A stored secret shows "stored" and a
   placeholder. A new value is typed into an input with no `value` attribute, held in the circuit's row, and sent once.
   An untouched stored secret is sent as `keepExistingSecret` with no value, which CONTRACTS §3 already provides.
4. **Hold the chosen environment in `localStorage` or a cookie.** Rejected. A browser-held choice can outlive the workspace
   or the environment, and the sends must carry an id the server can check. The choice lives in the circuit's memory
   (`EnvironmentSelection`, scoped), the same place as the session and the send.
5. **Check the form only on the server.** Rejected as the only check. The service stays the authority, and its messages
   are shown on the field they name, but the page refuses the obvious cases (an empty key, a duplicate, a secret with no
   value, a limit) before a request.

## Decision

- **A stored secret's value is never in the page.** `VariableRow` holds `Stored` (what the service said: key, flags, and
  `HasValue`) and `TypedSecret` (what the user typed since). The secret input has no `value` attribute and is keyed by the
  row's id, so a save or a removal clears it. A test searches the rendered markup for a sentinel after load, after typing,
  and after a failed save.
- **Replace semantics.** A secret with no typed value is sent as `keepExistingSecret: true` with a null value, but only while
  its key is the one it was stored under: the ciphertext is bound to the key (ADR-0006), so a renamed secret must be typed
  again. A plain variable made secret needs a typed value, and a secret made plain needs a value typed again.
- **Checks mirror the service's rules.** `EnvironmentRules` holds the limits and the key and name patterns. The key pattern is
  anchored to the whole key (`\A…\z`), as the service's attribute requires, so a trailing newline is refused on both
  sides. A parity test runs the service's own attributes on the same inputs.
- **Field messages.** A 400's `errors` map is read by the client. A `variables` message that starts with a row's key is shown on
  that row, a `name` message on the name, and any other message on the table.
- **The choice.** `EnvironmentSelection` holds the environment's id, its name and its enabled variable names. A disabled
  variable is not known, because the sender resolves only enabled ones (CONTRACTS §4). No value is held there. The builder
  marks the `{{names}}` the choice does not define. Deleting the chosen environment, or choosing one that is gone, clears it.
- **Errors.** A 503 `secrets_unavailable` shows a banner and leaves the plain variables usable. A 404 is "no longer exists"
  and the list is read again. A 409 on create is the workspace limit, which the service answers without a code.

## Consequences

- The page holds a typed secret in server memory until the save or the page ends. A new value is in the circuit, never in
  the markup, a cookie or the browser's storage.
- A secret cannot be shown again, not even to its creator. To change a value, the user types it again; the page says so.
- The edit is last-write-wins. CONTRACTS §3 has no version for an environment, so two tabs that edit the same environment
  can overwrite each other. The page does not detect it; the owner may want a version or an `If-Match` in a later amendment.
- Renaming a secret's key requires typing its value again, which is the price of the ciphertext binding.

## Not verified here

- The page was rendered in bUnit, not in a browser: the layout at 390 px, the two themes and the focus order were not
  looked at unless the PR body says otherwise.
- The service's 409 on create carries no code, so the page reads the workspace limit from the outcome, not from a code.
- A key with a trailing newline is refused by both sides. The service's attribute requires a whole-string match, and the
  Web's pattern is anchored with `\A` and `\z` to match it. The service was not changed.

## Revisit when

- A secret must be shown in part (a last-four hint), or shared outside a workspace. Each is a new record, because each
  weakens ADR-0006.
- Two people must edit one environment without overwriting each other. That needs a version on the environment, which is a
  contract amendment.
- The sender needs more than the enabled names for the builder's marking. The choice's shape would change with it.
