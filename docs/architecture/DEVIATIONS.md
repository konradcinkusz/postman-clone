# Deviations from architecture-standards

This register records every place where postman-clone departs from the reference architecture
(P1–P15 and the guides it cites), with the reason and the date. A deviation is allowed only when it
is written here first; an undocumented departure is a review finding.

Columns: the principle or guide departed from, what this repository does instead, the reason, and the
date it was recorded.

| Date | Principle or guide | Deviation | Reason |
|---|---|---|---|
| 2026-10-26 | P2 example call `AddDatabaseContext<TContext>(configuration, "mydb", "MyDbInMemory")` | `AddDatabaseContext<TContext>(builder, connectionName)`. The InMemory path names its in-memory database after the connection name; there is no second name argument | The builder carries the configuration, and the second name gave the InMemory path nothing the tests needed. WP01 |

None yet.
