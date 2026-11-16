-- Metric rollups (ADR 0015): one row per series and 5 minutes, and per series and hour, so a chart over days reads a
-- few rows per series instead of every point. Each row holds what ADR 0010 computes rates and percentiles from: the
-- number of points, the sum and number of their values, and the bucket's last point as stored.

CREATE TABLE storage.metric_rollup_5m (
    series_id          bigint           NOT NULL,
    -- The start of the bucket, on a multiple of its width since the Unix epoch.
    bucket             timestamptz      NOT NULL,
    points             integer          NOT NULL,
    -- How many points had a value, and its sum, minimum, and maximum; histograms have none.
    value_count        integer          NOT NULL,
    value_sum          double precision,
    value_min          double precision,
    value_max          double precision,
    -- The bucket's last point, as in storage.metric_point.
    last_timestamp     timestamptz      NOT NULL,
    last_start         timestamptz,
    last_value         double precision,
    last_count         bigint,
    last_sum           double precision,
    last_min           double precision,
    last_max           double precision,
    last_bounds        double precision[],
    last_bucket_counts bigint[]
) PARTITION BY RANGE (bucket);

CREATE TABLE storage.metric_rollup_1h (LIKE storage.metric_rollup_5m) PARTITION BY RANGE (bucket);

CREATE TABLE storage.metric_rollup_5m_default PARTITION OF storage.metric_rollup_5m DEFAULT;
CREATE TABLE storage.metric_rollup_1h_default PARTITION OF storage.metric_rollup_1h DEFAULT;

-- Every read is some series over a time window, as with the points.
CREATE UNIQUE INDEX metric_rollup_5m_series_bucket ON storage.metric_rollup_5m (series_id, bucket);
CREATE UNIQUE INDEX metric_rollup_1h_series_bucket ON storage.metric_rollup_1h (series_id, bucket);

-- How far the rollup job has come: every point before five_minutes is in a 5-minute row, every one before hours in an
-- hourly row. Reads take the points after these times. One row.
CREATE TABLE storage.metric_rollup_state (
    id           boolean     PRIMARY KEY DEFAULT true CHECK (id),
    five_minutes timestamptz NOT NULL,
    hours        timestamptz NOT NULL
);

-- Hours that received points after they were rolled up, for example from a Collector sending its queue after an
-- outage. Reads take their points until the job rolls them up again. version tells the job whether a point arrived
-- while it was rolling the hour up.
CREATE TABLE storage.metric_rollup_dirty (
    hour    timestamptz PRIMARY KEY,
    version bigint      NOT NULL
);

CREATE SEQUENCE storage.metric_rollup_dirty_version;

-- The days whose points were written before rollups existed. A background job rolls up each, newest first, up to
-- up_to, where the rollup job took over; until then the day is read from its points.
CREATE TABLE storage.metric_rollup_backfill (
    day   date        PRIMARY KEY,
    up_to timestamptz NOT NULL
);

DO $$
DECLARE
    started   timestamptz := date_bin('1 hour', now(), timestamptz '1970-01-01 00:00:00+00');
    partition record;
    day       date;
    day_start timestamptz;
BEGIN
    -- The job starts at the current hour; anything before is the backfill's.
    INSERT INTO storage.metric_rollup_state (five_minutes, hours) VALUES (started, started);

    -- The rollups get the same daily partitions as the points, so retention can drop them together.
    FOR partition IN
        SELECT child.relname
        FROM pg_inherits
        JOIN pg_class AS child ON child.oid = pg_inherits.inhrelid
        JOIN pg_class AS parent ON parent.oid = pg_inherits.inhparent
        JOIN pg_namespace ON pg_namespace.oid = parent.relnamespace
        WHERE pg_namespace.nspname = 'storage'
          AND parent.relname = 'metric_point'
          AND child.relname ~ '^metric_point_[0-9]{8}$'
    LOOP
        day := to_date(substring(partition.relname FROM 14), 'YYYYMMDD');
        day_start := (to_char(day, 'YYYY-MM-DD') || ' 00:00:00+00')::timestamptz;

        EXECUTE format(
            'CREATE TABLE storage.%I PARTITION OF storage.metric_rollup_5m FOR VALUES FROM (%L) TO (%L)',
            'metric_rollup_5m_' || to_char(day, 'YYYYMMDD'),
            to_char(day, 'YYYY-MM-DD') || ' 00:00:00+00',
            to_char(day + 1, 'YYYY-MM-DD') || ' 00:00:00+00');
        EXECUTE format(
            'CREATE TABLE storage.%I PARTITION OF storage.metric_rollup_1h FOR VALUES FROM (%L) TO (%L)',
            'metric_rollup_1h_' || to_char(day, 'YYYYMMDD'),
            to_char(day, 'YYYY-MM-DD') || ' 00:00:00+00',
            to_char(day + 1, 'YYYY-MM-DD') || ' 00:00:00+00');

        IF day_start < started AND EXISTS (
            SELECT 1 FROM storage.metric_point
            WHERE timestamp >= day_start AND timestamp < day_start + interval '1 day' AND timestamp < started) THEN
            INSERT INTO storage.metric_rollup_backfill (day, up_to) VALUES (day, started);
        END IF;
    END LOOP;

    INSERT INTO storage.metric_rollup_backfill (day, up_to)
    SELECT DISTINCT (timestamp AT TIME ZONE 'UTC')::date, started
    FROM storage.metric_point_default
    WHERE timestamp < started
    ON CONFLICT DO NOTHING;
END
$$;
