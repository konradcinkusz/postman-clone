# Sub-session protocol

You are an autonomous sub-session (Haiku 5.5). You deliver **exactly one work package**
(WP) from [WORK-PACKAGES.md](WORK-PACKAGES.md): one branch `wp/NN-slug`, one PR, **merged by you**.
Nobody answers questions mid-run. If something is ambiguous, pick the option that is closest to
[README.md](README.md), [CONTRACTS.md](CONTRACTS.md) and the standards, write the choice in the PR
body, and continue. Do not build other WPs, do not "improve" things outside your scope.

## 1. Read first (in this order, do not skip)

1. `docs/implementation-plan/README.md`, `CONTRACTS.md`, this file, then **your WP section** only.
2. `AGENTS.md` and the existing code your WP touches (it is on `main`).
3. The standards, attached read-only or cloned: `git clone --depth 1 https://github.com/konradcinkusz/architecture-standards /home/user/architecture-standards` (public). Read
   `docs/architecture/00-REFERENCE-ARCHITECTURE.md` (P1–P15) and the guides your WP names:
   `BLAZOR-WEB-APP`, `SERVICE-API-PATTERNS`, `IDENTITY-AND-ACCOUNTS`, `FRONTEND-BFF` (concepts),
   `STATE-SNAPSHOT-PERSISTENCE` (only if your WP says so), `E2E-ACCEPTANCE-TESTING`,
   `TESTING-STRATEGY`, `REPO-BASELINE`, `SECURITY-REVIEW`.
4. Worked example of the estate (read-only, only when your WP points at a file):
   `git clone --depth 1 https://github.com/konradcinkusz/aurelius-promptus /home/user/aurelius-promptus`.
   authservice (public, read-only): `git clone --depth 1 --branch v0.3.4 https://github.com/konradcinkusz/authservice /home/user/authservice`.
   If a clone is refused, say so in your final message and continue from the plan.

Never copy code from authservice into this tree. Never push to any repository other than this one.

## 2. Environment (sessions have no Docker daemon)

```bash
# .NET 10 SDK (global.json pins 10.0.100, rollForward latestFeature — apt gives 10.0.113). The dotnet.microsoft.com
# download host is blocked; apt works.
command -v dotnet >/dev/null || { apt-get update -qq && DEBIAN_FRONTEND=noninteractive apt-get install -y -qq dotnet-sdk-10.0; }
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
# PostgreSQL 16 is installed but stopped:
service postgresql start && su postgres -c "psql -c \"ALTER USER postgres PASSWORD 'postgres'\""
export TEST_POSTGRES="Host=localhost;Username=postgres;Password=postgres"
```

- NuGet (`api.nuget.org`) and GitHub clones work; `dot.net`, `ghcr.io` pulls and Docker do **not**.
  You cannot run the AppHost or build images. Say so honestly in the PR body ("not verified: …").
- Chromium + Playwright are **pre-installed** (`/opt/pw-browsers`). Never run `playwright install`.
- Tests: `dotnet test` runs unit + InMemory tests with no setup; PostgreSQL tests run only when
  `TEST_POSTGRES` is set (skipped, not failing, otherwise). Run them with it set before you open the PR.
- Build must be clean: warnings are errors (`Directory.Build.props`), restore fails on vulnerable packages.
- Use only the scratchpad / repo for files; do not write secrets to disk outside gitignored `.local-stack/`.

## 3. Git flow

```bash
git fetch origin main && git checkout -B wp/NN-slug origin/main   # branch name is given in your prompt (outcome_branch)
```

- **Push early and often.** You can be stopped by a usage limit with nothing pushed. Commit and
  `git push -u origin wp/NN-slug` after the first compiling slice and then after every meaningful
  step (wip commits are fine; the PR is squash-merged).
- Commit messages: area prefix (`auth:`, `collections:`, `environments:`, `exec:`, `web:`, `build:`, `ci:`, `docs:`, `test:`),
  imperative summary, a body that says **why**. End every commit and the PR body with the attribution
  lines from your own system reminder. One logical commit per step.
- **Before opening the PR and again before merging:** `git fetch origin main && git merge origin/main`. Parallel WPs collide on
  `PostmanClone.sln`, `Directory.Packages.props`, the AppHost, `docs/decisions/`, `AGENTS.md`,
  `.github/workflows/pr-validation.yml`. Resolve **keeping both sides**; regenerate nothing by hand that a tool
  generates. Re-run build + tests after resolving.
- **ADR numbers** are reserved per WP in WORK-PACKAGES.md. If the file already exists on `main` with different
  content, renumber yours to the next free number before merging and fix links.
- Never rewrite published history, never force-push, never push to `main`, **never push tags**,
  never delete remote branches (the permission classifier refuses; just report the stale branch name).
- Do not change repository settings (branch protection, secrets, Actions permissions) — list what you wanted in the PR body under "Owner actions".
- Do not deploy anything. No Fly.io, Azure or registry pushes (ADR-0007).

## 4. Pull request, CI, merge — you do all of it

1. Open the PR to `main` as ready for review (not draft), using `.github/pull_request_template.md` headings
   once it exists (WP00): Summary · Acceptance criteria (checked list copied from your WP) · What I ran (exact commands + result) ·
   Not verified · Owner actions · Contract amendments · ADRs. End with the attribution line.
