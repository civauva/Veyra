# Benchmark

Measures every read request of Veyra's screens at the reference volume of [ADR 0015](../../docs/adr/0015-query-speed-budgets-and-counts.md)
and fails when one is over its budget. Run it before every release and after any change to a query.

| File | What it does |
| --- | --- |
| `seed.sh` | Fills a benchmark installation: 14 days of logs at 3 million records a day, and 30 days of metric points for about 2,400 series, in time order. |
| `seed-logs.sql`, `seed-metric-series.sql`, `seed-metric-points.sql` | One day of records, the series, and one day of points; `seed.sh` runs them. |
| `seed-spans.sh`, `seed-spans.sql` | Fills the trace tables with 7 days at the scale of [ADR 0014](../../docs/adr/0014-trace-storage.md): 500,000 traces of ten spans a day, 35 million spans. |
| `load.js` | Sends 35 log records a second over OTLP/HTTP while measuring, as a busy installation receives them. |
| `measure.js` | Asks for each request 20 times after one warm-up and compares the 95th percentile with its budget. |

## Running it

The benchmark needs its own installation; never seed one that holds real data, because `seed.sh` empties the telemetry
tables first. Use a separate Compose project and ports, with an env file that sets `POSTGRES_PASSWORD`,
`POSTGRES_SHARED_BUFFERS=2GB` (a quarter of the reference installation's memory), `VEYRA_HTTP_PORT=8091`,
`VEYRA_OTLP_GRPC_PORT=24317`, and `VEYRA_OTLP_HTTP_PORT=24318` (see
[deploy/.env.example](../../deploy/.env.example)), and create its first administrator from the setup link in its log,
as in [the deployment guide](../../docs/guides/deployment.md):

```bash
docker compose -p veyra-bench --env-file bench.env -f deploy/docker-compose.yml up -d --build
```

Limit PostgreSQL to the reference installation, 4 vCPUs and 8 GB of memory:

```bash
docker update --cpus 4 --memory 8g --memory-swap 8g veyra-bench-postgres-1
```

Seed it. This takes about 15 minutes; give PostgreSQL more CPUs while it runs, and set them back before measuring:

```bash
scripts/benchmark/seed.sh
```

Then the traces, which take about as long again and need no backfill:

```bash
scripts/benchmark/seed-spans.sh
```

Veyra then counts and rolls up the seeded days in the background, newest first, exactly as after an upgrade. Measure
once that is done, which the two backfill tables tell:

```bash
docker exec veyra-bench-postgres-1 psql -U veyra -d veyra -c "SELECT (SELECT count(*) FROM storage.log_count_backfill) AS log_days, (SELECT count(*) FROM storage.metric_rollup_backfill) AS metric_days"
```

Send live traffic in one terminal and measure in another, signed in as that administrator:

```bash
SHOT_EMAIL=admin@example.test SHOT_PASSWORD=... node scripts/benchmark/load.js
```

```bash
SHOT_EMAIL=admin@example.test SHOT_PASSWORD=... node scripts/benchmark/measure.js
```

`load.js` never writes the word "order", which the seed writes often, so "a common word that stopped" searches for a
word whose newest record is where the seed ended (ADR 0016). Let the load run for an hour before measuring, so the
records after it are as many as a busy hour holds.

`measure.js` needs the frontend's dependencies (`npm ci` in `src/frontend`), because it signs in with Playwright.
`BASE` points it at another address, `RUNS` changes the number of runs, and `ONLY` picks requests by a regular
expression, for example `ONLY="Log counts"`.
