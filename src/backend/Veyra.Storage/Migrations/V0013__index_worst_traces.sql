-- The trace list worst first (ADR 0029): failed traces, then the slowest, then by trace ID and span ID, as the list's
-- keyset pages them. Like the newest-first indexes of V0006, these are partial on root spans and hold every column the
-- list shows, so a page is read from an index alone and in order, without sorting the window's traces. The status
-- column is included beside its expression so the scan stays index-only. They are made on the parent, so every day
-- has them: the existing days get them now, and days created from now on get them with their partition.
CREATE INDEX span_root_worst ON storage.span ((status = 2) DESC, duration_us DESC, trace_id DESC, span_id DESC)
    INCLUDE (start_time, service_name, environment, name, status)
    WHERE parent_span_id IS NULL;

-- The worst traces of one service, the common case, read the same way.
CREATE INDEX span_root_service_worst
    ON storage.span (service_name, (status = 2) DESC, duration_us DESC, trace_id DESC, span_id DESC)
    INCLUDE (start_time, environment, name, status)
    WHERE parent_span_id IS NULL;
