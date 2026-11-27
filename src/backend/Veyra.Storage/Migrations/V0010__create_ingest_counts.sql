-- Ingest counts (ADR 0023): how many log records, spans, and metric points were stored per minute and environment.
-- Each writer adds its batch's counts in the same transaction as the batch, so the counts are exact and current.
-- The Overview draws telemetry ingestion from them instead of counting raw rows, which would miss its speed budget.

CREATE TABLE storage.ingest_count_minute (
    minute      timestamptz NOT NULL,
    environment text,
    -- logs, spans, or points.
    signal      text        NOT NULL CHECK (signal IN ('logs', 'spans', 'points')),
    count       bigint      NOT NULL
) PARTITION BY RANGE (minute);

-- Counts of telemetry outside every daily partition, from a sender with a badly wrong clock.
CREATE TABLE storage.ingest_count_minute_default PARTITION OF storage.ingest_count_minute DEFAULT;

-- One row per key, which the writers' upsert finds. Telemetry without an environment is a key of its own.
CREATE UNIQUE INDEX ingest_count_minute_key
    ON storage.ingest_count_minute (minute, signal, environment) NULLS NOT DISTINCT;
