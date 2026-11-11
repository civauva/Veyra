-- ADR 0012: an alert belongs to a block, a service or a host, and optionally to a part of it such as a disk's mount
-- point. Every alert so far is about a service.
ALTER TABLE alerting.alert RENAME COLUMN service_name TO block_name;

ALTER TABLE alerting.alert
    ADD COLUMN block_kind text NOT NULL DEFAULT 'service' CHECK (block_kind IN ('service', 'host')),
    -- NULL when the alert is about the whole block.
    ADD COLUMN part text,
    -- The highest severity the alert ever had, so the history can say "was warning" after it eased and resolved.
    -- 1 = Caution, 2 = Warning (Veyra.Core.Alerts.AlertSeverity).
    ADD COLUMN worst_severity smallint CHECK (worst_severity BETWEEN 1 AND 2);

ALTER TABLE alerting.alert ALTER COLUMN block_kind DROP DEFAULT;

UPDATE alerting.alert alert
SET worst_severity = greatest(
    alert.severity,
    (SELECT max(event.severity) FROM alerting.alert_event event WHERE event.alert_id = alert.id));

ALTER TABLE alerting.alert ALTER COLUMN worst_severity SET NOT NULL;

-- At most one open alert per rule and subject. NULLS NOT DISTINCT makes a missing environment or part count as one.
DROP INDEX alerting.alert_open_per_service;

CREATE UNIQUE INDEX alert_open_per_subject
    ON alerting.alert (rule, environment, block_kind, block_name, part) NULLS NOT DISTINCT
    WHERE resolved_at IS NULL;

-- The Alerts screen lists alerts newest first, a page at a time (keyset paging).
CREATE INDEX alert_opened_at ON alerting.alert (opened_at DESC, id DESC);
