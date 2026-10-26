# postman-clone

An API client in the spirit of Postman: collections, environments with secret variables, a
request builder, a server-side sender with SSRF protection, response viewer, history, and
import and export of Postman v2.1 and cURL.

It is written from scratch as a **Blazor Web App (interactive server, no WebAssembly, no Node)**,
signed in through the external [`authservice`](https://github.com/konradcinkusz/authservice),
composed by an **Aspire AppHost**, and built to
[`architecture-standards`](https://github.com/konradcinkusz/architecture-standards).

## Status

**Baseline only.** The repository holds the plan, the decisions, the repository hygiene and the
CI configuration, and the solution with its shared kernel (WP01). The services and the Web do not exist yet;
the work packages below deliver them in order.

| Item | Status |
|---|---|
| Implementation plan (scope, architecture, contracts, work packages) | Written: [`docs/implementation-plan/`](docs/implementation-plan/README.md) |
| Architecture decisions | Written: ADR-0001 to ADR-0009, in [`docs/decisions/`](docs/decisions/README.md) |
| Repository baseline, CI, secret scanning | This commit (WP00) |
| Solution and shared kernel (`PostmanClone.ServiceDefaults`) | Written: WP01 |
| Services, Web | Not started (WP02 onward) |
| Running the product locally | Not possible yet: there is nothing to run |

The work packages (WP00–WP28), their dependencies and their acceptance criteria are in
[`docs/implementation-plan/WORK-PACKAGES.md`](docs/implementation-plan/WORK-PACKAGES.md).

## Layout

```
docs/implementation-plan/   the build plan: README, CONTRACTS (frozen HTTP contracts), PROTOCOL, WORK-PACKAGES
docs/decisions/             architecture decision records
docs/architecture/          DEVIATIONS.md: where the build departs from architecture-standards
scripts/                    install-hooks.sh, hooks/pre-commit (secret scan), check-doc-links.sh
.github/                    CI (pr-validation, secret-scan), CODEOWNERS, Dependabot, templates
```

The planned source layout (`src/`, `tests/`) is described in
[`docs/implementation-plan/README.md`](docs/implementation-plan/README.md#3-repository-layout-wp00wp03-create-it).
Of that layout, only the kernel and its tests exist (WP01).

## Working on it

Read [`AGENTS.md`](AGENTS.md) first: it lists the rules that are easy to break (tenant isolation,
tokens, secrets, masking, frozen contracts).

One-time setup for a clone, which installs the pre-commit secret scan:

```bash
scripts/install-hooks.sh
```

The hook needs gitleaks, or Docker to run the gitleaks image. Without one, it refuses to commit, by
design.

## Security

See [`SECURITY.md`](SECURITY.md). Do not report vulnerabilities in a public issue.

## Licence

MIT. See [`LICENSE`](LICENSE).
