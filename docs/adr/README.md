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
| [0007](0007-home-dashboards-blocks-and-groups.md) | Home dashboards on a shared block and group model | Accepted |

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
