# 0014. Trace storage: spans in PostgreSQL

Date: 2026-11-11
Status: Accepted

## Context

Traces are the next milestone after v0.1 (README roadmap: "Distributed tracing and cross-signal correlation"). They
arrive over OTLP from the standard SDKs, which already instrument ASP.NET Core, HttpClient, SqlClient, and Entity
Framework Core (monitoring scope, web APIs), and from the OpenTelemetry Collector. Storage is PostgreSQL (ADR 0003),
with no extra services (ADR 0006).

What arrives and what people ask:

- **Spans.** A trace is a tree of spans that share a trace ID. Each span has a span ID, an optional parent, a name,
  a kind (server, client, internal, producer, consumer), a start and an end, a status (unset, ok, error), attributes,
  events (an exception is an event named `exception` with its type, message, and stack trace), and links to other
  spans. The resource says which service, host, and environment sent it.
- **Spans arrive in pieces.** The spans of one trace come from several services, in separate batches, seconds apart
  and in any order. A trace is never "complete" at a moment Veyra can see.
- **Volume is the largest of the three signals.** A request to an API that calls a database and two other services
  is 5 to 20 spans. An API at 100 requests a second sends about 70 million spans a day; at 500 to 1,000 bytes a
  span that is 35 to 70 GB a day before sampling. Small installations send far less, but the shape is the same: traces
  need sampling, a size budget, and short retention.
- **Logs already carry trace IDs.** `storage.log_record` stores `trace_id` and `span_id` with an index, and the Log
  Explorer filters by them ("Same trace").
- **Speed is a requirement.** Every screen answers at once: opening a trace, listing traces, and filtering them must
  feel instant, also with a week of a busy service stored. A trace tool that makes people wait is not used during an
  incident, which is when it is needed.
- **Questions people ask.** "Why was this request slow?" (one trace, its spans on a timeline, the logs of each
  span), "Which requests of this service failed or were slow in the last hour?" (a list of traces by service,
  environment, name, duration, and status), and, from an alert, "Show me traces like the ones that are failing now."

## Decision

Spans are stored one row each in a daily-partitioned table of the Storage module's schema, and a trace is read by
its ID. There is no table of whole traces.

1. **`storage.span`**, split into daily partitions by start time like logs and metric points:
   - trace ID (16 bytes) and span ID and parent span ID (8 bytes each) as `bytea`, half the size of hex text, so
     their indexes stay in memory; the API shows and accepts them as lowercase hex, as logs store them;
   - name, kind, start time, duration in microseconds, status code and status message;
   - service, environment, and host from the resource, as columns, like logs;
   - the span's attributes and the resource attributes as JSONB;
   - events and links as JSONB, with the exception events kept in full.
2. **Indexes for the questions above:** trace ID (every span of a trace); service and start time; failed spans by
   service and start time (a partial index on status error); attribute filters (GIN with `jsonb_path_ops`, as logs);
   and a partial index on root spans (no parent) by service and start time that **includes** name, duration, status,
   and trace ID, so the trace list is read from the index alone, without touching the table.
3. **The trace list is read from root spans.** A trace's row in the list is its root span: name, service, start, and
   duration. Its span count and whether any span failed are counted when the page is read, for the 50 or so traces
   it shows. A trace whose root span has not arrived, or was not sampled, appears once its root arrives; until then it
   is reached by its ID, from a log record or a link.
4. **Veyra stores what it receives; sampling happens before.** Head sampling in the SDKs and tail sampling in the
   Collector (`tail_sampling`, which can keep every error and every slow trace) are documented, with a recommended
   Collector configuration. Veyra does not sample, so what it shows is exactly what was sent.
5. **Retention and budget.** Traces keep 7 days by default (`Veyra:Storage:TraceRetentionDays`), as the monitoring
   scope proposed, dropped by whole partitions. An optional size budget (`Veyra:Storage:MaxTraceStorageGigabytes`)
   drops the oldest days first, with the same disk guard as logs. The home's log storage figure gains a line for
   traces.
6. **Bounded spans.** A span keeps at most 128 attributes, 128 events, and 32 links, and attribute values up to
   4 KB; the rest is dropped and the span records how much, as the OpenTelemetry limits do. Exception stack traces
   keep up to 32 KB.
