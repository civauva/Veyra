# AGENTS.md

Instructions for any AI agent or automated tool working in this repository. Human contributors should read it too.
When this file and a tool's default behaviour disagree, this file wins.

## 1. What Veyra is

Veyra is a self-hosted, open-source observability platform: logs, metrics, traces, incidents, and alerts for
applications, hosts, containers, cloud resources, databases, and background jobs. It receives telemetry over
OTLP (OpenTelemetry Protocol) and does not require Grafana, Loki, Prometheus, Tempo, Seq, Kafka, or Redis.

Status: pre-alpha. The current milestone is v0.1, a useful logging foundation. See `README.md` for the roadmap.

## 2. Decisions that are already made

Do not re-open these without a new ADR (see section 7).

| Topic | Decision | ADR |
| --- | --- | --- |
| Backend | ASP.NET Core WebAPI on .NET 10 (LTS), modular monolith | `docs/adr/0002` |
| Storage | PostgreSQL | `docs/adr/0003` |
| Frontend | React + TypeScript, Vite, served as static files by the API | `docs/adr/0004` |
| License | Apache License 2.0 | `docs/adr/0005` |
| Deployment | Docker Compose: Veyra container + PostgreSQL container | `docs/adr/0006` |
| Ingestion | OTLP only. Platform and database collection is done by the OpenTelemetry Collector, not by Veyra | `README.md` |
| Instrumentation | Applications use the standard OpenTelemetry SDKs. There is no Veyra-specific SDK | `README.md` |
| Home dashboards | Shared block and group model; Forecast (default) and City homes | `docs/adr/0007` |
| Security | Ingestion keys for senders; local or OIDC sign-in with cookies; Viewer, Editor, Admin | `docs/adr/0008` |
| Notifications | Channels in the database, a delivery outbox, encrypted webhook URLs, one webhook document | `docs/adr/0009` |
| Metrics | Series and daily-partitioned points in PostgreSQL; rates and percentiles computed when read | `docs/adr/0010` |
| Sign-in | Authenticator-app second factor, recovery codes, session rows, 90 days of sign-in history | `docs/adr/0011` |
| Alert rules | Built-in error-rate, latency, disk-space, disk-filling rules; alerts on services and hosts; 90 days | `docs/adr/0012` |
| Monitoring scope | Targets, supported versions, and retention | `docs/architecture/monitoring-scope.md` |
| Tech stack | Libraries and tools beyond the ADRs | `docs/architecture/tech-stack.md` |

## 3. Repository map

```
AGENTS.md          this file
README.md          project vision and roadmap
LICENSE            Apache 2.0
.github/           CI workflows, issue and PR templates
docs/              documentation (adr/, architecture/, guides/, integrations/, api/)
src/               Veyra.sln, backend/ (one project per module), frontend/ (React app)
tests/             test projects mirroring src/backend
integrations/      OpenTelemetry Collector configs per target (azure, linux, docker, postgres, sqlserver)
samples/           instrumented sample app and telemetry generator
deploy/            Dockerfile, docker-compose.yml, reverse-proxy examples
scripts/           build, test, and local-stack helpers
```

Every folder has a `README.md` stating its purpose. Read it before adding files there. Do not create new top-level
folders without updating this map.

## 4. Architecture rules

- Modular monolith. Modules: `Core`, `Ingestion`, `Storage`, `Query`, `Alerting`, and `Access` (ADR 0008). Host:
  `Api`.
- Modules reference `Veyra.Core` only. Modules never reference each other. `Veyra.Api` is the only composition root.
- Cross-module communication goes through interfaces or messages defined in `Veyra.Core`.
- Each module owns its configuration section, its database schema objects, and its migrations.
- Ingestion must be bounded: queues have limits, backpressure is explicit, and telemetry-loss behaviour is documented.
- Nothing in the backend may depend on a Veyra-specific client SDK existing in monitored applications.
- The frontend talks to the backend only through the public REST API. No server-rendered pages.
- Secrets never go in code, config files, or documentation. Use environment variables and document them in
  `deploy/.env.example`.

## 5. Coding conventions

### C# / .NET

