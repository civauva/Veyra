# Tests

Mirrors `src/backend`: one test project per module, named `<Project>.Tests`.

- Unit tests: xUnit, no external services.
- Integration tests: Testcontainers for PostgreSQL; run against the real schema and migrations. The alert email
  tests send to a Testcontainers Mailpit server (`axllent/mailpit`), a real SMTP server whose API shows what it
  received.
- Frontend tests live next to the components in `src/frontend`.
- `Veyra.Testing` holds shared test infrastructure: the PostgreSQL container fixture, which also applies real
  migrations to throwaway databases.
