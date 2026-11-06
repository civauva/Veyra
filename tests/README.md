# Tests

Mirrors `src/backend`: one test project per module, named `<Project>.Tests`.

- Unit tests: xUnit, no external services.
- Integration tests: Testcontainers for PostgreSQL; run against the real schema and migrations.
- Frontend tests live next to the components in `src/frontend`.
