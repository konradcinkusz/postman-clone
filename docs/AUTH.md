# Authentication with authservice

PostmanClone does not have its own accounts. [authservice](https://github.com/konradcinkusz/authservice)
owns users, organizations, passwords and tokens (ADR-0004). This page records what the services and the
Web rely on, what the pinned version actually does, and where the two differ from
[CONTRACTS §6](implementation-plan/CONTRACTS.md#6-authservice-consumed-v034).

## Pinned version and how it runs

| | |
|---|---|
| Version | `v0.3.4`, one constant in `src/PostmanClone.AppHost/Resources/AuthServiceResource.cs` and one in `scripts/run-local-stack.sh` |
| Container (AppHost) | `ghcr.io/konradcinkusz/authservice:v0.3.4`, port 8080 inside, own database `authdb`, `Database__SchemaMode=Migrate`, RS256 |
| Local, no Docker | `scripts/run-local-stack.sh up` clones the tag to `.local-stack/`, builds it, and listens on port 5300 |
| Smoke test | `scripts/smoke-auth.sh` against a running authservice; CI job `auth-smoke` |
| Source read | Every fact below was read from the v0.3.4 source (file and line given). Runtime values are listed under [Observed](#observed) |

Configuration this repository sets (the keys are authservice's own):

| Setting | Value | Why |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | the `authdb` database | Startup fails without it |
| `Database__SchemaMode` | `Migrate` | Keeps the schema under migrations, not `EnsureCreated` |
| `Database__MigrationsAssembly` | `AuthService.Migrations.PostgreSQL` | Required with `Migrate` |
| `Jwt__PrivateKeyPem` | PKCS#8 RSA 2048 from a secret | Selects RS256: with a private key configured and no `Jwt:Algorithm`, authservice signs RS256 (`Services/JwtSigningKeys.cs`, `ResolveAlgorithm`) |
| `Jwt__Issuer`, `Jwt__Audience` | `PostmanClone` | A token from another product cannot authenticate here |
| `Jwt__PublicBaseUrl` | this service's own origin | The `jwks_uri` in discovery (`Program.cs`, line 493) |
| `FrontendBaseUrl` | the Web origin | Used in verification and reset links (`AuthController.cs`, lines 331 and 724) |

Without `SendGrid:ApiKey`, authservice does not require email verification: `Auth:RequireConfirmedEmail`
defaults to "a sender is configured" (`Program.cs`, line 150). So `register` returns tokens at once, and no
`202` is seen locally. With a sender configured, `register` returns `202` and no tokens until the address
is confirmed.

## The flow the Web uses

1. **Sign in.** `POST /api/v1/auth/login {email, password}` returns the token pair. The Web keeps it on the
   server, behind an HttpOnly session cookie (ADR-0005). If the account has two-factor enabled, the answer is
   a challenge, completed at `POST /api/v1/auth/2fa/login`. The Web shows "not supported yet" (plan §1).
2. **Register.** `POST /api/v1/auth/register` needs both consent versions from `GET /api/v1/auth/consents/versions`.
   Each must match the server's current version exactly, or the request fails with `400`.
3. **Workspace.** The access token carries one `organization` claim per membership, and
   `organization:{id}:role` for each. The Web reads the single `organization` claim. Zero means onboarding,
   two or more means fail closed (ADR-0004).
4. **Onboarding (zero organizations).** `POST /api/v1/organizations {name}` creates the organization, and the
   caller becomes its `Owner` (`OrganizationsController.cs`, line 158). The token the caller already holds has
   no `organization` claim. `POST /api/v1/auth/refresh {refreshToken}` issues a new pair that does.
5. **Refresh.** `POST /api/v1/auth/refresh` rotates the refresh token. A used or revoked token is refused with
   `401` (`AuthController.cs`, line 273).
6. **Sign out.** `POST /api/v1/auth/logout` needs a bearer token (`[Authorize]`, line 848). It revokes all of the
   user's refresh tokens, not only the current one.

## Endpoints the services use (read from v0.3.4)

| Method and path | Request | Response | Source |
|---|---|---|---|
| `POST /api/v1/auth/register` | `{email, password, acceptedTermsVersion, acceptedPrivacyVersion, locale?}` | `200 TokenResponse`, or `202` when verification is required | `AuthController.cs:82`, `DTOs/AuthDtos.cs:9` |
| `POST /api/v1/auth/login` | `{email, password}` | `200 TokenResponse`, `200` two-factor challenge, or `401` | `AuthController.cs:201` |
| `POST /api/v1/auth/2fa/login` | the challenge | `200 TokenResponse` | `TwoFactorController.cs:173` |
| `POST /api/v1/auth/refresh` | `{refreshToken}` | `200 TokenResponse`, or `401` | `AuthController.cs:259` |
| `POST /api/v1/auth/logout` | none; bearer | `200 {message}` | `AuthController.cs:849` |
| `GET /api/v1/auth/me` | bearer | `200 UserInfoResponse` (`organizations: [{id, name, imageUrl, role}]`) | `AuthController.cs:350` |
| `GET /api/v1/auth/consents/versions` | none | `200 {terms, privacy, cookies}` | `AuthController.cs:533` |
| `POST /api/v1/organizations` | `{name, description?, imageUrl?}`; bearer (the whole controller is `[Authorize]`, line 20) | `201 OrganizationDto` with `userRole: "Owner"` (the field is `UserRole`, not `role`); `Location` | `OrganizationsController.cs:158`, `DTOs/OrganizationDtos.cs:27` |
| `GET /api/v1/organizations/{id}` | bearer | `200 OrganizationDetailDto` (members) | `OrganizationsController.cs:107` |
| `POST /api/v1/organizations/{id}/invite` | `InviteMemberRequest`; bearer, Owner or Admin | invitation | `OrganizationsController.cs:371` |
| `POST /api/v1/organizations/invitations/accept` | `AcceptInvitationRequest`; bearer | membership | `OrganizationsController.cs:475` |
| `GET /.well-known/jwks.json` | none | `200 {keys: [...]}`, empty under HS256 | `Program.cs:474` |
| `GET /.well-known/openid-configuration` | none | discovery document | `Program.cs:484` |
| `GET /health/ready` | none | `200` once the schema is migrated and the database answers, else `503` | `Program.cs:516` |
| `GET /health`, `GET /alive` | none | `200` liveness | `Program.cs:507`, `:512` |

Every route is served both at `/api/v1/...` and at an unversioned `/api/...` alias. Only the `/api/v1`
form is used here.

## Claims in the access token

Source: `Services/TokenService.cs`, `BuildClaimsAsync` (line 265), signed by `GenerateAccessToken` with the
claims as they are. The names below are the names in the token, which are not always the names in
[CONTRACTS §6](implementation-plan/CONTRACTS.md#6-authservice-consumed-v034).

| Claim | Value | Used for | Contract name |
|---|---|---|---|
| `sub` | user id | the user | `sub` |
| `email` | email | display | `email` |
| `jti` | random per token | revocation key (not used here) | `jti` |
| `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier` | user id | the same user, for ASP.NET Core | not in the contract |
| `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name` | username | display | `name` |
| `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` | one per system role the user holds (usually none) | authservice's own admin endpoints | `role` |
| `organization` | an organization id, **one claim per membership** | the workspace (ADR-0004) | `organization` |
| `organization:{id}:role` | `Owner`, `Admin` or `Member` | the role in that workspace | `organization:{id}:role` |

Two things follow.

- The services must read the **single** `organization` claim as the workspace and fail closed when there are
  zero or two or more.
- The `role` and `name` claims are long URIs in the payload. A service that looks up `role` gets nothing.
  The kernel's `WorkspaceClaims` (WP02) must read `organization` and `organization:{id}:role`, which are short.

Claim names printed by the CI `auth-smoke` job are the runtime evidence for this table; see [Observed](#observed).

## Differences from CONTRACTS §6

Each difference is recorded here, not changed silently. None needs an amendment to CONTRACTS, because
the contract's endpoints and claim names are all still available. Where a name differs, the table above
gives the real one.

| # | CONTRACTS §6 says | v0.3.4 does | Effect |
|---|---|---|---|
| 1 | `POST /api/v1/organizations` exists (creator becomes Owner) | **Confirmed**: `OrganizationsController.cs:158`, creator gets `OrganizationRole.Owner` | None. The brief's claim that there is no create-organization endpoint is wrong for v0.3.4 |
| 2 | `role` claim for system roles | the claim type is the ASP.NET URI `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` | Services must not look for `role` |
| 3 | `name` claim | the claim type is `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name` | Same |
| 4 | `register` answers `202` for a pending account | `202` only when a sender is configured; without one, `200` with tokens | Local runs get tokens at once. The Web must handle both |
| 5 | `login` two-factor answer is `200 {requiresTwoFactor, challengeToken, ...}` | `200` with a two-factor challenge (`TwoFactorRequiredResponse`) | Consistent in shape; fields not exercised here |
| 6 | `logout` takes a bearer token | `[Authorize]`, and it revokes **all** of the user's refresh tokens | A logout signs out every session of that user, not only the current one |
| 7 | `GET /api/v1/organizations/{id}` (members) | `OrganizationDetailDto` (members in the body) | Consistent |

## Members and invitations (WP24)

The members page (`/workspace`) and the invitation flow use these authservice endpoints. Each was read from v0.3.4
(`OrganizationsController.cs`) and exercised against a local v0.3.4 on 2026-10-31 (see [Observed](#observed)).

| Method and path | Who may call it | What the Web does with the answer |
|---|---|---|
| `GET /api/v1/organizations/{id}` | any member | the workspace name and members (`OrganizationDetailDto`) |
| `GET /api/v1/organizations/{id}/invitations` | Owner, Admin (`403` otherwise) | the pending invitations, shown without their token |
| `POST /api/v1/organizations/{id}/invite` `{email, role}` | Owner, Admin (`403` otherwise) | `200` invitation; `400` with `error` for "already a member" or "already been sent", or a validation problem; `403` |
| `POST /api/v1/organizations/invitations/accept` `{token}` | the signed-in address the invitation names | `200 {message, organizationId}`; `404 "Invitation not found"` for an unknown token or another address; `400 "Invitation has already been accepted"`; `400 "Invitation has expired"`; `403` with `emailVerificationRequired` while the address is unconfirmed |

Facts the flow relies on:

- **Roles.** authservice accepts `Owner`, `Admin` or `Member` in the invite body, and an Admin may not invite at Owner
  (`403`). The Web offers `Admin` and `Member` only (the plan's choice), and an Owner's invite is authservice's path.
- **The invitation carries a token, not a link.** The invite response has no token, and the email (SendGrid) carries
  the token as text: `Your invitation token: <token>` (`SendGridEmailService.cs`). The Web has no link in it. The invitee
  opens `<Web origin>/accept-invitation?token=<token>`, which is how the Web's own page reads it.
- **Without an email provider**, `NoOpEmailService` logs a warning that names the address and the organization. It
  writes the token only when `ASPNETCORE_ENVIRONMENT` is `Development`; otherwise it writes `[withheld]`
  (`NoOpEmailService.cs`, `Redact`). The local stack runs authservice in Production unless told otherwise, so a developer
  starts it with `AUTHSERVICE_ENVIRONMENT=Development scripts/run-local-stack.sh up` and reads the token from
  `.local-stack/authservice.log`:

  ```text
  Email sending is not configured. Invitation email to <address> for organization <name> was not sent. Invitation token: <token>. ...
  ```

- **Email is not verified for the invitee.** `RequireConfirmedEmail` defaults to "a sender is configured", so a local
  account accepts at once. With a sender, an unconfirmed address gets `403` on accept.
- **Joining twice.** An invitation to an organization the account already belongs to is marked accepted and answers `200`,
  with no new membership. The Web reads the `organizationId` against the organizations the session held before the
  request, and says "already a member" instead of "joined".
- **The session must refresh** after an accept, or the new `organization` claim is not in the token. The Web refreshes
  at once and then redirects. An account with two organizations then reaches the Multiple page (ADR-0004).

Local dev token retrieval is the path above; a token is single-use and expires after 7 days (`ExpiresAt` = created + 7 days).

## Limits (ADR-0004)

- **No workspace switch.** authservice issues no organization-scoped token and has no switch endpoint. A user
  with two memberships is refused by every domain endpoint (`403 multiple_workspaces`) until one membership is
  removed.
- **No organization-scoped token.** The access token lists every membership. The services pick the workspace
  only when exactly one is present.
- **Roles are `Owner`, `Admin`, `Member`.** There are no viewer roles (ADR-0001).
- **Tokens are signed RS256 with `Jwt__PrivateKeyPem`.** Losing the key signs every user out. Leaking it lets
  the holder mint tokens for anyone (`docs/DEPLOYMENT.md`).
- **Lifetimes.** Access tokens last `AccessTokenMinutes` (`TokenService.cs`, line 332). Refresh tokens last 7 days by default (`DefaultRefreshTokenDays`, line 30).

## Observed

Runtime values are recorded here only from a real run. Nothing below is copied from the source.

- **Runtime status:** the CI job `auth-smoke` runs `scripts/run-local-stack.sh up` and `scripts/smoke-auth.sh` on a fresh
  runner with a PostgreSQL service container; its log holds the claim names and status codes for this table. In the WP12
  session (2026-10-30) authservice v0.3.4 was built from its tag and run locally on port 5300, and both the smoke test and
  the Web's sign-in flow were run against it (see below). There is no Docker daemon in sessions, so the container image was
  not run.
- **Observed in CI run 40353922349 (head 348fc89):** `GET /health/ready` 200, `GET /.well-known/jwks.json` 200 with 1 key,
  `GET /api/v1/auth/consents/versions` 200, `POST /api/v1/auth/register` 200 (tokens at once: no sender is configured),
  `POST /api/v1/auth/login` 200, `GET /api/v1/auth/me` 200, `POST /api/v1/organizations` 201.
  `expiresIn` 3600 and `tokenType` `Bearer`.
  Claim names on the token issued by register (before any organization): `aud`, `email`, `exp`, `iss`, `jti`, `sub`,
  and the two ASP.NET URIs `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name` and
  `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier`. No `organization` claim, as the source predicts.
  `aud` and `iss` are present, which the source sets from `Jwt:Audience` and `Jwt:Issuer`; the table above does not list them.
- **Observed in the WP12 session (2026-10-30, live, authservice v0.3.4 on localhost):**
  - Claim names after a refresh that follows an organization creation: `aud`, `email`, `exp`, `iss`, `jti`, `name`,
    `nameidentifier`, `organization`, `organization:{id}:role`, `sub`. The `organization` claim and its role claim are present,
    as the table above says. There is no `role` claim, because the user holds no system role.
  - `POST /api/v1/auth/refresh` with a refresh token already spent: `401`. Rotation is single use.
  - `POST /api/v1/auth/logout` with a bearer: `200`. A refresh with the newest refresh token issued before the logout: `401`.
    Logout revokes every refresh token of the user (row 6 of the table above), not only the one presented.
  - The Web, against this authservice: sign-in with the right password sets the session cookie and redirects; a wrong
    password redirects with `error=invalid` and no cookie; the access token from a live login verifies against the
    published JWKS (RS256, issuer `PostmanClone`, audience `PostmanClone`); sign-out redirects to `/sign-in` and the old
    cookie no longer opens a session. Creating the first organization from the Web refreshes the session and the next
    page shows the workspace as Owner.

- **Observed in the WP24 session (2026-10-31, live, authservice v0.3.4 on localhost, `AUTHSERVICE_ENVIRONMENT=Development`):**
  - Owner: `POST /api/v1/organizations` 201; `GET /api/v1/organizations/{id}` 200 with one member; `POST …/invite` 200. The
    invite body has `email`, `role`, `emailStatus`, `isAccepted`, `organizationId`, `organizationName`, `role`, and no token.
    A second invite to the same address `400 "An invitation has already been sent to this email"`. A malformed address
    `400` as a validation problem with `errors` and no `error` text. `GET …/invitations` 200 with `emailStatus` `Sent`.
  - The invitation token appears in `.local-stack/authservice.log` in Development (see above).
  - Invitee: `GET /api/v1/organizations/{id}` `404` for a non-member. `GET /api/v1/organizations/invitations` lists the
    pending invitation with its organization name. `POST …/invitations/accept` with another address's session: `404`;
    with the right one `200` `organizationId`; again: `400 "Invitation has already been accepted"`; an unknown token: `404`.
  - After a refresh, the claims carry `organization` and `organization:{id}:role` = `Member`.
  - A Member's `POST …/invite` and `GET …/invitations`: `403`, and the organization read is `200`.
  - Through the Web (the same authservice): the invite form posts and redirects `notice=invited`; the accept page with a
    token offers the form to an account with no workspace; the accept redirects `notice=joined` and the page then shows
    the Member view with no invite form; a replay redirects `error=used`; an anonymous visit to the accept page keeps the
    token through sign-in; an account with two organizations gets the Multiple explanation.
  - Not observed live: the "already a member" accept answer (`200` for an invitation to an organization the account holds).
    It needs a second pending invitation, which authservice does not allow while the first is open. The Web's branch for it
    is covered by the fake and the `organizationId` comparison.

## Not in scope here

- Google and GitHub sign-in (`/api/v1/external-auth/*`) and MCP connector clients (`AuthorizationServer`). Both
  are off by default in authservice, and the plan does not use them.
- Email verification and password reset flows: they need a SendGrid key. Local runs do not exercise them.
- Account export and deletion (`/api/v1/auth/export`, `DELETE /api/v1/auth/account`).
