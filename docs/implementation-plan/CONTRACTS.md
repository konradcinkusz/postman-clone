# Frozen contracts

Every WP codes against this file. **Do not change a name, type, status code or limit.** You may
add an *optional* field or a new endpoint only by appending to [Amendments](#amendments) in the same
PR (see PROTOCOL §8). A breaking change means: stop, push wip, report `BLOCKED: contract`.

## 0. Conventions (all services)

- Base path `/api/v1`. JSON, `camelCase`, enums as camelCase strings, `System.Text.Json`.
- Ids are `Guid` (string form). Timestamps are `DateTimeOffset` UTC (`...Z`). Services take a
  `TimeProvider`; **tests never pin a calendar date**, they use `FakeTimeProvider` relative to
  "now" or compare relative offsets.
- Errors: RFC 7807 `application/problem+json` with extension `code` (stable, snake_case, listed below)
  and, for 400, `errors: { field: [messages] }`.
- Lists: `?page=1&pageSize=50`, page ≥ 1, pageSize clamped to 1..100 → `Page<T> { items, page, pageSize, total }`.
- Auth: every endpoint `[Authorize]` except `/health`, `/alive` (and the Web's sign-in pages).
  JWT validated against authservice JWKS (`Auth:MetadataAddress`, `Auth:Issuer`, `Auth:Audience`).
  Local/CI default issuer and audience: **`PostmanClone`**.
- **Workspace** = the single `organization` claim. Resolved by the kernel's `WorkspaceClaims.Resolve`:
  `Active(workspaceId, role)` | `None` | `Multiple`. `None`/`Multiple` → every domain endpoint answers
  `403` with code `no_active_workspace` / `multiple_workspaces` (fail closed). Role is the value of
  `organization:{workspaceId}:role` (`Owner|Admin|Member`). Workspace is **never** read from a route,
  query or body.
- Id endpoints: load the row, compare `WorkspaceId`, answer **`404` `not_found`** when absent *or* in another
  workspace. Never 403 for a foreign row. A scoping test exists for every id endpoint.
- Rate limits (kernel policies): `api` 300/min per user, `execute` 60/min per user, `auth` 10/min per IP.
  Uniform 429 body `{ "code": "rate_limited", "title": "...", "status": 429 }`.
- Health: `GET /health` (readiness, includes optional integrations), `GET /alive` (liveness).
  `/health` JSON includes `status` and one entry per optional integration (P8).

### Error codes

`validation_failed` 400 · `not_found` 404 · `forbidden` 403 · `no_active_workspace` 403 ·
`multiple_workspaces` 403 · `conflict` 409 · `payload_too_large` 413 · `rate_limited` 429 ·
`secrets_unavailable` 503 · `upstream_unavailable` 502 · `secret_literal_not_allowed` 400 ·
`import_invalid` 400 · `import_too_large` 413 · `depth_exceeded` 400 · `cycle_detected` 400.

## 1. Shared shapes

```jsonc
KeyValue        { "key": "string(1..256)", "value": "string(0..8192)", "enabled": true, "description": "string(0..500)?" }   // max 100 rows per list
RequestBody     { "type": "none|raw|formUrlEncoded|formData",
                  "rawLanguage": "json|text|xml|html|javascript|null",   // only for raw
                  "raw": "string(0..1048576)|null",                      // only for raw
                  "fields": [KeyValue] }                                  // formUrlEncoded and formData (text only)
RequestAuth     { "type": "none|bearer|basic|apiKey",
                  "bearerToken": "string|null",   // {{var}} reference only (see below)
                  "username": "string|null", "password": "string|null",   // password: {{var}} reference only
                  "apiKeyName": "string|null", "apiKeyValue": "string|null", // apiKeyValue: {{var}} reference only
                  "apiKeyIn": "header|query|null" }
RequestDefinition { "method": "GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS",
                  "url": "string(1..8192)",      // may contain {{vars}}
                  "params": [KeyValue], "headers": [KeyValue], "body": RequestBody, "auth": RequestAuth }
```

**Secret literal rule (ADR-0006).** `bearerToken`, `password`, `apiKeyValue` must be empty or match
`^\s*\{\{[A-Za-z0-9_.\-]{1,100}\}\}\s*$` exactly. Anything else → `400 secret_literal_not_allowed`
(field named in `errors`). Header rows are not policed (UI warns on `Authorization`/`X-Api-Key`/etc.).

**Variable syntax:** `{{name}}`, name `[A-Za-z0-9_.\-]{1,100}`, case-sensitive, no nesting, no
expressions. Unresolved variables are left as the literal text and reported.

## 2. CollectionsService (`/api/v1`)

| Method & path | Body → Response | Notes |
|---|---|---|
| `GET /collections` | → `Page<CollectionSummary>` | `?q=` name contains, case-insensitive |
| `POST /collections` | `{ name(1..100), description(0..500)? }` → `201 CollectionDto` | `Location` header |
| `GET /collections/{id}` | → `CollectionTree` | |
| `PUT /collections/{id}` | `{ name, description? }` → `CollectionDto` | |
| `DELETE /collections/{id}` | → `204` | creator or Admin/Owner, else `403 forbidden` (only after the 404 check) |
| `POST /collections/{id}/folders` | `{ name(1..100), parentFolderId? }` → `201 FolderDto` | max depth 8 → `depth_exceeded` |
| `PUT /folders/{id}` | `{ name }` → `FolderDto` | |
| `POST /folders/{id}/move` | `{ parentFolderId?, sortOrder? }` → `FolderDto` | same collection; `cycle_detected` |
| `DELETE /folders/{id}` | → `204` | cascades folders and requests |
| `POST /collections/{id}/requests` | `{ name(1..200), folderId?, description?, request: RequestDefinition }` → `201 SavedRequestDto` | |
| `GET /requests/{id}` | → `SavedRequestDto` | |
| `PUT /requests/{id}` | `{ name, description?, request }` → `SavedRequestDto` | optimistic concurrency: `If-Match: "<version>"` optional; mismatch → `409 conflict` |
| `POST /requests/{id}/duplicate` | → `201 SavedRequestDto` | name `"<name> copy"` |
| `POST /requests/{id}/move` | `{ collectionId?, folderId?, sortOrder? }` → `SavedRequestDto` | target must be in the same workspace |
| `DELETE /requests/{id}` | → `204` | |
| `POST /import/postman` | multipart `file` (≤ 5 MB) **or** `application/json` body → `201 ImportResult` | WP16 |
| `POST /import/curl` | `{ command: string(1..20000), collectionId, folderId? }` → `201 ImportResult` | WP17 |
| `GET /collections/{id}/export/postman` | → `200 application/json` Postman v2.1, `Content-Disposition: attachment` | WP16 |
| `GET /requests/{id}/export/curl` | → `200 text/plain` | WP17; header values that were `{{var}}` stay `{{var}}` |

```jsonc
CollectionSummary { id, name, description, createdBy /*user id*/, createdAt, updatedAt, requestCount }
CollectionDto     { id, name, description, createdBy, createdAt, updatedAt }
FolderDto         { id, collectionId, parentFolderId, name, sortOrder }
RequestSummary    { id, collectionId, folderId, name, method, sortOrder, updatedAt }
CollectionTree    { collection: CollectionDto, folders: [FolderDto], requests: [RequestSummary] }   // flat, client builds the tree
SavedRequestDto   { id, collectionId, folderId, name, description, request: RequestDefinition, sortOrder, version, createdBy, createdAt, updatedAt }
ImportResult      { collectionId, collectionName, foldersCreated, requestsCreated,
                    warnings: [ { code, message, path } ] }   // e.g. scripts_ignored, secret_not_imported, unsupported_body, truncated
```

Limits: 200 collections / workspace, 5000 requests / collection, 500 folders / collection,
`RequestDefinition` JSON ≤ 1.5 MB (→ `413 payload_too_large`).

## 3. EnvironmentsService (`/api/v1`)

| Method & path | Body → Response | Notes |
|---|---|---|
| `GET /environments` | → `Page<EnvironmentSummary>` | |
| `POST /environments` | `{ name(1..100) }` → `201 EnvironmentDto` | max 50 / workspace |
| `GET /environments/{id}` | → `EnvironmentDto` | **secret values are never returned** |
| `PUT /environments/{id}` | `{ name, variables: [VariableWrite] }` → `EnvironmentDto` | replaces the variable set, max 200 variables |
| `DELETE /environments/{id}` | → `204` | creator or Admin/Owner |
| `POST /environments/{id}/resolve` | → `ResolvedEnvironment` | **plaintext secrets**; for ExecutionService; audit-logged without values; policy `api` |

```jsonc
EnvironmentSummary { id, name, variableCount, updatedAt }
EnvironmentDto     { id, name, createdBy, createdAt, updatedAt, variables: [VariableDto] }
VariableDto        { key, value /*null when isSecret*/, isSecret, hasValue, enabled }
VariableWrite      { key(1..100, [A-Za-z0-9_.-]), value /*string|null*/, isSecret, enabled,
                     keepExistingSecret /*bool: for isSecret=true and value=null, keep stored ciphertext*/ }
ResolvedEnvironment { id, variables: [ { key, value, isSecret } ] }   // enabled variables only
```

Rules: keys unique case-sensitively (`conflict` otherwise); a variable cannot flip `isSecret`
true → false without supplying a new value; value ≤ 8192 chars. Encryption: AES-256-GCM, key from
`Secrets:EncryptionKey` (base64, 32 bytes), stored as `v1:<base64 nonce|tag|ciphertext>`,
AAD = `"{workspaceId}|{environmentId}|{key}"`. Missing/short key → secret writes answer
`503 secrets_unavailable`, `/health` reports `secrets: "unavailable"`, non-secret features work (P8).

## 4. ExecutionService (`/api/v1`)

| Method & path | Body → Response | Notes |
|---|---|---|
| `POST /execute` | `ExecuteRequest` → `200 ExecutionResult` | policy `execute`; always 200 for a handled outcome; 400 for an invalid request; 502 `upstream_unavailable` only if EnvironmentsService fails |
| `GET /history` | `?page&pageSize&q=` → `Page<HistorySummary>` | caller's own entries in the active workspace |
| `GET /history/{id}` | → `HistoryEntry` | |
| `DELETE /history/{id}` | → `204` | |
| `DELETE /history` | → `204` | clears the caller's entries |

```jsonc
ExecuteRequest   { request: RequestDefinition, environmentId?: Guid, savedRequestId?: Guid,
                   timeoutSeconds?: int /*1..60, default 30*/, followRedirects?: bool /*default true, max 5*/ }
ExecutionResult  { outcome: "completed|blocked|timeout|networkError|invalidRequest|tooLarge",
                   errorCode: string|null, message: string|null,
                   response: ResponseDto|null,
                   timing: { totalMs, dnsMs, connectMs, ttfbMs, downloadMs },   // ints, unknown = 0
                   finalUrl: string /*secrets masked*/, redirects: [ { statusCode, location } ],
                   unresolvedVariables: [string], historyId: Guid|null }
ResponseDto      { statusCode, reasonPhrase, httpVersion, headers: [ { name, value } ],
                   body: { encoding: "utf8|base64", content: string, sizeBytes: long, truncated: bool, contentType: string|null } }
HistorySummary   { id, executedAt, method, url /*template, masked*/, statusCode: int|null, outcome, totalMs, sizeBytes }
HistoryEntry     { id, executedAt, savedRequestId, environmentId, request: RequestDefinition /*template, masked*/, result: ExecutionResult /*body ≤ 64 KB*/ }
```

Sender limits (all configurable under `Execution:` with these defaults, and enforced):
request body ≤ 1 MB · ≤ 100 headers · URL ≤ 8192 · response read cap **5 MB** (`truncated=true`,
`outcome` stays `completed`; `tooLarge` is only for a declared `Content-Length` > 50 MB we refuse to start
reading) · timeout 30 s default / 60 s max (covers DNS+connect+headers+body) · redirects ≤ 5 ·
history retention 200 entries per (user, workspace), oldest removed on insert · history body ≤ 64 KB.
`blocked` ⇒ `errorCode` ∈ `ssrf_blocked_address|ssrf_blocked_scheme|ssrf_blocked_port|dns_failed|too_many_redirects|invalid_url`.
Message for `blocked` never reveals which internal address matched beyond the category.

**Masking (history, logs, UI):** header values for `Authorization`, `Proxy-Authorization`, `Cookie`,
`Set-Cookie`, `X-Api-Key`, `X-Auth-Token` and any header/query name containing `token|secret|password|apikey|api-key`
become `••••••`; every resolved secret-variable value found in url/headers/body is replaced with `••••••`;
auth-helper values are never stored.

## 5. Web ↔ backends

Web has one typed client per backend (`CollectionsClient`, `EnvironmentsClient`, `ExecutionClient`,
`AuthServiceClient`) on logical names `http://collections-service`, `http://environments-service`,
`http://execution-service`, `http://authservice` (service discovery). DTOs in `PostmanClone.Web/Api/`
are copies; a test in `PostmanClone.Web.Tests` round-trips the services' real types through them.
Bearer = server-side access token passed per request via `HttpRequestMessage.Options`
(never a scoped service in a `DelegatingHandler`).

Web routes (navigation order pinned by a test): `/` workspace home · `/environments` · `/history` ·
`/import` · `/workspace` · `/about`; plus `/sign-in`, `/register`, `/sign-out` (POST), `/no-workspace`,
`/accept-invitation`, `/legal/terms`, `/legal/privacy`, `GET /download/collections/{id}` (BFF export).

`data-testid` convention: `area-element[-qualifier]`, kebab-case (`sign-in-email`, `tree-collection-<id>`,
`builder-send`, `response-status`, `env-var-row-<index>`). Test ids listed in a WP survive restyling.

## 6. authservice (consumed, v0.3.4)

Login `POST /api/v1/auth/login {email,password}` → `200 TokenResponse {accessToken,refreshToken,expiresIn,tokenType}`
| `202 RegistrationPendingVerificationResponse` | `200 {requiresTwoFactor:true,challengeToken,...}` | `401`.
Register `POST /api/v1/auth/register {email,password,acceptedTermsVersion,acceptedPrivacyVersion,locale?}`;
versions from `GET /api/v1/auth/consents/versions`. Refresh `POST /api/v1/auth/refresh {refreshToken}` (rotating,
single-use). Logout `POST /api/v1/auth/logout` (bearer). Profile `GET /api/v1/auth/me` → `UserInfoResponse`
(`organizations: [{id,name,imageUrl,role}]`). Organizations: `POST /api/v1/organizations {name,description?}`,
`GET /api/v1/organizations/{id}` (members), `POST /api/v1/organizations/{id}/invite`,
`POST /api/v1/organizations/invitations/accept`. WP03 re-verifies each against the pinned tag and
records differences in `docs/AUTH.md`; a difference is an Amendment, not a silent change.

## Amendments

### A1 (WP23): Web route `/requests/{id}/curl` (Copy as cURL)

A new **Web page**, not an API route, so the API contract is unchanged. It shows the text of `GET /api/v1/requests/{id}/export/curl`
(§2) in a read-only block. A request that is absent or belongs to another workspace is the same "no longer exists" answer (§0).
Optional only: no existing route, field, limit or status changes. Recorded in [ADR-0021](../decisions/0021-import-export-in-the-web.md).

### A2 (WP29): Local agent: pairing, the agent-only routes, the local protocol, and run history from the agent

Proposed by WP29 and decided in [ADR-0026](../decisions/0026-local-agent-and-fly-estate.md). Implemented by WP30 (the agent,
EnvironmentsService and ExecutionService routes), WP31 (the Web) and WP33 (the deployment). Only new routes, new error codes
and optional fields. No existing route, field, limit or status changes. Where this amendment and §0 or §4 name the same
thing, the same rules apply (for example, the masking of §4).

**A2.1 Error codes (new, additive to §0).**

| Code | Status | Where |
|---|---|---|
| `pairing_code_invalid` | 400 | `POST /agent/pair` (EnvironmentsService), local `POST /v1/pair`. Unknown, used or expired: one answer, no hint which |
| `agent_token_invalid` | 401 | Agent-only routes: no token, a wrong token, a revoked or expired one, or a user's token. Local `POST /v1/send`: a missing or wrong `X-Agent-Pairing-Token` |
| `agent_not_paired` | 409 | Local `POST /v1/send` when no pairing exists or the agent token was revoked (the page says "pair again") |
| `origin_not_allowed` | 403 | Local listener: `Origin` absent or not in the allow-list |
| `host_not_allowed` | 421 | Local listener: `Host` is not the loopback name and port |
| `unsupported_media_type` | 415 | Local listener: `POST /v1/send` or `/v1/pair` with a body that is not `application/json` (added by WP30, ADR-0027) |

Every other error is as in §0. A user's token or an agent token on any other route answers the standard `401` with no code, as
an unknown bearer does.

**A2.2 EnvironmentsService: the user's agent routes (user token, policy `api`).**

| Method & path | Body → Response | Notes |
|---|---|---|
| `POST /agent/pairing-codes` | → `201 PairingCodeDto { code, expiresAt }` | `code` is shown once. Lives 5 minutes. A new code for the same user replaces an unused one. Workspace from the token |
| `GET /agent/installations` | → `Page<AgentInstallationDto>` | Only the caller's own agents in the active workspace |
| `DELETE /agent/installations/{id}` | → `204` | Revokes the agent. Another user's agent, another workspace's, or an absent one → `404 not_found`. Scoping test `Scoping_DELETE_agent_installations_id` |

```jsonc
PairingCodeDto          { code: "string(10), Crockford base32", expiresAt }
AgentInstallationDto    { id, name, platform, version, pairedAt, lastUsedAt /*null*/, expiresAt, revokedAt /*null*/ }
```

**A2.3 EnvironmentsService: pairing (no bearer; policy `auth`, per address).**

| Method & path | Body → Response | Notes |
|---|---|---|
| `POST /agent/pair` | `AgentPairRequest` → `201 AgentPairing` | The code is consumed atomically. The agent token is returned once. An unused or expired code → `400 pairing_code_invalid` |

```jsonc
AgentPairRequest  { code: "string(10..20)", agentName: "string(1..100)", platform: "string(1..40)", version: "string(1..40)" }
AgentPairing      { agentId, agentToken: "string(43) /*opaque, shown once*/", expiresAt, workspaceId,
                    allowedWebOrigins: ["https://…"] /*from the server's configuration Agent:AllowedWebOrigins*/ }
```

**A2.4 EnvironmentsService: agent-only routes (agent token; no other credential is accepted here).**

| Method & path | Body → Response | Notes |
|---|---|---|
| `GET /agent/me` | → `200 { agentId, workspaceId, expiresAt }` | The agent checks at start and after a `401` whether it is still paired |
| `POST /agent/environments/{id}/resolve` | → `200 ResolvedEnvironment` | Same shape and rules as §3 `resolve`: enabled variables only, **plaintext secrets**. Workspace from the agent token. Another workspace's or absent environment → `404 not_found`. Policy `execute`. Audit log without values. Scoping test `Scoping_POST_agent_environments_id_resolve` |

Agent-only routes accept an agent token and refuse every other credential with `401 agent_token_invalid`. Every other route refuses an agent
token as an unknown bearer. An architecture test in WP30 checks both rules by reading each route's authorization metadata.

**A2.5 ExecutionService: run history from the agent (user token, reached through the Web's BFF; policy `api`).**

| Method & path | Body → Response | Notes |
|---|---|---|
| `POST /history/records` | `HistoryRecordWrite` → `201 HistoryEntry` | Records a run that was sent by an agent. It does not resolve or send anything. Stored under the caller's id and active workspace, with the same retention as §4 (200 per user and workspace, oldest removed on insert) |

```jsonc
HistoryRecordWrite  { source: "agent" /*only value accepted here*/, request: RequestDefinition /*template, unresolved*/,
                      environmentId: Guid|null, savedRequestId: Guid|null,
                      result: ExecutionResult /*masked by the agent; historyId null; body is cut to 64 KB*/ }
```

The service applies the name-based masking of §4 to the stored headers, query and URL, and the 64 KB cut to the body. It cannot
apply value-based masking (it holds no secret values), so the agent masks values before the result leaves the machine (A2.6).
The service rejects a `source` other than `"agent"` with `400 validation_failed`, and a `request` that holds a secret literal with
`400 secret_literal_not_allowed` (§1).

**Optional field on the history shapes (§4).** `HistorySummary` and `HistoryEntry` gain `source: "server" | "agent"`. Rows written
before this amendment read as `"server"`. A reader that ignores the field keeps working.

**A2.6 Local agent protocol (`http://127.0.0.1:47631`).** The agent is a program on the user's computer. The browser calls it with
JavaScript that the Web serves (ADR-0028). Nothing else calls it.

- **Listener.** Bound to `127.0.0.1` only (`Agent:Port`, default `47631`). Not `0.0.0.0`, not `::`. The agent refuses to start when the port is in use.
- **Every request.** `Host` must be `127.0.0.1:<port>` or `localhost:<port>`, else `421 host_not_allowed`. `Origin` must equal one entry of
  the agent's `allowedWebOrigins` exactly (scheme, host and port), else `403 origin_not_allowed`. An absent `Origin` is refused.
- **CORS.** A response echoes the exact allowed origin in `Access-Control-Allow-Origin`, with `Vary: Origin`. Never `*`. Never
  `Access-Control-Allow-Credentials`.
- **Preflight `OPTIONS`.** `204`, with `Access-Control-Allow-Methods: GET, POST, OPTIONS`, `Access-Control-Allow-Headers: Content-Type, X-Agent-Pairing-Token`,
  `Access-Control-Max-Age: 600`, and `Access-Control-Allow-Private-Network: true` when the request carries
  `Access-Control-Request-Private-Network: true`. Refused origins get no allow headers.
- **`GET /v1/status`** → `200 { agent: "postmanclone-agent", protocol: 1, version, paired: boolean }`. No token. Nothing else is returned.
- **`POST /v1/pair`** `{ code }` (`application/json`) → `201 { pairingToken, agentId, expiresAt, workspaceId }`. The agent exchanges the code
  at `POST /agent/pair` (A2.3) and stores the agent token and the hash of `pairingToken`. `pairingToken` is 32 random bytes, base64url. A
  new pairing replaces the previous one. Errors: `400 pairing_code_invalid`, `502 upstream_unavailable` when EnvironmentsService is unreachable.
- **`POST /v1/send`** headers `Content-Type: application/json` and `X-Agent-Pairing-Token: <pairingToken>`. A missing or wrong token → `401 agent_token_invalid`.
  No pairing, or a revoked agent token → `409 agent_not_paired`. A body over 2 MB → `413 payload_too_large`. Other `Content-Type` → `415`.
  Body:

  ```jsonc
  AgentSendRequest  { request: RequestDefinition, environmentId: Guid|null, timeoutSeconds: int /*1..60, default 30*/,
                      followRedirects: bool /*default true, max 5*/ }      // ExecuteRequest without savedRequestId
  ```

  Success → `200 ExecutionResult` (§4), identical in shape and rules, with `historyId: null`. When `environmentId` is set, the agent resolves it
  (A2.4). A failed resolve is an `outcome: "invalidRequest"` with `errorCode: "environment_unavailable"` (a new `errorCode` value, additive); the
  message does not say whether the environment exists.
- **Sender.** The same assembler, executor, masking and caps as §4 (body ≤ 1 MB, ≤ 100 headers, URL ≤ 8192, read cap 5 MB, timeout, redirects ≤ 5,
  no ambient proxy, no cookies). The SSRF guard is not applied unless `Agent:Targets:AllowPrivate` is `false`. Always refused, whatever that flag says,
  with `outcome: "blocked"`: a scheme other than `http` or `https` (`errorCode` `ssrf_blocked_scheme`); user info in the URL (`invalid_url`);
  a target whose host and port are the agent's own listener (`ssrf_blocked_port`).
- **Logging.** Method, host, status, sizes and durations, as §4. Never a header value, body or variable value.

**A2.7 Web routes and BFF (not API routes; the API contract is unchanged, as A1).**

- `/agent`: pairing (creates a code through A2.2 and shows it for five minutes), the installation list with revoke (A2.2), and the install
  instructions. Form posts with antiforgery (WP31).
- `POST /agent/runs` (BFF, cookie session, antiforgery, the origin guard of ADR-0022): takes a `HistoryRecordWrite` from the page and calls
  `POST /api/v1/history/records` (A2.5) with the session's bearer. Anonymous → `401`. It does not forward the body unchecked: the Web applies the
  same name-based masking first.
- `connect-src` in the Web's Content-Security-Policy gains exactly `http://127.0.0.1:47631` and `http://localhost:47631` (the configured port),
  and no other origin (WP31).
