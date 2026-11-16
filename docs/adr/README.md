# Architecture Decision Records

An ADR captures one significant decision: the situation, the choice, the alternatives, and the consequences.
ADRs are numbered sequentially and are immutable once accepted. To change a decision, write a new ADR that
supersedes the old one and update both status lines.

| # | Title | Status |
| --- | --- | --- |
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions | Accepted |
| [0002](0002-backend-aspnet-core-net10.md) | Backend: ASP.NET Core WebAPI on .NET 10, modular monolith | Accepted |
| [0003](0003-storage-postgresql.md) | Storage: PostgreSQL | Accepted |
| [0004](0004-frontend-react-typescript.md) | Frontend: React + TypeScript with Vite | Accepted |
| [0005](0005-license-apache-2.md) | License: Apache 2.0 | Accepted |
| [0006](0006-deployment-docker-compose.md) | Deployment: Docker Compose with Veyra and PostgreSQL | Accepted |
| [0007](0007-home-dashboards-blocks-and-groups.md) | Home dashboards on a shared block and group model | Accepted; its top bar superseded by 0013 |
| [0008](0008-authentication-roles-and-ingestion-keys.md) | Authentication, roles, and ingestion keys | Accepted |
| [0009](0009-alert-notifications.md) | Alert notifications: channels, a delivery outbox, and the webhook payload | Accepted |
| [0010](0010-metrics-storage.md) | Metrics storage: series and points in PostgreSQL | Accepted |
| [0011](0011-two-factor-sign-in.md) | Two-factor sign-in, sessions, and sign-in history | Accepted |
| [0012](0012-metric-alert-rules-and-alert-subjects.md) | Metric alert rules, alerts on hosts, and alert history | Accepted |
| [0013](0013-left-sidebar-navigation.md) | Left sidebar navigation | Accepted |
| [0014](0014-trace-storage.md) | Trace storage: spans in PostgreSQL | Accepted |
| [0015](0015-query-speed-budgets-and-counts.md) | Query speed: budgets, pre-counted logs, and metric rollups | Accepted |
| [0016](0016-word-search-by-hour.md) | Word search by hour | Accepted |
| [0017](0017-settings-in-the-admin-ui.md) | Settings in the admin UI | Accepted |
| [0018](0018-databases-and-containers.md) | Databases, containers, and Windows hosts | Accepted; its query events superseded by 0020 |
| [0019](0019-veyra-agent.md) | The Veyra agent: monitoring targets set up from the GUI | Accepted |
| [0020](0020-full-sql-server-monitoring.md) | Full SQL Server monitoring: scope, live view, drill-down, activity store | Accepted |

## Template

```markdown
# NNNN. Title

Date: YYYY-MM-DD
Status: Proposed | Accepted | Superseded by NNNN | Deprecated

## Context

What situation forces this decision. Constraints, requirements, and facts, not opinions.

## Decision

What we chose, in one or two sentences, then the details that matter.

## Alternatives considered

Each option with the main reason it was not chosen.

## Consequences

What becomes easier, what becomes harder, and what we commit to maintaining.
```
