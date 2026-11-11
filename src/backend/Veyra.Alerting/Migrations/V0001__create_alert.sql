-- Alerts raised by rules (ADR 0007: health comes only from alerts). The schema "alerting" is created by the migrator
-- before this script runs. An alert lives from the first evaluation that fires until the one that resolves it; its
-- severity can rise and fall in between.
CREATE TABLE alerting.alert (
    id              uuid             PRIMARY KEY,
    -- The rule that raised it, for example "error-rate".
    rule            text             NOT NULL,
    -- NULL when the sender set no deployment environment.
    environment     text,
    service_name    text             NOT NULL,
    -- 1 = Caution, 2 = Warning (Veyra.Core.Alerts.AlertSeverity).
    severity        smallint         NOT NULL CHECK (severity BETWEEN 1 AND 2),
    -- The latest and the highest measurement, and the threshold of the current severity, in the rule's unit.
    value           double precision NOT NULL,
    peak            double precision NOT NULL,
    threshold       double precision NOT NULL,
    window_seconds  integer          NOT NULL,
    opened_at       timestamptz      NOT NULL,
    updated_at      timestamptz      NOT NULL,
    resolved_at     timestamptz
);

-- At most one open alert per rule and service. NULLS NOT DISTINCT makes services without an environment count as one.
CREATE UNIQUE INDEX alert_open_per_service
    ON alerting.alert (rule, environment, service_name) NULLS NOT DISTINCT
    WHERE resolved_at IS NULL;

-- Recently resolved alerts for the home dashboard, and the cleanup of old ones.
CREATE INDEX alert_resolved_at ON alerting.alert (resolved_at) WHERE resolved_at IS NOT NULL;
