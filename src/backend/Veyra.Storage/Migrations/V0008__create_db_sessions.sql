-- The live view of database servers (ADR 0020, area 3): every read of a server's busy sessions, one row per session,
-- split into daily partitions by the time of the read like logs and spans, and kept for days, not months.
-- Session 0 of a read holds its server-wide figures, so a read that found nothing busy still exists.
CREATE TABLE storage.db_session (
    block_name              text        NOT NULL,
    environment             text,
    sampled_at              timestamptz NOT NULL,
    session_id              integer     NOT NULL,
    request_status          text        NOT NULL,
    session_status          text        NOT NULL,
    command                 text        NOT NULL,
    database_name           text        NOT NULL,
    program                 text        NOT NULL,
    host                    text        NOT NULL,
    login                   text        NOT NULL,
    blocked_by              integer     NOT NULL,
    wait_type               text        NOT NULL,
    wait_ms                 bigint      NOT NULL,
    wait_resource           text        NOT NULL,
    elapsed_ms              bigint      NOT NULL,
    cpu_ms                  bigint      NOT NULL,
    reads                   bigint      NOT NULL,
    writes                  bigint      NOT NULL,
    tempdb_kb               bigint      NOT NULL,
    grant_kb                bigint      NOT NULL,
    grant_requested_kb      bigint      NOT NULL,
    grant_waiting           boolean     NOT NULL,
    -- NULL when the session has no open transaction.
    transaction_ms          bigint,
    transaction_log_records bigint      NOT NULL,
    open_transactions       integer     NOT NULL,
    isolation               smallint    NOT NULL,
    percent_complete        real        NOT NULL,
    remaining_ms            bigint      NOT NULL,
    query_hash              text        NOT NULL,
    plan_hash               text        NOT NULL,
    idle_ms                 bigint      NOT NULL,
    connected_seconds       bigint      NOT NULL,
    text                    text        NOT NULL,
    running                 boolean     NOT NULL,
    blocking                boolean     NOT NULL,
    -- Server-wide figures, on session 0 only.
    tempdb_size_kb          bigint      NOT NULL,
    tempdb_free_kb          bigint      NOT NULL,
    grants_waiting          integer     NOT NULL
) PARTITION BY RANGE (sampled_at);

-- Catches reads outside every daily partition, from an agent with a wrong clock, so none is lost.
CREATE TABLE storage.db_session_default PARTITION OF storage.db_session DEFAULT;

-- A server's newest read, and the read at a chosen moment.
CREATE INDEX db_session_block_time ON storage.db_session (block_name, sampled_at DESC);

-- The newest read of every server, for the rules: one small row per read.
CREATE INDEX db_session_reads ON storage.db_session (sampled_at DESC, block_name) WHERE session_id = 0;
