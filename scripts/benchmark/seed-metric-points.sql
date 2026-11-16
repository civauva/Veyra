-- Benchmark (ADR 0015): one day of metric points, one a minute for every series, in time order as a Collector sends
-- them. psql variables: day (YYYY-MM-DD, a UTC day), until (an instant; points stop there), start (when counting
-- started, for cumulative sums). The day is left for Veyra's backfill to roll up, as after an upgrade.
-- Cumulative sums and histograms grow steadily from start; gauges wander around their level; disks fill slowly.
\set ON_ERROR_STOP on

SELECT format(
    'CREATE TABLE IF NOT EXISTS storage.metric_point_%s PARTITION OF storage.metric_point FOR VALUES FROM (%L) TO (%L)',
    to_char(:'day'::date, 'YYYYMMDD'),
    (:'day'::date)::text || ' 00:00:00+00',
    (:'day'::date + 1)::text || ' 00:00:00+00') \gexec

-- The rollups get the same days, as Veyra makes them.
SELECT format(
    'CREATE TABLE IF NOT EXISTS storage.%s_%s PARTITION OF storage.%s FOR VALUES FROM (%L) TO (%L)',
    t, to_char(:'day'::date, 'YYYYMMDD'), t,
    (:'day'::date)::text || ' 00:00:00+00',
    (:'day'::date + 1)::text || ' 00:00:00+00')
FROM unnest(ARRAY['metric_rollup_5m', 'metric_rollup_1h']) AS t \gexec

SET synchronous_commit = off;

INSERT INTO storage.metric_point
    (series_id, timestamp, start_timestamp, value, count, sum, min, max, bounds, bucket_counts)
SELECT
    s.id,
    t.ts,
    CASE WHEN s.temporality = 2 THEN :'start'::timestamptz END,
    CASE
        WHEN s.kind = 3 THEN NULL
        WHEN s.kind = 2 THEN (0.05 + (s.id % 9) / 10.0) * extract(epoch FROM t.ts - :'start'::timestamptz)
        WHEN s.name = 'system.filesystem.usage' THEN
            1e11 * (0.3 + (s.id % 5) / 10.0) + 2e4 * extract(epoch FROM t.ts - :'start'::timestamptz)
        ELSE (1000 + s.id % 1000) * (1 + 0.2 * sin(extract(epoch FROM t.ts) / 3600.0 + s.id))
    END,
    CASE WHEN s.kind = 3 THEN (c.n)::bigint END,
    CASE WHEN s.kind = 3 THEN c.n * 0.12 END,
    CASE WHEN s.kind = 3 THEN 0.002 END,
    CASE WHEN s.kind = 3 THEN 4.5 END,
    CASE WHEN s.kind = 3 THEN ARRAY[0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10] END,
    CASE WHEN s.kind = 3 THEN ARRAY[
        (c.n * 0.02)::bigint, (c.n * 0.05)::bigint, (c.n * 0.12)::bigint, (c.n * 0.18)::bigint, (c.n * 0.14)::bigint,
        (c.n * 0.10)::bigint, (c.n * 0.20)::bigint, (c.n * 0.08)::bigint, (c.n * 0.04)::bigint, (c.n * 0.03)::bigint,
        (c.n * 0.02)::bigint, (c.n * 0.01)::bigint, (c.n * 0.005)::bigint, (c.n * 0.003)::bigint,
        (c.n * 0.002)::bigint] END
FROM storage.metric_series AS s
CROSS JOIN LATERAL (
    SELECT ts
    FROM generate_series(
        (:'day' || ' 00:00:00+00')::timestamptz,
        least((:'day' || ' 23:59:00+00')::timestamptz, :'until'::timestamptz),
        interval '1 minute') AS ts
) AS t
CROSS JOIN LATERAL (
    -- Requests so far: a few a second per route, more on the busy ones.
    SELECT (1 + s.id % 7) * extract(epoch FROM t.ts - :'start'::timestamptz) / 10 AS n
) AS c
ORDER BY t.ts, s.id;
