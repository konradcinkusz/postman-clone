# Security review (WP28)

- **Scope:** the whole tree at `main` (`52eb745`) plus the WP28 changes on `wp/28-final-review`.
- **Method:** [SECURITY-REVIEW](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/SECURITY-REVIEW.md)
  as the criteria: the finding format (location, issue, risk, attack scenario, impact, recommendation), the readiness
  ledger (`OPEN` / `FIXED` / `NOT APPLICABLE`, with a priority window), and the recurring launch blockers (§9). Every
  focus area in the WP28 brief was read in code and matched to the test that shows it. Tests were run, not only read.
- **Date:** 2026-10-31. **Decision record:** [ADR-0019](decisions/0019-final-review-outcomes.md).
- **Companion:** [ARCHITECTURE_REVIEW.md](ARCHITECTURE_REVIEW.md) (principles and checklist).
- **Ranking:** *Blocking* means Critical/High, or a finding that must be closed before any deployment. *Should-fix*
  is Medium. *Consider* is Low or informational. Priorities follow the guide: P1 immediately, P2 within a week,
  P3 within two weeks, P4 long-term.

## 1. Summary

| Ranking | Found | Fixed in this PR | Owner decision or action | Documented or accepted | Open |
|---|---|---|---|---|---|
| Blocking (code) | 3 | 3 | 0 | 0 | 0 |
| Blocking (before deployment) | 4 | 0 | 4 | 0 | 0 |
| Should-fix | 4 | 4 | 0 | 0 | 0 |
| Consider | 11 | 2 | 3 | 4 | 2 |

Of the 11 "consider" rows: SR-08 and SR-09 are fixed; SR-14, SR-18 and SR-19 are owner decisions; SR-15, SR-20,
SR-21 and SR-22 are documented or accepted residuals; SR-16 and SR-17 are open, low, and named in OPEN-ITEMS.md.

**Zero open blocking findings in the code.** Four blocking findings are deployment preconditions that sit in
configuration this repository does not own (authservice, the TLS edge) or are policy choices. They are listed in §1.1
with their owners, and they are not a reason to deploy. Deployment is out of scope for this build (ADR-0007).

### 1.1 Blocking before deployment (owner)

| ID | Pri | Status | Issue | Owner action |
|---|---|---|---|---|
| SR-10 | P2 | OWNER ACTION | Every request to authservice comes from the Web's address, so all users share one sign-in window per edge address (OI-26). A burst from one user can refuse the others for up to a minute. | Set authservice's `ClientIpHeader` and trusted proxies, and the Web's `Proxy:TrustForwardedFor` and `ForwardedHeaders:KnownProxies`, for the edge. Decide whether one shared window is acceptable. |
| SR-11 | P2 | OWNER DECISION | authservice requires a confirmed email only when it can send mail (`src/AuthService/Program.cs`, `RequireConfirmedEmail = authOptions.RequireConfirmedEmail ?? canSendEmail`). With no sender, accounts are active unverified: recurring blocker §9.2. | Configure the email sender, or set `RequireConfirmedEmail` on and accept that sign-up cannot finish without it. |
| SR-12 | P2 | OWNER ACTION | No `Strict-Transport-Security` (OI-20). Right for the plain-HTTP local stack; wrong for a deployment. | Set HSTS at the TLS edge. |
| SR-13 | P2 | OWNER ACTION | `/legal/privacy` and `/legal/terms` are placeholders (OI-04). Users agree to a terms version that has no text. | Supply the text. |

SR-14 (erasure policy, OI-03) is an owner decision that the guide ranks before deployment; it is listed in §2.3 with the others.

## 2. Findings

Each finding below gives the location, the risk, the attack scenario and the impact. Where a finding is fixed, the test
that shows the fix is named.

### 2.1 Blocking (fixed in this PR)

**SR-01 — Sign-in, register and accept-invitation forms had no rate limit (High, OI-02, P2 → fixed).**
- *Location:* `src/PostmanClone.Web/Auth/AuthEndpoints.cs:20-21`, `src/PostmanClone.Web/Membership/MembershipEndpoints.cs:18`.
- *Issue:* the kernel's `auth` policy existed (`ServiceDefaults/RateLimiting/RateLimitingExtensions.cs`) but no endpoint used it.
- *Attack scenario:* an attacker posts `/sign-in` with a list of passwords for one account, at the rate the network allows. Nothing
  in the Web slows the attempts down. The only control is authservice's per-address window, and the attacker may use many addresses.
