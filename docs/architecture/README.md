# Architecture

Design documents describing how Veyra works internally.

Written pages:

- [`home-dashboards.md`](home-dashboards.md) — blocks, groupings, the shared shell, and the Forecast and City home
  dashboards (Forecast on log data implemented; City and groupings are design).
- [`alerting.md`](alerting.md) — the built-in rules (error rate, latency, disk space, disk filling), alerts on
  services and hosts, the alert lifecycle and the Alerts screen, notifications to webhooks and email, and their
  settings.
- [`monitoring-scope.md`](monitoring-scope.md) — what Veyra monitors, how the data arrives, supported versions,
  and where data is kept.
- [`tech-stack.md`](tech-stack.md) — the technologies, libraries, and tools in use.
- [`ingestion.md`](ingestion.md) — OTLP receivers, mapping, the bounded queue, backpressure, and when data can be
  lost.

Planned pages:

- `overview.md` — modules, boundaries, and the request/ingestion flow.
- `data-model.md` — PostgreSQL schema for logs, metrics, traces, and incidents; retention strategy.
- `query.md` — search, filtering, and cross-signal correlation.
