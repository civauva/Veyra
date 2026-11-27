-- Job runs (ADR 0027): one row per run of a job, whether recognised from a trace's root span or read from SQL Server
-- Agent's job history. The span writer and the activity writer add them in the transaction of their batch. Runs are
-- kept 30 days in daily partitions on their start, longer than the spans and shorter than the activity records they
-- come from, so a month's success share outlives the traces.
CREATE TABLE storage.job_run (
    -- trace or sqlagent.
    source            text             NOT NULL CHECK (source IN ('trace', 'sqlagent')),
    environment       text,
    -- service for a traced job, database for a SQL Server Agent job.
    block_kind        text             NOT NULL,
    -- The service, or the database server's engine and server, as the other tables name blocks.
    block_name        text             NOT NULL,
    job_name          text             NOT NULL,
    -- cicd.pipeline.run.id or the trace ID for a traced run; the job history's instance_id for SQL Server Agent.
    run_id            text             NOT NULL,
    started_at        timestamptz      NOT NULL,
    -- NULL while the run goes on.
    duration_seconds  double precision,
    -- 0 succeeded, 1 failed, 2 running.
    status            smallint         NOT NULL,
    -- A traced run's steps, the root span's direct children in start order:
    -- [{"name": ..., "duration_seconds": ..., "status": "succeeded" | "failed", "message": ...}].
    -- Always empty for SQL Server Agent, whose steps stay in storage.db_record and are read from there.
    steps             jsonb            NOT NULL,
    records_processed bigint,
    records_total     bigint,
    retries           integer,
    -- The trace of a traced run, as storage.span keeps trace IDs, so the trace link can tell whether it still exists.
    trace_id          bytea,
    message           text
) PARTITION BY RANGE (started_at);

-- Runs outside every daily partition, from a sender with a badly wrong clock.
CREATE TABLE storage.job_run_default PARTITION OF storage.job_run DEFAULT;

-- One row per run, which makes a resent span or job history row an update instead of a second run. Its columns are in
-- the order the per-job reads need, so the same index lists a job's runs newest first. A run without an environment
-- is a key of its own.
CREATE UNIQUE INDEX job_run_key
    ON storage.job_run (source, environment, block_name, job_name, started_at DESC, run_id) NULLS NOT DISTINCT;

-- Every job's runs over a window, for the Jobs list and the Overview.
CREATE INDEX job_run_time ON storage.job_run (started_at);
