# Security policy

## Reporting a vulnerability

Please **do not** open a public issue. Report it privately to the maintainer,
[@konradcinkusz](https://github.com/konradcinkusz), through a direct message on GitHub.
Include:

- what you found and where (file, endpoint, or work package),
- the steps to reproduce it,
- the impact you see: whose data, or which action, is exposed.

GitHub's private vulnerability reporting is the preferred channel once the owner enables it
(see the owner actions in the implementation plan). Until then, use the direct message.

Please allow a reasonable time for a fix before you disclose the issue publicly.

## Scope

This repository: the Blazor Web App, the three domain services, the shared kernel, the
Aspire AppHost, and the build and CI configuration. The external identity provider,
[`authservice`](https://github.com/konradcinkusz/authservice), has its own policy.

## Supported versions

There are no releases yet. The `main` branch is the only supported line.

## For contributors

- **Credentials never enter the repository.** Services read secrets from the environment.
  Local secrets come from `scripts/gen-dev-secrets.sh` into the gitignored `.local-stack/`.
  gitleaks runs as a pre-commit hook (`scripts/install-hooks.sh`) and over the full history in
  CI (`.github/workflows/secret-scan.yml`).
- **If a credential leaks, rotate it first**, then remove it from the source, and only then
  decide about history. A pushed commit is disclosed the moment it is pushed.
- **Findings already in history** are listed by fingerprint in `.gitleaksignore`, each with the
  reason it is not a live credential.
- **Dependencies:** restore fails on a known-vulnerable NuGet package (`NuGetAudit`), and
  Dependabot proposes version updates weekly.
- **Tokens never reach browser JavaScript**, logs, URLs or error messages (see
  [`AGENTS.md`](AGENTS.md)).
