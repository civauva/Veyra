-- Which block telemetry belongs to (ADR 0018): a service, a host, a database server, or a container, decided once at
-- ingestion from the resource. The kinds are Veyra.Core.Alerts.AlertBlockKind: 1 service, 2 host, 3 database,
-- 4 container.

-- Metric series: the kind and name of their block, filled in for every series stored so far. A series with a
-- service belonged to it; one with only a host, such as the Collector's host metrics, to the host.
ALTER TABLE storage.metric_series
    ADD COLUMN block_kind smallint,
    ADD COLUMN block_name text;

UPDATE storage.metric_series
SET block_kind = CASE WHEN service_name IS NULL AND host_name IS NOT NULL THEN 2 ELSE 1 END,
    block_name = coalesce(service_name, host_name, 'unknown_service');

ALTER TABLE storage.metric_series
    ALTER COLUMN block_kind SET NOT NULL,
    ALTER COLUMN block_name SET NOT NULL;

-- A database or container screen finds its series by block.
CREATE INDEX metric_series_block ON storage.metric_series (block_kind, block_name, environment);

-- Log records: service_name keeps the name of the record's block, so the existing indexes find a database's records
-- as they find a service's, and block_kind says which kind it is. A constant default changes no stored row, so the
-- records written so far count as their service without rewriting a partition.
ALTER TABLE storage.log_record
    ADD COLUMN block_kind smallint NOT NULL DEFAULT 1,
    ADD COLUMN host_name text;

-- The counts keep the kind too: error rates and the home count services only, while the Log Explorer's chart counts
-- every record.
ALTER TABLE storage.log_count_minute ADD COLUMN block_kind smallint NOT NULL DEFAULT 1;
ALTER TABLE storage.log_count_hour ADD COLUMN block_kind smallint NOT NULL DEFAULT 1;

DROP INDEX storage.log_count_minute_key;
DROP INDEX storage.log_count_hour_key;

CREATE UNIQUE INDEX log_count_minute_key
    ON storage.log_count_minute (minute, service_name, severity, environment, block_kind) NULLS NOT DISTINCT;
CREATE UNIQUE INDEX log_count_hour_key
    ON storage.log_count_hour (hour, service_name, severity, environment, block_kind) NULLS NOT DISTINCT;
