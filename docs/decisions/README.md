# Architecture decision records

Each decision that shapes the product, the boundaries or the security model is recorded here, with
its context, the options considered, the decision, the consequences and the condition under which it
should be revisited. A decision is changed by a new record that supersedes the old one, not by editing
the old one in place.

Numbers are reserved per work package in
[`docs/implementation-plan/WORK-PACKAGES.md`](../implementation-plan/WORK-PACKAGES.md). WP00 writes
0001–0009.

| ADR | Decision | Status |
|---|---|---|
| [0001](0001-product-scope.md) | Product scope and non-goals | Accepted |
| [0002](0002-blazor-web-app-over-nextjs.md) | Blazor Web App (interactive server) over Next.js; WebAssembly rejected | Accepted |
| [0003](0003-service-boundaries.md) | Three domain services and a Web, one database per service | Accepted |
| [0004](0004-authservice-identity-and-workspace.md) | authservice is the only IdP; a workspace is one organization | Accepted |
| [0005](0005-bff-session.md) | Backend-for-frontend session: opaque HttpOnly cookie, tokens on the server | Accepted |
| [0006](0006-secret-variables.md) | Secret variables: encrypted at rest, referenced by name, masked everywhere | Accepted |
| [0007](0007-deployment-out-of-scope.md) | Deployment is out of scope for this build | Accepted |
| [0008](0008-single-instance-web.md) | The Web keeps sessions in server memory and runs as one instance | Accepted |
| [0009](0009-dependency-budget.md) | Dependency budget | Accepted |
| [0010](0010-authservice-pin-and-local-run.md) | authservice pinned to v0.3.4; run from its tagged source when there is no Docker | Accepted |
| [0011](0011-sender-threat-model.md) | Sender threat model and the SSRF guard: resolve and check at connect, dial only checked addresses | Accepted |
| [0012](0012-variable-semantics.md) | Variable semantics and request assembly: single pass, encoded-form masking, header and auth rules | Accepted |
| [0013](0013-circuit-lifetime-and-session-store.md) | Circuit lifetime and session store: an opaque cookie, eight idle hours, seven absolute days, five-minute revalidation | Accepted |
| [0014](0014-execution-limits-and-redirects.md) | Execution limits, redirects and the send path: sender-followed hops, capped and decoded reads, masked results | Accepted |
| [0015](0015-history-retention-and-masking.md) | History retention and masking: the template with its references, masked in every form, 200 per user and workspace | Accepted |

Reserved for later work packages (see WORK-PACKAGES.md):
0016 import threat model,
0017 builder state model, 0018 E2E scope and charter, 0019 review outcomes. 0020–0029 are spare.
