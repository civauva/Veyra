# Architecture

Design documents describing how Veyra works internally. Planned pages:

- `overview.md` — modules, boundaries, and the request/ingestion flow.
- `ingestion.md` — OTLP receivers, normalization, buffering, backpressure, and loss behavior.
- `data-model.md` — PostgreSQL schema for logs, metrics, traces, and incidents; retention strategy.
- `query.md` — search, filtering, and cross-signal correlation.
- `alerting.md` — rule evaluation, incident lifecycle, notifications.
