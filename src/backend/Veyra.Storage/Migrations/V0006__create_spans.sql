-- Spans (ADR 0014): one row per span, split into daily partitions by start time like logs and metric points.
-- A trace is read by its ID; there is no table of whole traces, because spans of one trace arrive in pieces.
CREATE TABLE storage.span (
    -- IDs as bytes, half the size of hex text, so their indexes stay in memory. The API shows them as lower-case hex.
    trace_id            bytea       NOT NULL,
    span_id             bytea       NOT NULL,
    -- NULL for the root span of a trace.
    parent_span_id      bytea,
    name                text        NOT NULL,
    -- Veyra.Core.Traces.SpanKind: 0 unspecified, 1 internal, 2 server, 3 client, 4 producer, 5 consumer.
    kind                smallint    NOT NULL,
    start_time          timestamptz NOT NULL,
    duration_us         bigint      NOT NULL,
    -- Veyra.Core.Traces.SpanStatus: 0 unset, 1 ok, 2 error.
    status              smallint    NOT NULL,
    status_message      text,
    service_name        text        NOT NULL,
    environment         text,
    host_name           text,
    attributes          jsonb       NOT NULL,
    resource_attributes jsonb       NOT NULL,
    -- [{"time": ..., "name": ..., "attributes": {...}}], oldest first; an exception is an event named "exception".
    events              jsonb       NOT NULL,
    -- [{"traceId": ..., "spanId": ..., "attributes": {...}}]
    links               jsonb       NOT NULL,
    -- How many attributes, events, and links the sender or Veyra's limits left out.
    dropped_attributes  integer     NOT NULL,
    dropped_events      integer     NOT NULL,
    dropped_links       integer     NOT NULL
) PARTITION BY RANGE (start_time);

-- Catches spans outside every daily partition, for example from a sender with a wrong clock, so none is lost.
CREATE TABLE storage.span_default PARTITION OF storage.span DEFAULT;

-- Every span of a trace, for the trace view.
CREATE INDEX span_trace ON storage.span (trace_id);

-- The spans of a service in a time window, for searches across traces.
CREATE INDEX span_service_time ON storage.span (service_name, start_time DESC);

-- Failed spans of a service in a time window; a small index, because most spans succeed.
CREATE INDEX span_failed ON storage.span (service_name, start_time DESC) WHERE status = 2;

-- Attribute filters such as {"http.route": "/payments"}.
CREATE INDEX span_attributes ON storage.span USING gin (attributes jsonb_path_ops);

-- The trace list: one row per trace from its root span, newest first. The index holds every column the list shows,
-- so the list is read from the index alone, without touching the table (ADR 0014). Paging is by start time and
-- span ID, never by offset.
CREATE INDEX span_root_time ON storage.span (start_time DESC, span_id DESC)
    INCLUDE (trace_id, service_name, environment, name, duration_us, status)
    WHERE parent_span_id IS NULL;

-- The trace list of one service, the common case, read the same way.
CREATE INDEX span_root_service_time ON storage.span (service_name, start_time DESC, span_id DESC)
    INCLUDE (trace_id, environment, name, duration_us, status)
    WHERE parent_span_id IS NULL;