2. Wait for CI: poll `mcp__github__pull_request_read` (`get_check_runs`) / `mcp__github__actions_list`. **Do not stop at "review ready".**
   Red CI on your PR is yours: read the job log (`mcp__github__get_job_logs`), fix, push, repeat. A failure that is red on `main` too:
   say so in the PR, port the fix if one exists, otherwise report it. Never skip, disable or weaken a test to get green.
3. When CI is green and the branch contains the latest `main`: **squash-merge** with `mcp__github__merge_pull_request`
   (`merge_method: squash`, title = PR title). Verify with `pull_request_read` that state is merged.
4. Check `main` CI for your merge commit (`mcp__github__actions_list`, branch `main`). If it went red because of your change, fix forward immediately in a new PR from `wp/NN-slug-fix`.
5. Do not wait for the supervisor and do not ask for approval. Do not unsubscribe or delete branches.

## 5. Engineering rules (the ones that get violated)

- **P-rules.** `Program.cs` is a manifest (calls into `ServiceCollectionExtensions`/endpoint-mapping classes, < ~120 lines);
  endpoints are minimal APIs in groups that make the trust level visible; interfaces + DI instead of base classes;
  `AddServiceDefaults()` + `MapDefaultEndpoints()` in every service; one `HttpClient` resilience handler only (the kernel's).
- **Schema** = EF Core migrations (Npgsql) applied by `MigrateAsync` in a hosted service **after** Kestrel starts; `EnsureCreated` only for the
  InMemory/test path. Add a test that the model has no pending changes versus the last migration snapshot.
- **No secret in the tree** — not in code, config, comments, test names. Test fixtures that must look like secrets carry `gitleaks:allow`.
  Local secrets come from `scripts/gen-dev-secrets.sh` into gitignored `.local-stack/`.
- **Tenant isolation**: every id endpoint = load, check `WorkspaceId`, `404`; add a scoping test **per endpoint** (two workspaces, same ids), named `Scoping_<METHOD>_<route>` (e.g. `Scoping_GET_requests_id`) so WP25's matrix test can find it.
  Workspace comes from the token only (CONTRACTS §0).
- **Tokens never reach browser JS**, component fields persisted by the framework, logs, URLs or error messages. Do not log request/response
  bodies, header values or variable values — log method, host, status, sizes, durations.
- **Time**: inject `TimeProvider`. **No test may depend on today's date or a pinned calendar date** (a pinned test clock already broke once when the date moved).
- **Web**: interactive server render mode on `HeadOutlet` and `Routes` (pinned by a test), scoped-styles bundle linked (pinned), colours only via CSS tokens
  (stylesheet test), both themes, 390 px width works, every load catches into state and renders `role="alert"`, off-thread updates via `InvokeAsync`,
  background loops own a `CancellationTokenSource` cancelled in `Dispose`, no `HttpContext` in components, no JS interop and no JS/CSS libraries
  (ADR-0009). Logic lives in plain C# classes next to the page with xUnit tests (BLAZOR-WEB-APP §10).
- **Tests assert something real**: no placeholder `Assert.True(true)`, no guard-then-return, unfinished things are `[Fact(Skip = "reason")]`.
  Test at the layer that has the logic (TESTING-STRATEGY). E2E waits for the Blazor circuit and uses roles/test ids, never fixed sleeps.
- **Do not claim more than you verified** — in code comments, docs or the PR. Prefer "not verified" over a guess.
- Keep README/AGENTS/docs claims true: if you add a script, route, port or project, update the doc that lists it in the same PR.
- File size: kernel `PostmanClone.ServiceDefaults` stays ≤ 800 lines (checked in CI after WP01); no entities/DTOs/enums/user-facing strings in it.

## 6. Definition of done

- [ ] Every acceptance criterion in your WP is met and ticked in the PR body, each with the command or test that shows it.
- [ ] `dotnet build PostmanClone.sln` clean; `dotnet test PostmanClone.sln` green (with `TEST_POSTGRES` set for DB-backed tests).
- [ ] New tests assert real behaviour; scoping tests exist for every new id endpoint.
- [ ] CI green on the PR; PR squash-merged by you; `main` CI checked.
- [ ] Docs/AGENTS/AppHost registrations updated where your WP adds something they list.
- [ ] Contract amendments (if any) appended to CONTRACTS.md; ADRs written with the reserved number.

## 7. Failure handling

- Hit a usage limit or a tool outage: whatever you have is already pushed (§3). On resume, `git status`, continue from the branch, do not restart.
- Blocked by a missing dependency WP or a contract problem after one honest attempt to work around it inside scope: push wip, open **no** PR if nothing is
  reviewable, and end with `BLOCKED: <reason>` as the first line of your final message.
- A test you cannot make pass for an environmental reason (no Docker, no network): mark it `Skip` with the reason and say so under "Not verified".

## 8. Contract changes

`CONTRACTS.md` is frozen. Allowed without asking: append an **optional** field or a new endpoint under `## Amendments` (`### A<n> (WP<nn>): …`),
in your own PR, and implement it. Not allowed: renaming, removing, tightening a limit, changing a status code. If you believe one is needed:
`BLOCKED: contract — <what and why>` and stop.

## 9. Final message (your last message, plain text, no questions)

```
WP<NN> <slug>: MERGED | NOT MERGED | BLOCKED
PR: <url>   merge commit: <sha>   main CI after merge: green | red | pending
Ran: <commands that passed, counts of tests>
Not verified: <everything you could not run and why>
Contract amendments: <none | A<n>…>   ADRs: <numbers>
Owner actions / stale branches: <list or none>
Follow-ups noticed (not done): <list or none>
```
