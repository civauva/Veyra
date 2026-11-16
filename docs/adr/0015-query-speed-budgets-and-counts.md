# 0015. Query speed: budgets, pre-counted logs, and metric rollups

Date: 2026-11-11
Status: Accepted

## Context

Every screen must answer at once (VEY-28): an observability tool that makes people wait is not used during an
incident, which is when it is needed. ADR 0014 set budgets for traces; this ADR sets them for everything else.

Veyra was measured on a benchmark installation at the scale of a busy small team: 3 million log records a day for the
14 days logs are kept (41 million records, 18 GB), and about 2,400 metric series at one point a minute for the 30 days
metrics are kept (104 million points, 13 GB). PostgreSQL was limited to the reference installation of ADR 0014 (4
vCPUs, 8 GB of memory, SSD), the tables were laid out in time order as ingestion writes them, and 35 log records a
second arrived while measuring. Times are the 95th percentile of ten requests after one to warm up.

| Request | p95 |
| --- | --- |
| Every log search: last hour, 7 days, a word, errors, a service, an attribute, a trace | 10 to 33 ms |
| Alerts, the alert summary, users, the account, the session | 7 to 24 ms |
| Log counts (the Log Explorer's histogram and services): last hour | 168 ms |
| Log counts: 24 hours | 1,330 ms |
| Log counts: 7 days | 5,510 ms |
| Log counts: 7 days of errors | 3,147 ms |
| Log counts: 7 days of a word | 1,237 ms |
| The home, which every open browser asks for every 15 seconds | 2,042 ms |
| Metrics overview: 6 hours | 592 ms |
| Metrics overview: 7 days | 5,917 ms |
| A host, a service: 7 days | 323, 512 ms |
| A metric chart: 7 days | 1,778 ms |

Searching is fast because every search reads newest first with an index and stops after a page. Counting is slow
because it reads every record or point in its window: twice the records, twice the time. The home counts the last 24
hours on every refresh and the last 7 days once an hour; the metric screens read every point of every series they draw.

Two prototypes on the same data show the way out:

- Counts per minute, environment, service, and severity: 2.6 million rows for 14 days (195 MB). Seven days of counts
  in 215 ms. Counts per hour: 59,000 rows (5 MB); seven days in 3 to 9 ms.
- One row per metric series and hour with the hour's last point: seven days of the overview's 360 series in 13 ms,
  against 5,873 ms from the points.

## Decision

1. **Budgets.** Server time at the 95th percentile, at the reference volume above, on the reference installation:

   | Request | Budget |
   | --- | --- |
   | The home | 150 ms |
   | A log search, any range and filter | 100 ms |
   | Log counts, any range, by time, severity, service, or environment | 100 ms |
   | Log counts filtered by a word or an attribute, up to 24 hours | 300 ms |
   | Metrics overview, a host, a service, a chart, any range | 200 ms |
   | Alerts, alert details, the alert summary | 100 ms |
   | Users, the account, keys, notification channels, the session | 50 ms |
   | Accepting an OTLP batch (queued) | 10 ms, as before |

   Counts by time, severity, service, and environment, and metric reads, must keep their budgets at ten times the
   reference volume: they may grow with the number of services and series, never with the number of records.
2. **Logs are counted as they are written.** Two tables in the Storage module's schema hold the number of records per
   minute and per hour, by environment, service, and severity: `storage.log_count_minute` and
   `storage.log_count_hour`. The log writer adds each batch's counts in the same transaction as the batch's binary
   `COPY`, with `INSERT … ON CONFLICT DO UPDATE` on the batch's few hundred keys, so the counts are exact and current
   the moment the records are. They are split into daily partitions and dropped with the log partitions of the same day.
3. **Counting reads the counts.** The Log Explorer's histogram and service list, the home's hours and blocks, the
   expected load, and the error-rate rules read the hour table for buckets of an hour or more and the minute table
   otherwise. The few seconds at either edge of a window that starts or ends inside a minute are counted from the
   records, so the numbers are exactly those of the records.
4. **Counts filtered by a word or an attribute** still read the records. Up to 24 hours they stay within 300 ms. For a
   longer window the histogram is filled day by day, newest first, each day a request of its own, and the list of
   records never waits for it. A new log search starts on the day's counts, and the screen says that the older days
   are still being counted.
5. **Metrics are rolled up into 5-minute and hourly rows** per series: the number of points, their average, total,
   minimum, and maximum, and the bucket's last point as stored (time, start time, value, and for histograms count, sum,
   bounds, and bucket counts). Rates and percentiles keep being computed when read (ADR 0010), from these rows
   exactly as from points. A background job of the Storage module rolls up every finished 5 minutes, about 12,000
   points at this scale, and every finished hour from its 5-minute rows; reading the unfinished bucket at the end of a
   window from the points keeps charts current to the minute. Rollups live in daily partitions and are dropped with
   the points.
6. **A metric read picks the coarsest level that still gives its buckets:** hourly rows for buckets of an hour or more,
   5-minute rows for buckets of 5 minutes or more, points otherwise. The 1-hour range keeps reading points.
7. **The home is built once per environment every 5 seconds** and shared by every browser that asks, so twenty open
   homes cost what one does.
8. **Proof.**
   - The benchmark that produced the numbers above is kept in `scripts/benchmark/`: it seeds the reference volume in
     time order, sends live traffic, measures every request, and fails when one is over its budget. It runs before
     every release and on any change to a query.
   - Integration tests (Testcontainers) check every counting and metric query's plan with `EXPLAIN`: counts must read
     the count tables, metric reads the level they should, and every query only the partitions of its window. A change
     that loses an index or a level fails the build.
   - Tests compare the counts with a count of the records after writes, retention, and a restart.
   - The reference installation watches Veyra's own request durations with the built-in latency rule (ADR 0012).
9. **Upgrading.** A migration creates the tables; a background backfill counts the records and rolls up the points that
   already exist, newest day first. Until a day is done, its counts are read from the records as before, so nothing is
   wrong while the backfill runs, only slower.

## Alternatives considered

- **TimescaleDB continuous aggregates.** They do exactly this, but they are an extension the standard `postgres` image
  does not have, against ADR 0003 and ADR 0006.
- **Materialized views refreshed on a timer.** A full refresh rereads everything it summarises, and the counts would lag
  by the refresh interval, so the home would disagree with the Log Explorer.
- **Triggers on the log table.** Row triggers slow binary `COPY` several times over; the writer already holds each
  batch and can count it for free.
- **Counting faster: parallel query, more memory, caching results.** Each helps by a constant; counting stays linear in
  the number of records, and every new filter or window misses the cache.
- **Approximate counts (sampling, HyperLogLog).** Fast, but an operations tool must not show a rounded number of errors.
- **Rolling up metrics at ingestion, like logs.** Keeping each bucket's last point current would add an upsert per point
  and double the write work; a set-based job every 5 minutes is cheap, and reading the newest bucket from the points
  keeps charts current.

## Consequences

- The budgets are a commitment: a request over its budget is a bug, and the benchmark and the plan tests catch it.
- Each log batch writes a few hundred count rows more, in the same transaction; counts add about 1 % to log storage.
  Metric rollups add about a fifth of the points' rows at the 5-minute level and a sixtieth at the hourly one.
- Two sources must agree: the counts and the records, the rollups and the points. Tests compare them, retention drops
  both together, and the backfill is resumable.
- Counting by a word over many days stays the one slower request; it is shown progressively instead of making people
  wait for it, and it does not hold up the list.
- Grouping by other dimensions later (host, attribute) means another count table; the budgets say when one is needed.
