# Backend

ASP.NET Core WebAPI on .NET 10, organized as a modular monolith.

| Project | Responsibility | May reference |
| --- | --- | --- |
| `Veyra.Core` | Shared domain types, contracts, abstractions. No infrastructure. | nothing |
| `Veyra.Ingestion` | OTLP receivers, normalization, filtering, buffering, backpressure. | Core |
| `Veyra.Storage` | PostgreSQL persistence, migrations, retention enforcement. | Core |
| `Veyra.Query` | Search, filtering, and correlation across signals. | Core |
| `Veyra.Alerting` | Rule evaluation, incidents, email/webhook notifications. | Core |
| `Veyra.Api` | Host process: HTTP endpoints, auth, DI wiring, serves the frontend. | all modules |

Rules:

- Modules communicate through abstractions defined in `Veyra.Core`, never by referencing each other.
- `Veyra.Api` is the only composition root.
- Each module owns its own configuration section and its own database schema objects.
