# 0010. Metrics storage: series and points in PostgreSQL

Date: 2026-11-10
Status: Accepted

## Context

v0.1 needs basic application and host metrics (README roadmap). They arrive over OTLP from the standard SDKs and
from the OpenTelemetry Collector's `hostmetrics` receiver. Storage is PostgreSQL (ADR 0003), with no extra services.

What arrives:

- **Kinds.** Gauges (memory in use, load), sums (CPU time, bytes sent; usually cumulative, so the value only grows
  until the process restarts), and histograms (request duration). Exponential histograms and summaries exist but are
  rare in the SDKs and Collector receivers Veyra targets.
- **Volume.** A host with `hostmetrics` sends about 100 series every 30 seconds: about 290,000 points a day. A .NET
  service sends 50 to 200 series. Small next to logs, but it never stops.
- **Cardinality.** Each distinct set of attributes is a new series. One badly chosen attribute, such as a user ID on
  a request counter, can create millions of series.
- **Questions people ask.** "How busy is this host now and over the last hours?", "Is the disk filling up?", "How
  slow is this service, and how many requests fail?" Rates of counters and percentiles of histograms, over a time
  window, per host or per service.

## Decision

Metrics are stored as **series** and **points** in the Storage module's schema:

- `storage.metric_series` holds each series once: metric name, kind, unit, description, service, environment, host,
  and the resource and point attributes as JSON, identified by a hash of all of them. New series are created as they
  first arrive.
- `storage.metric_point` holds the points, split into daily partitions like logs: series ID, time, start time, and the
  value; for histograms count, sum, min, max, bucket bounds, and bucket counts. A point row is narrow, so a year of a
  host's metrics stays in the low gigabytes.
- **Cumulative sums are stored as they arrive.** Rates are computed when read, from the difference between points of
  the same series, treating a lower value or a new start time as a restart. Ingestion stays a plain write, and no
  state is lost when Veyra restarts.
- **Histograms keep their buckets.** Percentiles are estimated from the buckets when read, the same way Prometheus
  does it, so "p95 latency" needs no raw durations.
- **Exponential histograms, summaries, and delta histograms are refused** with OTLP partial success, which tells the
  sender how many points were rejected and why. The SDKs and the Collector send cumulative explicit-bucket histograms
  by default. The others are added when a target needs them.
- **Retention.** Metrics keep 30 days by default (`Veyra:Storage:MetricRetentionDays`), dropped by whole partitions
  like logs. Series without points for longer than the retention are deleted.
- **Series limit.** At most 50,000 series by default (`Veyra:Storage:MaxMetricSeries`). Points of a new series beyond
  the limit are dropped when they are written, with a warning in Veyra's log that names the metric, so one bad
  attribute cannot fill the disk. The sender is not told: it got its answer when the points were queued. Existing
  series keep working.
- **Ingestion** uses its own bounded queue, the same backpressure as logs (`503` / `UNAVAILABLE` when full), and the
  same ingestion keys and environment limits (ADR 0008).

## Alternatives considered

- **One wide table without a series table.** Simpler to write, but every point repeats the metric name and all
  attributes as JSON: several times the size, and every query filters JSON.
- **TimescaleDB or another time-series extension.** Compression and continuous aggregates, but an extension the
  standard `postgres` image does not have, against ADR 0003 and ADR 0006.
- **A separate time-series database (Prometheus, VictoriaMetrics).** Built for this, but another service to run,
  back up, and secure, against Veyra's purpose of needing only PostgreSQL.
- **Converting cumulative sums to deltas at ingestion.** Smaller reads, but it needs the last value of every series
  in memory, loses it on restart, and gets it wrong when two Veyra instances receive the same series.
- **Downsampling old points.** Less disk for long retention, but more moving parts. With 30 days and the volumes
  above it is not needed yet; it can be added with longer retention.

## Consequences

- Metric queries are SQL over two tables, with indexes on series by name, service, and host, and on points by series
  and time.
- Rates and percentiles cost CPU when read. Charts ask for at most a few hundred points per series, so this stays
  small; dashboards over thousands of series would need pre-aggregation later.
- The log storage budget (`Veyra:Storage:MaxLogStorageGigabytes`) does not cover metrics. Metrics are bounded by
  retention and the series limit; the home shows how much space they use.
- Attributes that change every point (a request ID) create a series each and hit the limit; the warning names the
  metric so the sender can be fixed.