7. **Ingestion** gets OTLP traces over gRPC and HTTP (`/v1/traces`), its own bounded queue, and the same
   backpressure (`503` / `UNAVAILABLE` when full), ingestion keys, and environment limits as logs and metrics
   (ADR 0008). A batch is written as it arrives; nothing waits for the rest of a trace.
8. **Speed budgets.** Measured as server time at the 95th percentile, with 7 days of 5 million spans a day stored on
   the reference installation (4 vCPUs, 8 GB of memory, SSD):

   | Request | Budget |
   | --- | --- |
   | One trace by ID, with all its spans | 50 ms |
   | The trace list: 50 traces of a service in the last hour | 100 ms |
   | Filtered trace search over 24 hours (duration, status, name, attributes) | 300 ms |
   | The logs of a trace | 50 ms |
   | Accepting an OTLP batch (queued, not yet written) | 10 ms |
   | A span searchable after it is accepted | 2 s |

   They are met by design, not by tuning later:
   - **Every query is bounded in time**, so PostgreSQL reads only the daily partitions in the window. A trace opened
     from a log record, an alert, or the list carries the time it is known from, and is looked up in that day and
     its neighbours first; only a bare trace ID searches all seven days.
   - **Lists page by keyset** (start time and span ID), never by offset, so the 20th page costs what the first does.
   - **Counts for a page are bounded**: the span count and error flag of the 50 listed traces are 50 index lookups.
   - **Writes are batched** with binary `COPY`, as logs are, so ingestion does not compete with reads row by row.
   - **Proof in tests.** Integration tests seed a large trace set in PostgreSQL (Testcontainers) and check that each
     query uses its index and partition pruning (`EXPLAIN`), so a change that loses an index fails the build. A
     benchmark against the budgets runs before every release, and the reference installation watches Veyra's own
     request durations with the built-in latency rule (ADR 0012).
9. **Correlation.** A trace shows the logs of its spans from `storage.log_record` by trace ID, and a log record
   with a trace ID opens its trace. Alerts on a service link to its failing and slow traces of the alert's window.
   Request rates, error rates, and latency keep coming from metrics (ADR 0010, ADR 0012), because traces are sampled
   and metrics are not.

## Alternatives considered

- **A separate trace store (Tempo, Jaeger, Zipkin) or a columnar database (ClickHouse).** Built for this volume, but
  another service to run, back up, and secure, against ADR 0003 and ADR 0006.
- **One row per trace with its spans as JSON.** Reading a trace is one row, but spans arrive in pieces, so every batch
  would rewrite the row; and questions across traces ("slow database calls of this service") would unpack JSON.
- **A trace summary table kept up to date as spans arrive.** A fast list with span counts and errors, but every span
  of a busy trace updates the same row, which serialises writes, and the summary is wrong until the last span
  arrives. Counting on read for one page of root spans avoids both; a summary can be added if lists get slow.
- **Sampling in Veyra.** Saves disk, but decisions would differ from what the SDKs and the Collector already do, and
  tail sampling needs whole traces in memory, which a modular monolith on one PostgreSQL should not hold.
- **Deriving service metrics from spans.** It would make the HTTP metrics optional, but spans are sampled, so rates
  and percentiles from them would be wrong exactly when sampling is configured well.

## Consequences

- Reading one trace is one index lookup in a few partitions; the trace list is an index-only range scan over root
  spans, so both stay within their budgets whatever the number of child spans.
- The budgets are a commitment: a change that breaks one is a bug, and the tests and the release benchmark catch it.
- The extra indexes cost write time and disk, about a third more than the table alone; batched writes and the queue
  absorb the write cost, and the size budget counts the indexes.
- Disk is the real cost. Without sampling, a busy API fills a small disk in days; the documentation leads with
  sampling, the budget guards the disk, and the home shows trace storage next to log storage.
- Seven days is short for comparing with last month; longer retention needs more disk or downsampling, which is not
  planned.
- A trace may look incomplete for a few seconds while its spans arrive, and stays incomplete when a service did not
  send or sample its part. The trace view says so instead of hiding the gap.
- Job runs (`BackgroundService`, Hangfire, Quartz.NET) arrive as spans too, which the jobs ADR can build on.