- Target `net10.0`. `Nullable` and `ImplicitUsings` enabled. `TreatWarningsAsErrors` on.
- File-scoped namespaces. One public type per file. `record` for immutable data.
- Async methods end in `Async` and accept a `CancellationToken`.
- Use `ILogger<T>` with structured message templates. Never string-interpolate log messages.
- Follow `.editorconfig`. Run `dotnet format` before committing.
- Write code for human readers: blank lines between members and logical steps, braces always, one argument per
  line when a call wraps. See `docs/guides/coding-standards.md`.
- XML comments on every public and protected member, and on internal members whose purpose is not obvious.
  Comments explain why, not what.
- Tests: xUnit. Unit tests have no I/O. Integration tests use Testcontainers for PostgreSQL.
- Test names: `Method_Scenario_ExpectedResult`.

### TypeScript / React

- Strict TypeScript. Function components and hooks only. No `any` without a comment explaining why.
- Co-locate component, styles, and tests. Follow ESLint and Prettier configs in `src/frontend`.
- TSDoc comments on every exported component, hook, function, and type, with props documented field by field.
- Every screen must be responsive on desktop, tablet, and phone, work in light and dark themes, and be keyboard
  accessible.

### General

- Markdown for all documentation. Mermaid for diagrams.
- Line endings LF. UTF-8. See `.gitattributes`.
- Prefer small, focused changes. Do not reformat files you are not otherwise changing.

## 6. Commands

To be kept current as the solution grows. Run from the repository root.

```
dotnet build src/Veyra.sln
dotnet test  src/Veyra.sln
dotnet format src/Veyra.sln --verify-no-changes
cd src/frontend && npm ci && npm run lint && npm test && npm run build && npm run test:e2e
docker compose -f deploy/docker-compose.yml up
```

## 7. Decisions and documentation

- Any choice that is hard to reverse (framework, storage, protocol, auth model, public API shape) needs an ADR in
  `docs/adr/`. Use the template in `docs/adr/README.md`. Number sequentially. Never edit an accepted ADR;
  supersede it.
- Behavioural changes update the relevant page in `docs/`. A feature is not done until its documentation is.
- Keep `README.md` truthful about what exists. Do not describe unreleased features as available.

## 8. Git rules

- Branch from `main`. Branch names: `feature/<short-name>`, `fix/<short-name>`, `docs/<short-name>`.
- Every commit belongs to a Throughly work item. The commit title is `#<work item key>: type(scope): summary`,
  for example `#VEY-42: feat(ingestion): accept OTLP logs over HTTP`. The rest follows Conventional Commits:
  imperative mood, 72 characters max for the summary, body explains why. Types: `feat`, `fix`, `docs`, `refactor`,
  `test`, `build`, `ci`, `chore`.
- **No trace of AI tooling in git history.** Commit messages, commit trailers, and pull request descriptions must
  not contain `Co-Authored-By` lines naming an AI, "Generated with ...", "AI-assisted", or similar. Write commits
  in the project's own voice as the author would.
- Do not commit tool-specific files or folders (`.claude/`, `.cursor/`, `CLAUDE.md`, `.aider*`, and similar).
  They are ignored in `.gitignore`; keep them that way.
- Do not commit, push, tag, or open pull requests unless explicitly asked to.
- Pull requests are squash-merged. The pull request title is the commit title, including the work item key.
- Never rewrite history on `main`. Never force-push shared branches.

## 9. Working style for agents

- Read this file, then the `README.md` of the folder you are working in, before changing anything.
- Follow the existing structure. If the structure is wrong, say so and propose a change rather than working around it.
- When a task is ambiguous in a way that changes the result materially, ask. Otherwise decide, state the assumption,
  and continue.
- Work test-driven: a failing test first, then the code, then refactor. A bug fix starts with a failing test.
- For a longer session, set a plan in Throughly on the work item first, and report each step as it starts and ends.
- Audit every work item before closing it, using the checklist in `docs/guides/methodology.md`, and post the result
  on the work item.
- Finish the whole task, including tests and documentation. Report honestly what was not done or not verified.
- Do not add dependencies without stating why the standard library or an existing dependency is insufficient.
- Do not add features, abstractions, or configuration beyond what the task requires.

## 10. Definition of done

A change is done when:

1. It builds with zero warnings, all tests pass, and CI is green.
2. New behaviour has tests at the appropriate level.
3. Documentation and, if applicable, an ADR are updated.
4. `README.md`s and this file still describe reality.
5. No secrets, no generated artifacts, and no tool-specific files are included.
6. The audit at the end of the work item has passed.
