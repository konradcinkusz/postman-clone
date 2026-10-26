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

- **Runtime status:** not run in the authoring session. The authservice build was refused by the session's
  permission check, and there is no Docker daemon, so neither the container nor the local build ran there. The
  CI job `auth-smoke` runs `scripts/run-local-stack.sh up` and `scripts/smoke-auth.sh` on a fresh runner with a
  PostgreSQL service container. Its log holds the claim names and status codes for this table.
- **Observed in CI run 40353922349 (head 348fc89):** `GET /health/ready` 200, `GET /.well-known/jwks.json` 200 with 1 key,
  `GET /api/v1/auth/consents/versions` 200, `POST /api/v1/auth/register` 200 (tokens at once: no sender is configured),
  `POST /api/v1/auth/login` 200, `GET /api/v1/auth/me` 200, `POST /api/v1/organizations` 201.
  `expiresIn` 3600 and `tokenType` `Bearer`.
  Claim names on the token issued by register (before any organization): `aud`, `email`, `exp`, `iss`, `jti`, `sub`,
  and the two ASP.NET URIs `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name` and
  `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier`. No `organization` claim, as the source predicts.
  `aud` and `iss` are present, which the source sets from `Jwt:Audience` and `Jwt:Issuer`; the table above does not list them.
- **Still to observe:** the claim names after refresh (the `organization` and `organization:{id}:role` claims), `/api/v1/auth/logout`, and the refused refresh. The next CI run prints them.

## Not in scope here

- Google and GitHub sign-in (`/api/v1/external-auth/*`) and MCP connector clients (`AuthorizationServer`). Both
  are off by default in authservice, and the plan does not use them.
- Email verification and password reset flows: they need a SendGrid key. Local runs do not exercise them.
- Account export and deletion (`/api/v1/auth/export`, `DELETE /api/v1/auth/account`).