- *Impact:* a weak password is found. authservice's lockout (five failures, five minutes, `Program.cs:163-164`) limits this per account,
  but it does not stop a spread of guesses across accounts.
- *Fix:* the Web registers the kernel limiter and applies `RequireRateLimiting(RateLimitPolicies.Auth)` to sign-in, register and
  accept-invitation, and `Api` to invitations and workspace creation. The `auth` window is configurable (`RateLimiting:AuthPermitsPerMinute`,
  default 10). Test: `tests/PostmanClone.Web.Tests/Auth/AuthFormRateLimitTests.cs` (the fourth post in a window of three returns 429 with
  the kernel's `rate_limited` body and `Retry-After`; register shares the window).
- *Status:* FIXED (commit `2908fc3`; the invitation and workspace-creation limits in `6ad2d70`).

**SR-02 — The sender's target URLs were traced (Medium, before deployment, P2 → fixed).**
- *Location:* `src/PostmanClone.ServiceDefaults/Telemetry/OpenTelemetryExtensions.cs` (the HTTP client instrumentation),
  `src/PostmanClone.ExecutionService/Sending/Execution/WireRequest.cs` (the marker).
- *Issue:* `AddHttpClientInstrumentation()` records the URL of every outbound request. For the sender, the URL is the user's target, and a
  variable can put a secret in a path segment. (Whether the library redacts the query by default was not checked; the path is not redacted.)
- *Attack scenario:* a user writes `https://api.example/v1/{{token}}`. With `OTEL_EXPORTER_OTLP_ENDPOINT` set, the path with the secret
  leaves the service in a span, to whoever runs the collector.
- *Impact:* a secret leaves the trust boundary through telemetry (CONTRACTS §4, ADR-0006).
- *Fix:* the sender marks its requests (`OutboundTelemetry.UserTarget`), and the tracing filter drops marked requests. Other clients keep their
  spans. Tests: `Outbound_requests_marked_as_user_targets_get_no_client_span` and `A_user_target_request_is_not_recorded_and_any_other_request_is_recorded`
  (`tests/PostmanClone.ServiceDefaults.Tests/Telemetry/TelemetryTests.cs`). Removing the filter makes the first test fail (checked).
- *Status:* FIXED (commit `2908fc3`). Recorded as a deviation from P15 in DEVIATIONS.md. The first test's wait was fixed too (OI-06).

**SR-03 — The SSRF guard could be relaxed in any environment except one named `Production` (High under that configuration, P2 → fixed).**
- *Location:* `src/PostmanClone.ExecutionService/Options/ExecutionOptionsValidator.cs:22` (was `environment.IsProduction()`).
- *Issue:* `AllowLoopback` and `AllowPrivateRanges` were refused only in an environment named Production. A deployment with the name
  Staging, QA or any other name could set them.
- *Attack scenario:* a user sends `http://169.254.169.254/latest/meta-data/` (the cloud metadata address, link-local, relaxed by
  `AllowPrivateRanges`) through a Staging ExecutionService. The response, with the instance's credentials, comes back in the result.
- *Impact:* cloud credentials and the private network are reachable from the sender.
- *Fix:* the relaxations are accepted only in Development; any other name is refused at startup (`ValidateOnStart`). Tests:
  `Development_accepts_the_loopback_relaxation`, `Any_environment_other_than_Development_refuses_the_loopback_relaxation`, the
  existing `Production_refuses_each_SSRF_relaxation…` (message updated). The local loopback stack runs the sender as Development.
- *Status:* FIXED (commit `aa29143`). ADR-0011 addendum in ADR-0019.

### 2.2 Should-fix (Medium, fixed in this PR)

**SR-04 — A text body cut inside a multi-byte character was sent unmasked (Medium).**
- *Location:* `src/PostmanClone.ExecutionService/Sending/Execution/ResponseBody.cs` (`Describe`, `TryDecodeUtf8`).
- *Issue:* the body is read to a cap (5 MB). A cut inside a multi-byte character makes strict UTF-8 decoding fail, and the body is then
  sent as base64. Base64 is not searched for secrets (the masker works on text), so the whole body is unmasked.
- *Attack scenario:* a target answers 6 MB of UTF-8 text with a secret near the start. The cut happens inside a `é`. The response shows the
  whole body as base64, which contains the secret in a form the masker does not look for. The base64 is decodable.
- *Impact:* a masking bypass for a large text response.
- *Fix:* when the body is truncated, an incomplete tail of at most three bytes is set aside before strict decoding. Test:
  `A_body_cut_inside_a_multi_byte_character_is_still_text_and_still_masked`; `A_truncated_binary_body_is_still_base64` keeps binary as base64.
- *Status:* FIXED (commit `df4b7b9`).

**SR-05 — A secret cut by the read cap showed its beginning (Medium, OI-12, P4 → fixed).**
- *Location:* `src/PostmanClone.ExecutionService/Sending/Assembly/SecretMasker.cs` (`TrimPartialSecretAtEnd`), `ResponseBody.cs`.
- *Issue:* when a secret straddles the cap, the part before the cut is in the captured body and the rest is not, so it is not masked.
- *Attack scenario:* a target echoes the user's key in a 5 MB response; the key straddles byte 5 MB. The first characters of the key show.
- *Impact:* a partial secret, which narrows a brute-force search.
- *Fix:* when the body is truncated, the longest tail that is a proper prefix of a secret form is removed before masking. Test:
  `A_body_cut_inside_a_secret_shows_none_of_the_secret`. Removing the trim call makes it fail (checked).
- *Status:* FIXED (commit `df4b7b9`). OI-12 closed.

**SR-06 — Invitations and workspace creation had no rate limit (Medium, recurring blocker §9.3 → fixed).**
- *Location:* `src/PostmanClone.Web/Membership/MembershipEndpoints.cs:16`, `src/PostmanClone.Web/Auth/AuthEndpoints.cs:22`.
- *Issue:* a signed-in Owner or Admin can post invitations without a limit; authservice's own limit is per address.
- *Attack scenario:* a compromised Admin session invites thousands of addresses with a role of its choice, quickly.
- *Impact:* spam from the product's address, and a large number of pending invitations. Authority is not gained: authservice checks the role.
- *Fix:* the per-user `Api` policy. Test: the same Web test host (`AuthFormRateLimitTests`); the full Web suite passes (704 tests).
- *Status:* FIXED (commit `6ad2d70`). Partial coverage closed (§9.3 of the guide).

**SR-07 — A Web with missing or truncated static assets answered empty (Medium, OI-27, P3 → fixed as far as the symptom goes).**
- *Location:* `src/PostmanClone.Web/Hosting/StaticAssetManifestCheck.cs`, `WebHostExtensions.UseWebPipeline`.
- *Issue:* a file listed in the publish manifest was served as a 200 with `Content-Length: 0`, so the browser never started the circuit.
- *Attack scenario:* not an attacker's; an operational failure. A partial deploy leaves the sign-in page broken with no error.
- *Impact:* silent failure. Nothing leaks.
- *Fix:* the Web checks the publish manifest against the files on disk (presence and declared length) before it serves anything, and stops
  with the names of the files at fault. Tests: `StaticAssetManifestCheckTests` (six cases). End to end: a publish folder with a truncated
  `app.css` refused to start and named the file. **Not reproduced:** the empty response itself. Four configurations served the full files
  (Debug and publish; the repository root and the publish folder; Development and Production; with and without gzip).
- *Status:* FIXED (commit `08f584a`). The mechanism of the empty response is unconfirmed; the check covers the symptom.

### 2.3 Consider (Low, or informational)

**SR-08 — Teredo and the 6to4 relay anycast were not blocked (Low).** `Rule("2001::", 32, "teredo")` and `Rule("192.88.99.0", 24, "reserved")`
(`src/PostmanClone.ExecutionService/Sending/Ssrf/IpRangePolicy.cs`). A Teredo address reaches the Teredo tunnel's IPv4 host; the 6to4 relay
(RFC 7526, deprecated) is a public anycast address. Neither is needed for a request to a public host. Tests: boundary rows in
`IpRangePolicyTests.cs`. *Status:* FIXED (commit `df4b7b9`).

**SR-09 — The auth budget read the system clock (Low, test code).** `tests/PostmanClone.Web.E2E/Support/AuthBudget.cs` now takes a
`TimeProvider`; the window and the wait have tests (`AuthBudgetWindowTests`, which run without the stack). Supports OI-26. *Status:* FIXED (commit `aa29143`).

**SR-14 — Personal data is not removed when an account is deleted (Medium, OI-03; owner decision before deployment).** authservice soft-deletes and removes later
(`UserCleanupService`, v0.3.4). This system keeps `CreatedBy`, `UserId`, history rows and invitation records. The retention rule is an
owner decision (PRIVACY.md §5). *Status:* OWNER DECISION. Not decided here.

**SR-15 — Environments and collections have no version; the last write wins (Low).** A second writer overwrites the first without a warning.
CONTRACTS does not define a version for these resources and this review does not invent one (the plan forbids contract changes outside an
amendment). The builder does version saved requests (ADR-0017). *Status:* DOCUMENTED (OPERATIONS.md). Not a security boundary.

**SR-16 — The environment limit can be exceeded by one under a race (Low, OI-09).** The count is checked, then inserted, with no database constraint.
Two concurrent creates at 49 can both succeed. No data is exposed. *Status:* OPEN, consider a workspace lock as in CollectionsService.

**SR-17 — The OTLP exporter was never run against a collector (Low, OI-18).** The exporter is registered only when
`OTEL_EXPORTER_OTLP_ENDPOINT` is set, so the default build sends nothing. Its failure mode when set is untested. *Status:* OPEN.

**SR-18 — The CI secret scan uses `v8.19.0` (Low, owner decision).** The local hook needs `v8.28.0` (verified here, OI-05). CI uses
the Docker image `ghcr.io/gitleaks/gitleaks:v8.19.0` with `detect`, which is not the hook's form and was not run here (no Docker). Changing
the image needs a digest. *Status:* OWNER DECISION (see OPEN-ITEMS.md §0). The hook itself is unchanged and fail-closed.

**SR-19 — A Member may edit a collection another Member created (Low, product rule).** `CollectionsService/Services/Caller.cs:23`:
edit is open to every member, delete is limited to the creator, an Admin or an Owner. This matches the README roles table. *Status:*
DOCUMENTED. The owner confirms the rule.

**SR-20 — Sessions and open tabs are in one process's memory (Low, accepted, OI-25, ADR-0008).** A restart signs everyone out. *Status:* ACCEPTED RESIDUAL.

**SR-21 — Masking covers the known secret values, not personal data in a response (Low, PRIVACY.md §2.4).** A response that echoes a user's
email is shown as sent. *Status:* ACCEPTED RESIDUAL.

**SR-22 — The origin guard compares the request's host, which is forwarded only from known proxies (Informational).** The Web accepts
`X-Forwarded-Host` only from `ForwardedHeaders:KnownProxies` (loopback by default, `WebHostExtensions.cs`). A proxy that is not listed
is ignored, so the guard compares the proxy's own host and refuses cross-site posts; a listed proxy must set the header itself. The
edge configuration is in SR-10. *Status:* DOCUMENTED.

## 3. Focus areas (WP28 brief)

### 3.1 The SSRF guard

| Property | Where | Evidence (test) | Result |
|---|---|---|---|
| Private, link-local, loopback, metadata, CGNAT, multicast, reserved, documentation, benchmarking, broadcast ranges are blocked (IPv4 and IPv6) | `Sending/Ssrf/IpRangePolicy.cs` | `IpRangePolicyTests` (edges of every range) | Met |
| IPv4-mapped, compatible, NAT64 and 6to4 addresses are judged by their embedded IPv4 | `IpRangePolicy.cs` (`EvaluateIpv6`) | `IPv6_blocks_and_embedded_IPv4_…` | Met |
| Encoded IPv4 forms (`0x7f.1`, `2130706433`, octal) are read as the address they name | `Sending/Ssrf/IpLiteralParser.cs` | `IpLiteralParserTests` | Met |
| Zone-scoped IPv6 and userinfo (`http://allowed@127.0.0.1/`) are refused | `IpRangePolicy.cs:69`, `TargetValidator.cs:67` | `An_IPv6_address_with_a_zone_id…`, `TargetValidatorTests` | Met |
| DNS rebinding: each name resolved once; every answer checked; the connection dials a checked address, not a name | `Sending/Ssrf/GuardedConnector.cs:18-44`, `SsrfHandlerFactory.cs:24` (`ConnectCallback`) | `A_public_answer_is_dialled_once_at_its_validated_address`, `A_mixed_answer_with_any_blocked_address_is_refused_and_nothing_is_dialled`, `A_resolver_that_answers_differently_on_the_second_call_is_not_trusted_for_the_first_connection` | Met |
| Redirects: every hop goes through the same guarded handler; a hop to a blocked address is refused | `RequestExecutor.cs:194-223`; `AllowAutoRedirect = false` (`SsrfHandlerFactory.cs:22`) | `A_redirect_to_loopback_is_blocked_when_the_flag_is_off`, `A_302_then_200_is_followed_and_the_chain_is_reported`, `A_redirect_loop_stops_at_the_limit…` | Met |
| Relaxations refused outside Development | `ExecutionOptionsValidator.cs:22` | see SR-03 | Met (fixed) |
| Size cap: 5 MB read; a gzip bomb stops at the cap on decoded bytes | `ResponseBody.cs` | `A_gzip_bomb_is_stopped_at_the_cap_on_the_decoded_bytes`, `An_endless_body_is_read_only_to_the_cap_plus_one_chunk` | Met |
| Time cap: a request ends at its timeout (at most 60 s) | `RequestExecutor.cs:51,88` | `A_target_slower_than_the_timeout_gives_outcome_timeout_at_the_deadline`, `A_timeout_outside_the_contract_range…` | Met |
| Blocked ports before any resolution | `GuardedConnector.cs:20` | `A_blocked_port_is_refused_before_any_resolution` | Met |
| Only http and https | `TargetValidator.cs` | `A_scheme_other_than_http_or_https_is_refused` | Met |
| No proxy, no cookies, no decompression by the handler | `SsrfHandlerFactory.cs:19-23` | `SsrfLoopbackListenerTests` (the handler's flags) | Met |

The guard does what ADR-0011 says, with one exception the guard cannot see: a deployment that is not Development but uses
an alias for Production (SR-03, fixed). Retries are off for the sender by design (ADR-0014, ADR-0019 §8).

### 3.2 Secret handling: create → store → resolve → send → mask → history → logs

| Stage | Control | Evidence |
|---|---|---|
| Create | A secret's value is accepted once; the stored form is AES-256-GCM ciphertext bound to workspace, environment and key (`EnvironmentsService/Secrets/AesGcmSecretProtector.cs`) | `AesGcmSecretProtectorTests`, `A_new_secret_is_encrypted_and_bound_to_its_workspace_environment_and_key` |
| Store | Rows hold ciphertext only; no plaintext column | `Database_rows_hold_ciphertext_only_for_a_secret_in_postgresql`, `A_resolved_secret_is_absent_from_every_column_of_every_row_in_PostgreSQL` |
| Read | `GET` returns no secret value (the value is write-only; the page has no value attribute, ADR-0025) | `Response_sweep_no_endpoint_returns_the_plaintext_after_a_secret_is_created` |
| Resolve | Only `POST …/resolve` returns plaintext, to ExecutionService with a bearer (ADR-0006) | `Resolve_returns_the_enabled_variables_in_key_order_with_each_secret_in_plaintext`, `Scoping_POST_environments_id_resolve` |
| Send | The secret goes to the target; it is absent from the final URL, the result and the logs | `A_secret_is_sent_to_the_target_but_is_absent_from_finalUrl_the_result_and_the_logs` |
| Mask | Secret forms (raw, percent-encoded, JSON-escaped) are masked in text, URLs and headers; sensitive names (`Authorization`, `Cookie`, names with `token`, `secret`, `password`, `apikey`) are masked | `SecretMasker`, `Sensitive_header_and_query_values_are_masked_in_the_stored_template_and_url`, `The_stored_template_masks_sensitive_form_fields_and_drops_every_auth_value` |
| Body cut at the cap | Partial secrets at the cut are removed before masking (SR-05) | `A_body_cut_inside_a_secret…` |
| History | The stored template keeps references, not values; the result is masked; the history is per user and workspace | `HistoryMaskingTests`, `HistoryScopingTests`, `HistoryRetentionTests` |
| Logs | Method, masked host, status, sizes and durations only; no body, header value or variable value | `A_secret_is_sent…` (logs), `The_key_is_never_written_to_the_logs_or_the_health_payload`, `CapturingLogs` tests |
| Telemetry | The sender's targets are not traced (SR-02) | `TelemetryTests` (outbound) |
| Literal secret in an auth helper | Refused, and nothing is sent | `A_secret_literal_in_the_auth_helper_is_refused_and_nothing_is_sent` |
| Encrypted at rest | AES-256-GCM; the key is an environment setting; `/health` reports only whether the key works | `Health_reports_secrets_available…`, `Secret_operations_answer_secrets_unavailable_when_no_key_is_configured` |

Known gap, recorded: masking covers the known secret values. A response that carries a value the user did not
mark as secret is shown as sent (SR-21).

### 3.3 The import threat model (Postman v2.1 and cURL)

| Case | Control | Where | Test evidence |
|---|---|---|---|
| Oversized file | 5 MB file, 64 KB multipart envelope; refused on declared length and while reading | `PostmanImportLimits.cs:10-13`, `Api/PostmanUpload.cs:36,47,83` | `tests/PostmanClone.CollectionsService.Tests/Interop/Postman/` |
| Deep JSON | depth 64 | `PostmanImportLimits.cs:16`, `PostmanJson.cs` | same |
| Deep folders | nesting 20 | `PostmanImportLimits.cs:19` | same |
| Many items | 5,500 items; 500 warnings | `PostmanImportLimits.cs:22,25` | same |
| Long text | description 500 characters | `PostmanImportLimits.cs:28` | same |
| Literal secrets | replaced by a variable and not stored; a warning names the key, not the value | `Interop/Postman/SecretRules.cs`, `CurlParser.cs` (`secret_not_imported`) | same, `Interop/Curl/` |
| Code execution | none: no process is started by the importers (`grep`: no `Process`/`Shell` use in `Interop/`) | `Interop/Curl/CurlTokenizer.cs` is a tokenizer; no shell | `Interop/Curl/` tests |
| Network | none: an import does not send a request | Importers are pure | same |
| Atomicity | one transaction | `Data/CollectionsTransactions.cs` | `Scoping_POST_import_curl`, import tests |
| File names on download | `SafeFileName` (`Path.GetFileName` and limits) | `Web/Collections/ImportRules.cs:70`, `CollectionDownloadEndpoints.cs:101-104` | `CollectionDownloadTests` |
| Tenant | import writes only to the caller's workspace | `Scoping_POST_import_curl` | scoping |

### 3.4 Tenant isolation

Every id route has a scoping test named `Scoping_<METHOD>_<route>` (the names in §3.4). The workspace comes from the token,
never from a route, query or body (`WorkspaceClaims.cs`). The architecture rules (R1–R10, `tests/PostmanClone.ServiceDefaults.Tests/Architecture/`)
check that every `{id}` route has its scoping test.

| Service | Routes with `{id}` | Scoping tests |
|---|---|---|
| CollectionsService | `GET/PUT/DELETE /collections/{id}`, `POST /collections/{id}/folders`, `POST /collections/{id}/requests`, `GET /collections/{id}/export/postman`, `GET/PUT/DELETE /folders/{id}`, `POST /folders/{id}/move`, `GET/PUT/DELETE /requests/{id}`, `POST /requests/{id}/move`, `POST /requests/{id}/duplicate`, `GET /requests/{id}/export/curl` | `Scoping_GET_collections_id`, `Scoping_PUT_collections_id`, `Scoping_DELETE_collections_id`, `Scoping_POST_collections_id_folders`, `Scoping_POST_collections_id_requests`, `Scoping_GET_collections_id_export_postman`, `Scoping_DELETE_folders_id`, `Scoping_PUT_folders_id`, `Scoping_POST_folders_id_move`, `Scoping_GET_requests_id`, `Scoping_PUT_requests_id`, `Scoping_DELETE_requests_id`, `Scoping_POST_requests_id_move`, `Scoping_POST_requests_id_duplicate`, `Scoping_GET_requests_id_export_curl` |
| EnvironmentsService | `GET/PUT/DELETE /environments/{id}`, `POST /environments/{id}/resolve` | `Scoping_GET_environments_id`, `Scoping_PUT_environments_id`, `Scoping_DELETE_environments_id`, `Scoping_POST_environments_id_resolve` |
| ExecutionService | `GET /history/{id}`, `DELETE /history/{id}`; `POST /execute` (the environment id is checked through EnvironmentsService) | `Scoping_GET_history_id`, `Scoping_DELETE_history_id`, `Scoping_POST_execute` |
| Web (BFF) | `GET /download/collections/{id}`, `POST /workspace/invitations` (the workspace from the session) | `Scoping_GET_download_collections_id`, `Scoping_POST_workspace_invitations_takes_the_workspace_from_the_token_not_the_form` |

Every foreign row answers `404` (never `403`), as AGENTS.md requires; the scoping tests in the three services assert `NotFound`
(eight files reference it).

### 3.5 The endpoint × role matrix

Roles: `Owner`, `Admin`, `Member` (`ServiceDefaults/Authentication/WorkspaceClaims.cs:50-56`). `anon` is an unauthenticated request.

| Endpoint | anon | Member | Admin | Owner | Rate limit | Source |
|---|---|---|---|---|---|---|
| `GET /sign-in`, `POST /sign-in`, `POST /register` | yes | yes | yes | yes | `auth` (per address) | `Web/Auth/AuthEndpoints.cs` |
| `POST /sign-out` | yes | yes | yes | yes | none (no credential to guess) | `Web/Auth/AuthEndpoints.cs` |
| `POST /no-workspace/create` | no | yes | yes | yes | `api` (per user) | `Web/Auth/AuthEndpoints.cs` |
| `POST /workspace/invitations` | no | no (Web pre-check), authservice refuses | yes | yes | `api` (per user) | `Web/Membership/InvitationRules.cs:26`, authservice |
| `POST /accept-invitation` | no | yes | yes | yes | `auth` | `Web/Membership/MembershipEndpoints.cs:18` |
| Collections: list, read, export | no | yes | yes | yes | `api` | `CollectionsApi.cs:16-18` |
| Collections: create, edit | no | yes | yes | yes | `api` | same |
| Collections and requests: delete | no | creator only | yes | yes | `api` | `Caller.cs:23` |
| Import (`/import/postman`, `/import/curl`) | no | yes | yes | yes | `api` | `PostmanIoEndpoints.cs` |
| Environments: list, read | no | yes | yes | yes | `api` | `EnvironmentEndpoints.cs:32-35` |
| Environments: create, edit, variables | no | yes | yes | yes | `api` | same |
| Environments: delete | no | creator only | yes | yes | `api` | `EnvironmentAccess.cs:9` |
| Environments: resolve (plaintext) | no | yes (ExecutionService's token) | yes | yes | `api` | `EnvironmentEndpoints.cs` |
| `POST /execute` | no | yes | yes | yes | `execute` (60 per minute per user) | `ExecutionEndpoints.cs:24-28` |
| History: list, read, delete, clear | no | own entries only | own entries only | own entries only | `api` | `HistoryEndpoints.cs:26-29` |

Authority: the Web's role checks are a convenience. authservice and the services enforce the same rules, and an
unauthorised call through the Web is refused by the service it reaches (the Web forwards the user's bearer).

### 3.6 The origin guard, the Referer rule and Referrer-Policy (WP18b, ADR-0022)

`ServiceDefaults/Security/OriginGuardMiddleware.cs`. A request whose `Origin` is neither this host nor configured is refused.
`Origin: null` is refused. An unsafe method (POST, PUT, DELETE, PATCH) whose `Referer` is foreign, or unreadable, is refused.
A cross-site navigation other than a top-level GET is refused. A request with no `Origin` (curl, the services' own calls, probes) is allowed.
The mitigation is for browsers; it is not authentication. `Referrer-Policy: same-origin` (`SecurityExtensions.cs:16`) sends the
real `Origin` on a same-origin form post, which the guard needs; `no-referrer` had sent `Origin: null` and refused sign-in (OI-01,
verified in a browser in WP18b). Tests: `tests/PostmanClone.ServiceDefaults.Tests/Security/OriginGuardTests.cs`, `tests/PostmanClone.Web.Tests/Host/OriginGuardHostTests.cs`.

### 3.7 Security headers and CSP

| Header | Value | Where | Met |
|---|---|---|---|
| `X-Content-Type-Options` | `nosniff` | `SecurityExtensions.cs:12` | yes, every service |
| `X-Frame-Options` | `DENY` | `SecurityExtensions.cs:17` | yes |
| `Referrer-Policy` | `same-origin` | `SecurityExtensions.cs:16` | yes |
| `Content-Security-Policy` (Web) | `default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'` | `Web/Hosting/ContentSecurityPolicyMiddleware.cs:13-14` | yes; no `unsafe-inline`, no `unsafe-eval` |
| `Strict-Transport-Security` | not set | none | no (SR-12, owner) |

The CSP has no `unsafe-inline`. The Web has no inline script or style (`grep`), and the browser journeys run under it.

### 3.8 The session and tokens in the BFF

- The session cookie holds 32 random bytes, `HttpOnly`, `SameSite=Strict` (`Web/Auth/SessionCookie.cs:26-28`), and is `Secure` with the
  `__Host-` prefix outside Development (`SessionCookie.cs:7-16`).
- Tokens are held in `ServerSessionStore` on the server (ADR-0005, ADR-0013). Nothing token-shaped is in the page, storage or a URL:
  no `localStorage`, `sessionStorage` or JS interop in `src/PostmanClone.Web` (`grep`).
- Eight idle hours, seven absolute days, five-minute revalidation (ADR-0013).
- Logging: `SessionAuthenticationHandler`, `RevalidatingAuthenticationStateProvider` and the download endpoint log events, not values.

### 3.9 The kernel's retry rule (ADR-0023)

`ServiceDefaults/Extensions.cs:52`: `options.Retry.DisableForUnsafeHttpMethods()` for every `HttpClient`. A POST is never retried by
a library, so a write is not sent twice. The sender has no resilience handler at all and is recorded (ADR-0019 §8).

### 3.10 Untrusted response rendering

`Web/Components/Response/ResponsePanel.razor`: every response value is written as encoded text; there is no `MarkupString`, no
`iframe`, no script, and a body is never rendered as HTML (its header comment). Preview is disabled. `JsonPretty` is an iterative
scanner (no recursion, so no stack exhaustion on a deep body). Tests: `tests/PostmanClone.Web.Tests/Response/ResponsePanelTests.cs`, `…/Response/JsonPrettyTests.cs`.

### 3.11 Account forms and the authservice contract (drift check)

- authservice's password rule (`src/AuthService/Program.cs:157-161`): at least 8 characters, with a digit, a lowercase letter, an
  uppercase letter and a non-alphanumeric character. The register page now states it (commit `aa29143`).
- authservice's lockout (`Program.cs:163-164`): five failed sign-ins lock an account for five minutes. Not duplicated in the Web.
- authservice's `auth` policy (20 per minute per address, queue 5) is the one the browser journeys budget against (`AuthBudget`).
- Email confirmation follows whether an email sender is configured, in authservice v0.3.4 `src/AuthService/Program.cs` (SR-11).
- The endpoints and claims in `docs/AUTH.md` were checked against authservice v0.3.4 by `scripts/smoke-auth.sh` (run against the
  local stack, not in this session: see the verification list in ARCHITECTURE_REVIEW.md §4).

## 4. Recurring launch blockers (guide §9)

| # | Blocker | Status here |
|---|---|---|
| 1 | Production CORS origins never configured | **Not applicable by design.** The services have no CORS; only the Web calls them, server to server. No browser calls a service directly. `grep AddCors` finds nothing. |
| 2 | Email confirmation disabled for simplicity | **Open, owner (SR-11).** It follows the email sender, which is not configured in this build. |
| 3 | Rate limiting partial | **Met in code (SR-01, SR-06).** Every API group and every account form applies a policy (`grep RequireRateLimiting`). The edge window is an owner item (SR-10). |
| 4 | Token storage inconsistent | **Met.** One design: an HttpOnly cookie for the browser and the tokens on the server. No browser storage. |

## 5. Dependency audit

`dotnet list PostmanClone.sln package --vulnerable --include-transitive` (2026-10-31, restore against NuGet): every project reports
"no vulnerable packages given the current sources". The build fails on a vulnerable package (`NuGetAudit`, warnings are errors).

## 6. Residual risks accepted

- In-memory sessions and open tabs; one instance (ADR-0008, OI-25).
- Masking covers known secret values (SR-21).
- Non-secret environment values are stored in clear (ADR-0006).
- The sender sends a user's secret to the host the user names. That is the product (ADR-0011).
- The CI scan image pin (SR-18) until the owner bumps it.

## 7. Owner decisions and actions

See [OPEN-ITEMS.md §0](architecture/OPEN-ITEMS.md#0-owner-decisions-and-actions) for the list with owners. The
ones with a security effect are: SR-10 (edge client-IP trust), SR-11 (email verification), SR-12 (HSTS), SR-13 (legal text),
SR-14 (erasure policy), SR-18 (CI scan image), SR-19 (collection editing rule).

## 8. What was not verified

- The AppHost has never been run; no Docker image was built (OI-19). The Dockerfiles were not run.
- The browser journeys (`tests/PostmanClone.Web.E2E`) were not run in this session with the stack; they run in CI's `e2e` job for the head SHA.
- `scripts/smoke-auth.sh` against the live authservice: see ARCHITECTURE_REVIEW.md §4.
- The OTLP exporter against a collector (SR-17).
