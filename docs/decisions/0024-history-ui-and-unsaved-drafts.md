# ADR-0024: History UI, and a draft opened from history is unsaved until it is saved as a new request

- **Status:** Accepted
- **Date:** 2026-10-31
- **Work package:** WP22

## Context

ExecutionService keeps each user's sends (ADR-0015), and the Web must show them: a list, a detail, a delete of one entry, a clear of
all, and "open in builder", which turns a past send back into something the user can edit and send again. Two things make that
hard. The builder only holds saved requests, each with a collection and a version, so a history entry has nothing to attach to. And
the stored request is the masked template: a secret is `••••••` and was never kept, so a draft opened from history holds the mask
where the value was. Sending that draft as written would send the mask as if it were the value.

## Options considered

1. **Open in builder saves a new request into a collection.** Rejected. It writes data into the user's collections that the user did
   not ask to save, and it chooses a collection for them.
2. **Open in builder restores the secret values from the environment.** Rejected. The history never holds a secret, and the page must
   not expand one (ADR-0006, ADR-0015). Environments arrive with WP21, and the mask is not the reference.
3. **Open in builder makes an unsaved draft.** Chosen. The draft has a local id that no service row has. It is dirty from the start,
   so closing its tab asks first. It can only be saved as a new request, through save as, which the user chooses.
4. **Strip the masked values from the draft silently.** Rejected. The request would send without a header or a field the user sent,
   and nothing would show it.
5. **Refuse to send or save while the draft holds the mask (chosen), and say which values to enter again.** The page does not
   change the request. The user sees the mask, enters the value or a `{{variable}}` reference, and only then sends or saves.

## Decision

- **Pages.** `/history` lists the caller's entries (20 a page, newest first, a filter on the masked URL), and the selected entry is in
  the address (`?entry=`), so a page can be bookmarked (CONTRACTS §5 names the page; no new route). A panel in the workspace shell
  (`<details>`, no script) lists the 10 newest entries.
- **Open in builder.** `/?reopen=<id>` asks the home page to open the entry once. The page reads the entry, opens an unsaved tab
  titled with the method and the URL, and says the draft is not saved.
- **Masked values.** `HistoryReopen.MaskedValueCount` counts the string values that still hold the mask, in any field. The page refuses
  to send or save while the count is above zero, and shows the reason. A `{{name}}` reference is not a mask, so it is not refused.
- **Unsaved drafts.** Send records no saved-request id. Save is disabled. Save as is available. The workbench refuses to save an
  unsaved draft by its id, so a save never writes to a row that does not exist.
- **Destructive actions.** Delete one and clear all are two steps: the first click asks, the second performs the call. The text says
  it cannot be undone and, for clear, that only the caller's entries in this workspace are removed.
- **Live update.** The page raises `IHistoryChanged` when a send is recorded (its answer has a history id). The panel subscribes on
  start, reloads through the renderer, and unsubscribes in `Dispose`. A raise after dispose reaches nobody.
- **Time.** The relative times are read through `TimeProvider`. Tests use a fake clock relative to now and pin no calendar date.
- **Display.** Every stored value is shown as the text the service returned, through the response panel or Razor's encoding. The page
  never unmasks, never renders markup from an entry, and shows a truncated body as truncated.

## Consequences

- A re-opened entry with a sensitive header or form field needs the value entered again before it can be sent or saved. That costs
  a step; it is the cost of never restoring a secret (ADR-0015 records the same cost).
- The mask check is the page's, not the validator's, so the client-side validator's parity with the service is unchanged.
- The panel loads on every render of the home page. It is a single call of ten rows, so the cost is small.
- A recorded send refreshes the panel only when the send was made on this circuit. Another tab of the same user reloads on its own
  next visit.

## Not verified here

- The browser layout at 390 px and in both themes was checked only where the report says so (see the pull request). The bUnit tests
  check markup and behaviour, not the layout.
- The end-to-end journey (history, reopen, delete) is WP26's, against the real stack.

## Revisit when

- A reference-preserving rule is wanted: keep a `{{name}}` reference on a sensitive header, and mask only literal values. ADR-0015
  names this as the record that would supersede its rule.
- Several tabs of one user must agree on the history at once. That needs a push from the service or a shared store, not a signal on
  one circuit.
