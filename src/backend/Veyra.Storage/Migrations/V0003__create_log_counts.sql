-- Log counts (ADR 0015): the number of records per minute and per hour, by environment, service, and severity.
-- The log writer adds each batch's counts in the same transaction as the batch, so they are exact and current.
-- Counting reads them instead of the records, so it takes the same time however many records there are.

CREATE TABLE storage.log_count_minute (
    minute       timestamptz NOT NULL,
    environment  text,
    service_name text        NOT NULL,
    -- Veyra.Core.Telemetry.LogSeverity, as in storage.log_record.
    severity     smallint    NOT NULL,
    records      bigint      NOT NULL
) PARTITION BY RANGE (minute);

CREATE TABLE storage.log_count_hour (
    hour         timestamptz NOT NULL,
    environment  text,
    service_name text        NOT NULL,
    severity     smallint    NOT NULL,
    records      bigint      NOT NULL
) PARTITION BY RANGE (hour);

-- Counts of records outside every daily partition, which land in storage.log_record_default.
CREATE TABLE storage.log_count_minute_default PARTITION OF storage.log_count_minute DEFAULT;
CREATE TABLE storage.log_count_hour_default PARTITION OF storage.log_count_hour DEFAULT;

-- One row per key, which the writer's upsert finds. A record without an environment is a key of its own.
CREATE UNIQUE INDEX log_count_minute_key
    ON storage.log_count_minute (minute, service_name, severity, environment) NULLS NOT DISTINCT;
CREATE UNIQUE INDEX log_count_hour_key
    ON storage.log_count_hour (hour, service_name, severity, environment) NULLS NOT DISTINCT;

-- The days whose records were written before counting existed. A background job counts each, newest first; until
-- then the day is counted from its records. up_to_id is the last record ID written before counting started: the job
-- counts only those records, because the writer counted every later one.
CREATE TABLE storage.log_count_backfill (
    day      date   PRIMARY KEY,
    up_to_id bigint NOT NULL
);

DO $$
DECLARE
    last_id   bigint;
    partition record;
    day       date;
BEGIN
    EXECUTE format(
        'SELECT CASE WHEN is_called THEN last_value ELSE 0 END FROM %s',
        pg_get_serial_sequence('storage.log_record', 'id'))
    INTO last_id;

    -- The count tables get the same daily partitions as the records, so retention can drop them together.
    FOR partition IN
        SELECT child.relname
        FROM pg_inherits
        JOIN pg_class AS child ON child.oid = pg_inherits.inhrelid
        JOIN pg_class AS parent ON parent.oid = pg_inherits.inhparent
        JOIN pg_namespace ON pg_namespace.oid = parent.relnamespace
        WHERE pg_namespace.nspname = 'storage'
          AND parent.relname = 'log_record'
          AND child.relname ~ '^log_record_[0-9]{8}$'
    LOOP
        day := to_date(substring(partition.relname FROM 12), 'YYYYMMDD');

        EXECUTE format(
            'CREATE TABLE storage.%I PARTITION OF storage.log_count_minute FOR VALUES FROM (%L) TO (%L)',
            'log_count_minute_' || to_char(day, 'YYYYMMDD'),
            to_char(day, 'YYYY-MM-DD') || ' 00:00:00+00',
            to_char(day + 1, 'YYYY-MM-DD') || ' 00:00:00+00');
        EXECUTE format(
            'CREATE TABLE storage.%I PARTITION OF storage.log_count_hour FOR VALUES FROM (%L) TO (%L)',
            'log_count_hour_' || to_char(day, 'YYYYMMDD'),
            to_char(day, 'YYYY-MM-DD') || ' 00:00:00+00',
            to_char(day + 1, 'YYYY-MM-DD') || ' 00:00:00+00');

        IF last_id > 0 THEN
            INSERT INTO storage.log_count_backfill (day, up_to_id) VALUES (day, last_id);
        END IF;
    END LOOP;

    IF last_id > 0 THEN
        INSERT INTO storage.log_count_backfill (day, up_to_id)
        SELECT DISTINCT (timestamp AT TIME ZONE 'UTC')::date, last_id
        FROM storage.log_record_default
        ON CONFLICT DO NOTHING;
    END IF;
END
$$;
