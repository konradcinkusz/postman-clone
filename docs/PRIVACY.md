# Privacy

What personal data this system holds, where, for how long, and what happens when someone asks for it to be
removed. This is a description of the code on `main`, not a privacy notice and not legal advice. The
published notice (`/legal/privacy`) is a placeholder, and that is an open item (OI-04).

## 1. Summary

- Identity lives in **authservice**, a separate pinned service. This repository holds no passwords.
- The services hold **user ids** (GUIDs) on the data they write. They do not hold names or email addresses,
  with one exception: the audit trail in authservice does.
- Users write **their own content**: collections, requests, environment values, and the responses they receive
  into history. That content can contain personal data of its own, such as a name in a request body or in an
  API's response. The system cannot tell.
- **Secrets are not stored in clear.** Secret variables are encrypted; tokens never reach the browser; logs
  carry no bodies, header values or variable values.
- **Erasure is incomplete.** Deleting an account in authservice does not remove this system's data that
  refers to the user's id. There is no erasure consumer (OI-03).

## 2. Personal data by store

### 2.1 authdb (authservice v0.3.4, external)

Source: `src/AuthService.Data/Models/` in the pinned tag, read on 2026-10-31.

| Table (model) | Personal data |
|---|---|
| `ApplicationUser` (extends ASP.NET Identity `IdentityUser`) | email, user name, password hash, profile image URL, creation time, last login time, soft-delete fields, organization memberships |
| `RefreshToken` | user id, a **hash** of the token (not the token), family id, expiry, revocation |
| `UserConsent` | user id, consent type and version, acceptance time, **IP address, user agent**, locale |
| `OrganizationInvitation` | the invitee's **email**, the invitation token, the inviter's user id, status, email attempts and last error |
| `AuditEvent` | actor user id, **actor email**, target user id, **IP address, user agent**, action, metadata |

### 2.2 collectionsdb (CollectionsService)

Source: `src/PostmanClone.CollectionsService/Domain/`.

| Model | Personal data |
|---|---|
| `Collection`, `Folder`, `SavedRequest` | workspace id, `CreatedBy` (user id), timestamps, names and descriptions the user wrote |
| `SavedRequest.Definition` (`jsonb`) | the request as the user wrote it: URL, headers, query, body, auth helper references. Literal secrets are refused by the validator (`secret_literal_not_allowed`), so auth-helper secrets are `{{variable}}` references only. Other fields are free text. |

### 2.3 environmentsdb (EnvironmentsService)

Source: `src/PostmanClone.EnvironmentsService/Domain/`.

| Model | Personal data |
|---|---|
| `Environment` | workspace id, `CreatedBy`, name, timestamps |
| `Variable` | key, **value in clear when not secret**, `Ciphertext` when secret (AES-256-GCM, `v1:` envelope, AAD = workspace, environment and key; ADR-0006), enabled flag |

Non-secret values are stored in clear, by design: they are the user's configuration. A user who puts a personal
value into a non-secret variable has put it in clear.

### 2.4 executiondb (ExecutionService)

Source: `src/PostmanClone.ExecutionService/Data/HistoryEntryRow.cs`.

| Column | Personal data |
|---|---|
| `WorkspaceId`, `UserId` | the user and workspace that sent the request |
| `ExecutedAt`, `Method`, `Url` (masked template), `StatusCode`, `Outcome`, `TotalMs`, `SizeBytes`, `SavedRequestId`, `EnvironmentId` | metadata about the send. The URL may carry personal data the user typed into it. |
| `RequestJson` | the request **template**: variables as `{{name}}`, secrets masked (ADR-0015) |
| `ResultJson` | the result: response headers and body up to 64 KB, masked. **A third party's response can contain personal data**, and the masking covers only known secret values, not personal data in general. |

The sender does not store the response in full; only the first 64 KB of the body are kept (`HistoryBodyMaxBytes`).

### 2.5 The Web (PostmanClone.Web)

Stores **nothing on disk and nothing in the browser**, and has no database. Verified on 2026-10-31: no file writes, no EF
or Npgsql reference, no `localStorage`, `sessionStorage` or JavaScript interop in `src/PostmanClone.Web`. Its in-memory state:

- the session store (ADR-0005, ADR-0013): the access and refresh tokens and the claims of each signed-in session;
- the open request tabs and the builder's draft (WP18, WP19), and the last response of a send (WP20), held per circuit.
  They are lost on a reload or a restart.

A Postman file uploaded on `/import` is streamed to CollectionsService and is not kept by the Web. The Web logs the
export outcome (the collection id and the status code) and not file contents (`CollectionDownloadEndpoints.cs:63,84`).
The browser holds an HttpOnly cookie with a random id.

### 2.6 Logs and telemetry

- Services log the **method, host, status, sizes and durations**. Verified in `RequestExecutor` and `EnvironmentsService`: the
  request and response bodies, header values and variable values are not logged. The target **host** is logged
  (masked as text, but a host can be personal, for example a private address).
- `EnvironmentAudit` logs environment id, user id and a count when a resolve happens, and never a value.
- OpenTelemetry exports only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (OPERATIONS.md). What an exporter would
  send by default, including HTTP span attributes, was not inspected and no test covers the exporter (OI-18).

