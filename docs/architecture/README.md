# Architecture

Design documents describing how Veyra works internally.

Written pages:

- [`home-dashboards.md`](home-dashboards.md) — blocks, groupings, the shared shell, and the Forecast and City home
  dashboards (design, not implemented).
- [`monitoring-scope.md`](monitoring-scope.md) — what Veyra monitors, how the data arrives, supported versions,
  and where data is kept.
- [`tech-stack.md`](tech-stack.md) — the technologies, libraries, and tools in use.

Planned pages:

- `overview.md` — modules, boundaries, and the request/ingestion flow.
- `ingestion.md` — OTLP receivers, normalization, buffering, backpressure, and loss behavior.
- `data-model.md` — PostgreSQL schema for logs, metrics, traces, and incidents; retention strategy.
- `query.md` — search, filtering, and cross-signal correlation.
- `alerting.md` — rule evaluation, incident lifecycle, notifications.
