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

## Build settings

- `global.json` at the repository root pins the .NET 10 SDK.
- `Directory.Build.props` at the repository root applies to `src/`, `tests/`, and `samples/`: `net10.0`, nullable,
  implicit usings, warnings as errors, code-style enforcement, and `latest-recommended` analyzers.
- `Directory.Packages.props` at the repository root holds every package version (central package management).
  Project files reference packages without a version.

## Configuration

Each module binds its options from the `Veyra:<Module>` section (see `Veyra.Core.VeyraOptionNames`) and validates
them at startup. Defaults live in `Veyra.Api/appsettings.json`; secrets such as the PostgreSQL connection string are
supplied through environment variables, for example `Veyra__Storage__ConnectionString`.

## Endpoints

- `GET /healthz` returns `200 OK` when the process is up. Readiness checks will be added with the Storage module.