## 3. Secrets and masking

| Where a secret could appear | What happens |
|---|---|
| Environment secret variable, at rest | encrypted, ciphertext only in the database (`SecretLeakTests.Database_rows_hold_ciphertext_only...`, run on the InMemory provider) |
| Environment API, any response | `value` is `null` and `hasValue` says whether one is stored. The only plaintext is the internal `resolve` call from ExecutionService to EnvironmentsService. |
| Auth-helper fields in a saved request | `{{variable}}` references only |
| Sender, URL / headers / body | the resolved value is replaced by `••••••` in the returned `finalUrl`, the response headers, and the body (ADR-0012, CONTRACTS §4) |
| History | template and masked result only; a stored request never contains a secret value (ADR-0015) |
| Logs | no values, no bodies, no header values |
| Browser | the access and refresh tokens never leave the server; the cookie holds a random 256-bit id |
| Masked header names | `Authorization`, `Proxy-Authorization`, `Cookie`, `Set-Cookie`, `X-Api-Key`, `X-Auth-Token`, and any name containing `token`, `secret`, `password`, `apikey` or `api-key` |

Known limit: a secret that the 5 MB response cap cuts in half is not masked, and only the prefix that was kept is visible.
The caller is a workspace member who already holds that secret (OI-12).

## 4. Retention

| Data | Kept until | Who can remove it now |
|---|---|---|
| authservice users, sessions, consents, audit | authservice's own rules: soft-deleted users are removed for good after `ApplicationUser.DefaultRetentionDays` (30 days) by `UserCleanupService`; audit events by `Audit:RetentionDays` (default not checked here) | authservice's endpoints (see §5) |
| Collections, folders, requests | until deleted. No time-based expiry. | creator or Admin/Owner, through the API (`DELETE /api/v1/collections/{id}`, folders, requests). The UI for this is WP18–WP19. |
| Environments and variables | until deleted | creator or Admin/Owner, through the API (`DELETE /api/v1/environments/{id}`) |
| History | **200 entries per user and workspace**, trimmed on insert, oldest first (`HistoryRetentionPerUserWorkspace`). No time-based expiry. | the user, through the API: delete one or clear all (`DELETE /api/v1/history`, `DELETE /api/v1/history/{id}`). No UI until WP22. |
| Web sessions | 8 hours idle, 7 days absolute, or until a restart | sign-out; restart |
| Builder drafts, open tabs and the last response | until the circuit ends (reload, restart) | the user, by closing the tab or reloading |
| Invitations | 7 days (authservice) | authservice |

## 5. Erasure and export: what actually happens

**Authservice account deletion.** authservice v0.3.4 exposes `DELETE /api/v1/auth/account` (`AuthController.cs`, line 869). It requires the password (for accounts with one), revokes the user's refresh tokens, and marks the user
deleted with a scheduled permanent deletion 30 days later (`ApplicationUser.DefaultRetentionDays`). A background service
(`UserCleanupService`) performs the permanent deletion. These facts come from reading the source; **this
repository does not call that endpoint, and nothing in the Web offers account deletion** (AUTH.md, Not in scope).

**What that does not do in this system.** No service in this repository subscribes to or polls authservice's
deletion. After permanent deletion in authservice:

- `collectionsdb` and `environmentsdb` keep their rows, with the user's id in `CreatedBy`. Those ids are returned by the
  API to workspace members (collection and environment DTOs carry `createdBy`).
- `executiondb` keeps the deleted user's history rows. Retention trims only when that user inserts a new entry, and a deleted
  user cannot send, so those rows stay until someone removes them from the database. The API cannot reach them after the user is
  gone, because history is served only to its own user.
- Nothing removes the user's content from collections or environments, because the content belongs to the workspace.

**Export.** There is no per-user data export. The Postman export (`GET /api/v1/collections/{id}/export/postman`) exports
one workspace collection, and does not carry user ids. authservice's export endpoint (`/api/v1/auth/export`) is not used
by this repository.

**What would close the gap** (owner decisions, not built): an erasure job that reads authservice's deletion and removes or
anonymizes user ids in the three service databases; a decision on whether collections a deleted user created are
kept for the workspace or removed; a per-user export; a privacy notice that describes all of this.

## 6. Data flows outside the system

- **Sender.** A request goes to whatever host the user names. The data in that request (headers, body, the resolved
  secrets) is sent to that host. This is the purpose of the product, but the user should know that a secret used in a
  request is sent to its target. Egress is restricted by the SSRF guard, not by a privacy control.
- **Invitations.** authservice sends invitation email through SendGrid when a key is configured, and otherwise logs the
  token (Production withholds it). This repository sends no email itself.
- **authservice** may send verification and reset email when configured (out of scope here).

## 7. Open points

| Point | Status |
|---|---|
| Privacy notice and terms | placeholders at `/legal/privacy` and `/legal/terms` (OI-04) |
| Erasure propagation | not built (OI-03) |
| Who may delete a user's history after authservice deletion | not decided |
| Retention of authservice audit events | depends on `Audit:RetentionDays`, not checked here |
| Legal basis, data-protection roles, transfers | not assessed; no owner decision recorded |
