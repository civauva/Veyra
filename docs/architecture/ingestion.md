# Ingestion

How a log record or a metric point travels from an application into PostgreSQL, and when it can be lost.

Status: logs and metrics work end to end (metrics: ADR 0010). Traces follow the same path later.

```mermaid
flowchart LR
  SDK[OTel SDK or Collector] -- "OTLP/gRPC :4317" --> GRPC[gRPC service]
  SDK -- "OTLP/HTTP :4318, /v1/logs or /v1/metrics" --> HTTP[HTTP endpoint]
  GRPC --> KEY{Ingestion key valid?}
  HTTP --> KEY
  KEY -- no --> R401[401 / UNAUTHENTICATED]
  KEY -- yes --> MAP[Map OTLP to LogRecord<br/>apply the key's environments]
  MAP --> Q{Room in the queue?}
  Q -- no --> R503[503 + Retry-After / UNAVAILABLE<br/>sender retries]
  Q -- yes --> OK[200, partial success if refused] --> QUEUE[(Bounded queue<br/>in memory)]
  QUEUE --> W[Writer: batches up to 5,000<br/>binary COPY, retries]
  W --> PG[(storage.log_record<br/>daily partitions)]
  PG --> API[GET /api/v1/logs]
```

## Receiving

- **Transports.** OTLP/gRPC on port 4317 and OTLP/HTTP on port 4318, as the OTLP specification defines them. The
  HTTP endpoints `/v1/logs` and `/v1/metrics` also answer on the main port 8080, so remote senders can reach it through Caddy with
  TLS. Caddy forwards gRPC calls to port 4317 as well. See [`docs/api/otlp.md`](../api/otlp.md).
- **Formats.** Protobuf and OTLP/JSON, optionally gzipped. Bodies are limited to 16 MB, also after decompression.
- **Keys.** Every request needs an ingestion key in `Authorization: Bearer` (ADR 0008). A key limited to some
  environments has the records for other environments refused, and the sender learns about it through OTLP partial
  success.
- **Wrong keys.** After 20 wrong keys from one address in a minute, that address gets `429` or `RESOURCE_EXHAUSTED`
  for the rest of the minute, before any database lookup.

## Mapping

`OtlpLogMapper` turns each OTLP record into one `LogRecord`:

- **Time.** The record's time, else its observed time, else the time Veyra received it.
- **Service and environment.** From the resource attributes `service.name` (default `unknown_service`) and
  `deployment.environment.name`, or the older `deployment.environment`.
- **Values.** Attribute and body values that are not strings become text; arrays and maps become JSON. The
  instrumentation scope name is kept as the attribute `otel.scope.name`.
- **IDs.** Trace and span IDs become lower-case hex; all-zero IDs mean none.

## Buffering and backpressure

- **Bounded queue.** Accepted records wait in an in-memory queue limited to `Veyra:Ingestion:MaxQueueLength` records,
  10,000 by default. A record keeps its place until it is written, not just read.
- **Backpressure.** When a request does not fit, Veyra answers `503` with `Retry-After: 1` over HTTP and `UNAVAILABLE`
  over gRPC. OTLP senders retry those with back-off, so a slow or unavailable database slows senders down instead of
  exhausting Veyra's memory.
- **Writing.** One background writer takes everything waiting, up to 5,000 records, and stores it with one binary
  `COPY`. A failed write is retried with pauses that double up to 30 seconds. The batch is never dropped while
  Veyra runs.

## Metrics

Metrics take the same path as logs, with the same keys, environment limits, and backpressure, but their own queue
and writer (ADR 0010).

- **Mapping.** `OtlpMetricMapper` turns each OTLP data point into one `MetricPoint`: gauges, sums with their
  temporality and whether they only grow, and explicit-bucket histograms with their bounds and counts. Service,
  environment, and `host.name` come from the resource, as for logs.
- **Refused.** Exponential histograms, summaries, and delta histograms are refused and reported through OTLP partial
  success, with the reason. So are points for an environment the key does not allow.
- **Queue.** `Veyra:Ingestion:MaxMetricQueueLength` points, 50,000 by default. A request that does not fit gets
  `503` or `UNAVAILABLE`, as for logs.
- **Writing.** One background writer takes up to 10,000 points, finds or creates their series, and stores the points
  with one binary `COPY`, retrying like the log writer.
- **Series limit.** Each distinct set of name and attributes is one series in `storage.metric_series`. At most
  `Veyra:Storage:MaxMetricSeries` (50,000) exist; points of new series beyond it are dropped when written, with a
  warning in Veyra's log that names the metric. Known series keep arriving.

## When data can be lost

Veyra answers success once records are queued, before they are written. That keeps senders fast, and it means:

- **Crash or kill.** Records in the queue are lost if the process dies. At most `MaxQueueLength` records are at
  risk, usually far fewer.
- **Stop while the database is down.** On a normal stop, Veyra keeps retrying the current batch until the host stops
  it, then logs how many records it gives up.
- **Refused records.** Records and points refused for their environment, and metric points of a kind Veyra does not
  store, are dropped by design, and reported to the sender.
- **Too many series.** Points of new series beyond the series limit are dropped and logged, but not reported to the
  sender, which got its answer when they were queued.
- **Not lost: wrong clocks.** Records with a timestamp far in the past or future land in the default partition and
  are kept and searchable.

## Storage

- **Table.** `storage.log_record`, partitioned by day in UTC, with indexes for time, service, trace, word search,
  and attributes. See the migration `V0001__create_log_record.sql` in `Veyra.Storage`.
- **Partitions.** Created at startup and every hour, from yesterday to two days ahead.
- **Retention.** Every hour, daily partitions older than `LogRetentionDays` (14) are dropped, and old rows are deleted
  from the default partition. With `MaxLogStorageGigabytes` set, the oldest days are also dropped while log storage
  is over that budget, never today.
- **Metrics.** `storage.metric_series` holds each series once; `storage.metric_point` holds the points in daily
  partitions, created and dropped like the log partitions, after `MetricRetentionDays` (30). Series without points
  for that long are deleted. See the migration `V0002__create_metrics.sql`. Rates of counters and percentiles of
  histograms are computed when read, in `Veyra.Query`.
- **Search.** Newest first with keyset paging, through `ILogSearch` in `Veyra.Core`, served by
  `GET /api/v1/logs` in `Veyra.Query`.
