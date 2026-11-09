# Tech stack

Status: agreed. A choice that is hard to reverse has an ADR in
[`docs/adr/`](../adr/README.md), and changing it needs a new ADR that supersedes the old one.

## Decided by ADR

| Area | Choice | ADR |
| --- | --- | --- |
| Backend | .NET 10 (LTS) Web API, modular monolith | [0002](../adr/0002-backend-aspnet-core-net10.md) |
| Storage | PostgreSQL, time-partitioned tables | [0003](../adr/0003-storage-postgresql.md) |
| Frontend | React, TypeScript, and Vite, served by the API | [0004](../adr/0004-frontend-react-typescript.md) |
| License | Apache License 2.0 | [0005](../adr/0005-license-apache-2.md) |
| Deployment | Docker Compose: a Veyra and a PostgreSQL container | [0006](../adr/0006-deployment-docker-compose.md) |

## Backend

- **API style.** Controllers for the REST API, one controller per resource, with an XML comment on every action.
  Each module keeps its controllers in its own assembly and routes them under `/api/v1`. `Veyra.Api` adds them as
  application parts, so it stays the only composition root. The OTLP endpoints are not part of the REST API and
  follow the OTLP specification instead.
- **OTLP ingestion.** gRPC on port 4317 and HTTP on port 4318, with protobuf and JSON payloads, as the OTLP
  specification defines.
- **Database access.** Npgsql with plain, parameterised SQL. Writes use binary `COPY` for bulk inserts. There is no
  ORM, because telemetry volume and partitioned tables need full control over the SQL.
- **Migrations.** Plain SQL scripts that only move forward. Each module owns the scripts for its own schema, and
  `Veyra.Storage` runs them. **Migrations run automatically at startup**, inside a transaction and under a lock,
  so two instances never migrate at the same time.
- **Ingestion buffering.** Bounded `System.Threading.Channels` queues. When a queue is full, Veyra answers with the
  OTLP retry status, so senders back off instead of Veyra running out of memory.
- **Configuration.** Options classes per module, validated at startup. Secrets come only from environment
  variables, documented in `deploy/.env.example`.
- **Time.** `TimeProvider` everywhere, so tests control the clock. Timestamps are `DateTimeOffset` in UTC.
- **Logging.** `ILogger<T>` with structured templates, and source-generated `LoggerMessage` methods on hot paths.
- **Errors.** Problem details (RFC 9457) for every error response.

## Security

- **Users.** Both local accounts and OpenID Connect sign-in, with Microsoft Entra ID as the first tested provider.
- **Ingestion.** API keys per sender. Browser apps get separate write-only keys bound to their allowed origins.
- **TLS.** Terminated by a reverse proxy. Caddy is the documented default because it obtains certificates
  automatically. nginx and Traefik examples follow.
- **ADRs.** Authentication, roles, and ingestion keys are decided in
  [ADR 0008](../adr/0008-authentication-roles-and-ingestion-keys.md). Browser ingestion still needs its own ADR.

## Frontend

- **Framework.** React with strict TypeScript, function components, and hooks, as ADR 0004 decides.
- **Visuals.** Home dashboards are hand-drawn SVG, so they do not depend on a chart library.
- **Tooling.** ESLint and Prettier for linting and formatting. Vitest and Testing Library for unit and component
  tests. Playwright for end-to-end tests, run at desktop, tablet, and phone viewports against the built app, with
  the REST API answered by the tests.
- **Log Explorer.** React Router keeps the filters in the URL. TanStack Virtual renders only the rows in view, so
  thousands of loaded records stay fast. The histogram is plain HTML and CSS, so there is still no chart library.
- **Documentation.** Every exported component, hook, function, and type has a TSDoc comment. See
  [`coding-standards.md`](../guides/coding-standards.md).

## Testing

- **Backend.** xUnit with its built-in assertions. FluentAssertions is not used, because version 8 and later
  need a paid licence for commercial use.
- **Integration tests.** Testcontainers for PostgreSQL, and `WebApplicationFactory` for the API.
- **Golden payloads.** Recorded OTLP payloads from real SDKs and Collectors are kept as test fixtures.

## Collection and operations

- **Collector.** The OpenTelemetry Collector Contrib distribution, pinned to one tested version for all
  configurations in `integrations/`.
- **CI.** GitHub Actions (`.github/workflows/ci.yml`) restores, builds in Release, runs all tests, and checks
  formatting on every push and pull request. A frontend job lints, checks formatting, runs the unit and
  end-to-end tests, and builds the app.
- **Images.** One multi-stage `Dockerfile` builds the API and the frontend into a single image.
