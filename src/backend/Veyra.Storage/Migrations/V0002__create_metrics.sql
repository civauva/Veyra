-- Metrics (ADR 0010): each series once, and narrow points split into daily partitions like logs.

-- One metric name with one exact set of resource and point attributes.
CREATE TABLE storage.metric_series (
    id                  bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    -- SHA-256 of everything that identifies the series, so a lookup needs no JSON comparison.
    hash                bytea       NOT NULL UNIQUE,
    name                text        NOT NULL,
    -- 1 = gauge, 2 = sum, 3 = histogram (Veyra.Core.Metrics.MetricKind).
    kind                smallint    NOT NULL CHECK (kind BETWEEN 1 AND 3),
    unit                text        NOT NULL,
    description         text        NOT NULL,
    -- 0 = none, 1 = delta, 2 = cumulative (Veyra.Core.Metrics.MetricTemporality).
    temporality         smallint    NOT NULL CHECK (temporality BETWEEN 0 AND 2),
    monotonic           boolean     NOT NULL,
    -- From service.name, deployment.environment.name, and host.name; null when the sender set none.
    service_name        text,
    environment         text,
    host_name           text,
    resource_attributes jsonb       NOT NULL,
    attributes          jsonb       NOT NULL,
    first_seen          timestamptz NOT NULL,
    -- Refreshed at most once a minute per series; retention deletes series not seen for longer than it keeps points.
    last_seen           timestamptz NOT NULL
);

-- Finding the series of a metric, a service, or a host.
CREATE INDEX metric_series_name ON storage.metric_series (name, last_seen);
CREATE INDEX metric_series_service ON storage.metric_series (service_name, environment) WHERE service_name IS NOT NULL;
CREATE INDEX metric_series_host ON storage.metric_series (host_name) WHERE host_name IS NOT NULL;

-- One measurement. Gauges and sums fill value; histograms fill count, sum, min, max, bounds, and bucket_counts.
-- There is no foreign key: whole days are dropped by partition, and series are deleted only after their points.
CREATE TABLE storage.metric_point (
    series_id       bigint           NOT NULL,
    timestamp       timestamptz      NOT NULL,
    -- When counting started, for cumulative points; a new start time means the sender restarted.
    start_timestamp timestamptz,
    value           double precision,
    count           bigint,
    sum             double precision,
    min             double precision,
    max             double precision,
    bounds          double precision[],
    bucket_counts   bigint[]
) PARTITION BY RANGE (timestamp);

-- Catches points outside every daily partition, for example from a sender with a wrong clock, so none is lost.
CREATE TABLE storage.metric_point_default PARTITION OF storage.metric_point DEFAULT;

-- Every read is some series over a time window.
CREATE INDEX metric_point_series_time ON storage.metric_point (series_id, timestamp);
