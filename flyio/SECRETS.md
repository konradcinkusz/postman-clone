# Fly secrets for the estate

Every value that is not in a `flyio/*.fly.toml` `[env]` table is a Fly **secret**: set with `flyctl secrets set`, never committed,
never printed. Fly shows only the names and digests afterwards (`flyctl secrets list -a <app>`). The step-by-step order is in
[docs/FLY_DEPLOYMENT.md](../docs/FLY_DEPLOYMENT.md) §4–§6. The workflow `.github/workflows/flyio.yml` checks the **names** before it
deploys an app, and stops with a message when one is missing.

Generate values on a machine you control, keep them in your password manager, and do not put them in a file in this repository.
Nothing under `.local-stack/` is used on Fly: those are development values only.

## The secrets

| Secret (name) | Set on app | What it is | Generate with | If it is missing or wrong |
|---|---|---|---|---|
| `POSTGRES_PASSWORD` | `postmanclone-postgres` | Password of the PostgreSQL superuser `postgres`. Used by the image's own start-up. | `openssl rand -hex 24` | The image refuses to initialise. Nothing else is affected. |
| `PG_AUTH_PASSWORD` | `postmanclone-postgres` | Password of the role `auth_svc`, which owns `authdb`. Read by the init script only. | `openssl rand -hex 24` | The first start stops with a message naming it. The volume is empty: destroy and bootstrap again (FLY_DEPLOYMENT §4). |
| `PG_COLLECTIONS_PASSWORD` | `postmanclone-postgres` | Role `collections_svc`, owner of `collectionsdb`. | `openssl rand -hex 24` | As above. |
| `PG_ENVIRONMENTS_PASSWORD` | `postmanclone-postgres` | Role `environments_svc`, owner of `environmentsdb`. | `openssl rand -hex 24` | As above. |
| `PG_EXECUTION_PASSWORD` | `postmanclone-postgres` | Role `execution_svc`, owner of `executiondb`. | `openssl rand -hex 24` | As above. |
| `ConnectionStrings__DefaultConnection` | `postmanclone-authservice` | authservice's connection to `authdb` as `auth_svc`. Same key as the AppHost. | Build it from the template below. | authservice cannot migrate or serve; `/health/ready` stays 503 and the deploy fails the health check. The workflow stops first. |
| `Jwt__PrivateKeyPem` | `postmanclone-authservice` | The RS256 **signing key**, PEM (PKCS#8). Whoever holds it can mint a token for any user. Losing it signs every user out. | `openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out <file outside the repo>` | The consumers reject every token, or authservice does not sign (its behaviour without the key is authservice's, not verified here). Treat as required. |
| `ConnectionStrings__collectionsdb` | `postmanclone-collections` | CollectionsService's connection to `collectionsdb` as `collections_svc`. | Template below. | The service does not become ready. |
| `ConnectionStrings__environmentsdb` | `postmanclone-environments` | EnvironmentsService's connection to `environmentsdb` as `environments_svc`. | Template below. | The service does not become ready. |
| `Secrets__EncryptionKey` | `postmanclone-environments` | The 32-byte key (base64) that encrypts environment **secret values** (ADR-0006). **Losing or changing it makes every stored secret unreadable.** | `openssl rand -base64 32` | The service starts, secret writes answer `503 secrets_unavailable`, and `/health` reports `secrets: unavailable` (OPERATIONS §2). |
| `ConnectionStrings__executiondb` | `postmanclone-execution` | ExecutionService's connection to `executiondb` as `execution_svc` (history). | Template below. | The service does not become ready. |

The web app `postmanclone-web` has **no** secret. Its settings are in `flyio/web.fly.toml`.

## Connection string template

Each service gets one string to its own database and nothing else (P3). The host is the Fly private name of the database app.

```
Host=postmanclone-postgres.internal;Port=5432;Database=<db>;Username=<role>;Password=<password>;SSL Mode=Disable
```

| Database | Role | Password secret used in the init script |
|---|---|---|
| `authdb` | `auth_svc` | `PG_AUTH_PASSWORD` |
| `collectionsdb` | `collections_svc` | `PG_COLLECTIONS_PASSWORD` |
| `environmentsdb` | `environments_svc` | `PG_ENVIRONMENTS_PASSWORD` |
| `executiondb` | `execution_svc` | `PG_EXECUTION_PASSWORD` |

`SSL Mode=Disable` is deliberate and recorded in ADR-0029: the traffic is inside Fly's WireGuard-encrypted private network, and the
image has no TLS certificate. A change to `SSL Mode=Require` needs a certificate on the database first.

## Setting them

```bash
# Generate the passwords (keep them in the password manager; print nothing into a shared log)
openssl rand -hex 24      # one per PG_* secret and POSTGRES_PASSWORD

# The database secrets, before the first start of postgres
flyctl secrets set -a postmanclone-postgres POSTGRES_PASSWORD=<value> PG_AUTH_PASSWORD=<value> \
  PG_COLLECTIONS_PASSWORD=<value> PG_ENVIRONMENTS_PASSWORD=<value> PG_EXECUTION_PASSWORD=<value>

# authservice: the connection string and the signing key (a multi-line PEM is passed as one quoted argument)
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out ~/postmanclone-jwt.pem && chmod 600 ~/postmanclone-jwt.pem
flyctl secrets set -a postmanclone-authservice \
  "ConnectionStrings__DefaultConnection=Host=postmanclone-postgres.internal;Port=5432;Database=authdb;Username=auth_svc;Password=<PG_AUTH_PASSWORD>;SSL Mode=Disable" \
  "Jwt__PrivateKeyPem=$(cat ~/postmanclone-jwt.pem)"
rm ~/postmanclone-jwt.pem          # after it is stored in the password manager

# environments: the encryption key first, in a file, then store it before anything else is done with it
openssl rand -base64 32 > ~/postmanclone-encryption-key && chmod 600 ~/postmanclone-encryption-key
# store the content of ~/postmanclone-encryption-key in the password manager NOW; a lost key cannot be recovered
flyctl secrets set -a postmanclone-environments \
  "ConnectionStrings__environmentsdb=Host=postmanclone-postgres.internal;Port=5432;Database=environmentsdb;Username=environments_svc;Password=<PG_ENVIRONMENTS_PASSWORD>;SSL Mode=Disable" \
  "Secrets__EncryptionKey=$(cat ~/postmanclone-encryption-key)"
rm ~/postmanclone-encryption-key   # after it is stored

# collections and execution: one connection string each
flyctl secrets set -a postmanclone-collections \
  "ConnectionStrings__collectionsdb=Host=postmanclone-postgres.internal;Port=5432;Database=collectionsdb;Username=collections_svc;Password=<PG_COLLECTIONS_PASSWORD>;SSL Mode=Disable"
flyctl secrets set -a postmanclone-execution \
  "ConnectionStrings__executiondb=Host=postmanclone-postgres.internal;Port=5432;Database=executiondb;Username=execution_svc;Password=<PG_EXECUTION_PASSWORD>;SSL Mode=Disable"
```

## Rotation

- **A database password:** change the role's password in postgres (`ALTER ROLE … PASSWORD`), then set the matching connection string on the service. The init script runs once, so it does not change a password.
- **The signing key:** every token signed by the old key is refused after the change, so every user signs in again. Plan it as a sign-out for everyone.
- **The encryption key:** not rotated in place. The encrypted values would need re-encrypting by a migration that does not exist yet (open item).
