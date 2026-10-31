# Deviations from architecture-standards

This register records every place where postman-clone departs from the reference architecture
(P1–P15 and the guides it cites), with the reason and the date. A deviation is allowed only when it
is written here first; an undocumented departure is a review finding.

Columns: the principle or guide departed from, what this repository does instead, the reason, and the
date it was recorded.

| Date | Principle or guide | Deviation | Reason |
|---|---|---|---|
| 2026-10-26 | P2 example call `AddDatabaseContext<TContext>(configuration, "mydb", "MyDbInMemory")` | `AddDatabaseContext<TContext>(builder, connectionName)`. The InMemory path names its in-memory database after the connection name; there is no second name argument | The builder carries the configuration, and the second name gave the InMemory path nothing the tests needed. WP01 |
| 2026-10-30 | BLAZOR-WEB-APP §7 "cookie authentication on the host" | A custom authentication handler (`SessionAuthenticationHandler`) reads a cookie that holds only a random session id. The ticket and the token pair stay in `ServerSessionStore` on the server, not in a Data Protection cookie ticket | The cookie must carry nothing but a random id (ADR-0005). The cookie middleware's ticket is an encrypted envelope around the principal, not an id. ADR-0013. WP12 |

