# Backend

ASP.NET Core WebAPI on .NET 10, organized as a modular monolith.

| Project | Responsibility | May reference |
| --- | --- | --- |
| `Veyra.Core` | Shared domain types, contracts, abstractions. No infrastructure. | nothing |
| `Veyra.Ingestion` | OTLP receivers, normalization, filtering, buffering, backpressure. | Core |
| `Veyra.Storage` | PostgreSQL persistence, migrations, retention enforcement. | Core |
| `Veyra.Query` | Search, filtering, and correlation across signals. | Core |
| `Veyra.Alerting` | Rule evaluation, incidents, email/webhook notifications. | Core |
| `Veyra.Access` | Users, roles, sessions, ingestion keys, first-run setup (ADR 0008). | Core |
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

- `GET /healthz` returns `200 OK` when the process is up. It never checks dependencies.
- `GET /readyz` returns `200 OK` when Veyra can also reach PostgreSQL, otherwise `503`.
- `/api/v1/setup`, `/api/v1/session`, `/api/v1/account`, `/api/v1/users`, and `/api/v1/ingestion-keys` belong to
  `Veyra.Access`, with two-factor sign-in, sessions, and sign-in history (ADR 0011).
  Everything else requires a signed-in user; the anonymous endpoints are listed in `AnonymousEndpointTests`.
- `GET /api/v1/logs` and `/api/v1/metrics` belong to `Veyra.Query` and read stored logs and metrics.
- OTLP: `POST /v1/logs`, `POST /v1/metrics`, and the gRPC `LogsService/Export` and `MetricsService/Export` belong
  to `Veyra.Ingestion` and check ingestion keys.
  Kestrel listens on 8080 (API and OTLP/HTTP), 4317 (OTLP/gRPC), and 4318 (OTLP/HTTP). See `docs/api/`.

## Database migrations

- Each module embeds forward-only SQL scripts named `V0001__create_log_record.sql` in its `Migrations` folder and
  registers an `ISchemaMigrationSource` (`Veyra.Core.Schema`).
- At startup, before it listens, Veyra applies all pending scripts in one transaction under a PostgreSQL advisory
  lock, and records them in `storage.schema_migration` with a checksum.
- Editing a script that was already applied stops startup with an error. Add a new script instead.
