# Samples

- [`Veyra.Sample.Api/`](Veyra.Sample.Api/Program.cs) — minimal .NET application instrumented only with the
  OpenTelemetry SDK, exporting its logs and its HTTP and runtime metrics to Veyra over OTLP. See `docs/integrations/dotnet.md`.
- [`sqlserver-workload/`](sqlserver-workload/README.md) — keeps a SQL Server busy for a few minutes, with a blocking
  chain, a long query, and deadlocks, so Veyra's SQL Server screen has something to show.
- `telemetry-generator/` — planned: generates synthetic logs, metrics, and traces for local development and demos.
