# 0003. Storage: PostgreSQL

Date: 2026-11-07
Status: Accepted

## Context

Veyra must persist logs first, then metrics and traces, with time-range queries, attribute filtering, full-text search,
and retention enforcement. The README called for a benchmark-driven choice. The owner already operates PostgreSQL and
prefers it.

## Decision

Use PostgreSQL as the only storage backend for v0.1. Logs use a time-partitioned table with JSONB attributes and
GIN indexes for attribute and full-text search. Retention is enforced by dropping partitions. Schema migrations are
owned by `Veyra.Storage`.

## Alternatives considered

- ClickHouse: excellent for telemetry analytics, but a second unfamiliar system to operate and back up.
- SQLite embedded: truly single-container, but weak concurrency under sustained ingestion and no partition dropping.
- Elasticsearch/OpenSearch: strong search, heavy to run, exactly the kind of dependency Veyra wants to avoid.
- Object storage with a columnar format: strong long-term option for metrics and traces; too much to build for v0.1.

## Consequences

- Deployment needs a PostgreSQL instance (see ADR 0006).
- TimescaleDB or a columnar store can be introduced later behind the `Veyra.Storage` abstraction if metrics volume
  demands it; that will be a new ADR.
- Benchmarks are still needed to validate partition size, index strategy, and ingestion throughput targets.
