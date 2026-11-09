-- Log records (ADR 0003): one table, split into daily partitions by timestamp. Retention drops whole partitions,
-- which is instant, instead of deleting rows. Partitions are created ahead of time by LogPartitions.
CREATE TABLE storage.log_record (
    -- Orders records with equal timestamps for keyset paging. Not meaningful otherwise.
    id                  bigint      GENERATED ALWAYS AS IDENTITY,
    timestamp           timestamptz NOT NULL,
    observed_at         timestamptz NOT NULL,
    -- Veyra.Core.Telemetry.LogSeverity: 0 unspecified, 1 trace ... 6 fatal.
    severity            smallint    NOT NULL,
    severity_text       text,
    body                text        NOT NULL,
    service_name        text        NOT NULL,
    environment         text,
    trace_id            text,
    span_id             text,
    attributes          jsonb       NOT NULL,
    resource_attributes jsonb       NOT NULL
) PARTITION BY RANGE (timestamp);

-- Catches records outside every daily partition, for example from a sender with a wrong clock, so none is lost.
CREATE TABLE storage.log_record_default PARTITION OF storage.log_record DEFAULT;

-- Newest first within a time window: the Log Explorer's default view and keyset paging.
CREATE INDEX log_record_time ON storage.log_record (timestamp DESC, id DESC);

-- Filtering by service within a time window.
CREATE INDEX log_record_service_time ON storage.log_record (service_name, timestamp DESC);

-- Jumping from a trace to its logs.
CREATE INDEX log_record_trace ON storage.log_record (trace_id) WHERE trace_id IS NOT NULL;

-- Word search in messages. The "simple" configuration does no stemming, so it works the same for every language.
CREATE INDEX log_record_body_text ON storage.log_record USING gin (to_tsvector('simple', body));

-- Attribute filters such as {"http.route": "/pay"}.
CREATE INDEX log_record_attributes ON storage.log_record USING gin (attributes jsonb_path_ops);
