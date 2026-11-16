-- Database activity other than the live view's sessions (ADR 0020): query plans, deadlocks, events, and snapshots of
-- settings, each kept as the agent sent it, by kind and key, in daily partitions.
CREATE TABLE storage.db_record (
    block_name  text        NOT NULL,
    environment text,
    sampled_at  timestamptz NOT NULL,
    kind        text        NOT NULL,
    -- What identifies the record within its kind, such as a plan hash; empty when nothing does.
    key         text        NOT NULL,
    attributes  jsonb       NOT NULL,
    -- Large text, such as a plan's XML or a deadlock graph; empty when the record has none.
    body        text        NOT NULL
) PARTITION BY RANGE (sampled_at);

CREATE TABLE storage.db_record_default PARTITION OF storage.db_record DEFAULT;

-- A server's records of one kind over a period, and one record by its key.
CREATE INDEX db_record_kind_time ON storage.db_record (block_name, kind, sampled_at DESC);
CREATE INDEX db_record_kind_key ON storage.db_record (block_name, kind, key, sampled_at DESC);

-- Who ran a statement: the live view's sessions by query hash.
CREATE INDEX db_session_query ON storage.db_session (block_name, query_hash, sampled_at) WHERE query_hash <> '';
