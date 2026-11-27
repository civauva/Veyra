-- Incidents (ADR 0026): related alerts grouped into one thing to work on. An alert joins an incident in the
-- transaction that opens it, so an alert is never stored without its incident, and the incident resolves in the
-- transaction that resolves its last alert.

-- INC-1, INC-2, ...: people say the number, so it never repeats, even after incidents are deleted.
CREATE SEQUENCE alerting.incident_number AS bigint;

CREATE TABLE alerting.incident (
    id                    uuid        PRIMARY KEY,
    number                bigint      NOT NULL UNIQUE DEFAULT nextval('alerting.incident_number'),
    -- NULL when its alerts' senders set no deployment environment. Only alerts of the same environment join.
    environment           text,
    -- The headline of its first alert, or of the worst one that joined later, as notifications word it.
    title                 text        NOT NULL,
    -- The worst severity of its alerts: 1 = Caution, 2 = Warning (Veyra.Core.Alerts.AlertSeverity).
    severity              smallint    NOT NULL CHECK (severity BETWEEN 1 AND 2),
    state                 text        NOT NULL CHECK (state IN ('open', 'acknowledged', 'resolved')),
    opened_at             timestamptz NOT NULL,
    -- Who acknowledged it and when; cleared when the acknowledgement is taken back.
    acknowledged_at       timestamptz,
    acknowledged_by_id    uuid,
    acknowledged_by_name  text,
    resolved_at           timestamptz,
    updated_at            timestamptz NOT NULL,
    CHECK ((state = 'resolved') = (resolved_at IS NOT NULL)),
    CHECK ((state = 'acknowledged') = (acknowledged_at IS NOT NULL) OR state = 'resolved')
);

ALTER SEQUENCE alerting.incident_number OWNED BY alerting.incident.number;

-- The Incidents screen lists open and acknowledged incidents first, then the newest (keyset paging).
CREATE INDEX incident_listing ON alerting.incident ((state = 'resolved'), opened_at DESC, id DESC);

-- A new alert looks for an open incident of its environment to join; there are few.
CREATE INDEX incident_unresolved ON alerting.incident (environment) WHERE state <> 'resolved';

-- An alert belongs to at most one incident. Deleting an alert takes it out of its incident.
CREATE TABLE alerting.incident_alert (
    alert_id     uuid        PRIMARY KEY REFERENCES alerting.alert (id) ON DELETE CASCADE,
    incident_id  uuid        NOT NULL REFERENCES alerting.incident (id) ON DELETE CASCADE,
    joined_at    timestamptz NOT NULL
);

-- The alerts of one incident.
CREATE INDEX incident_alert_incident ON alerting.incident_alert (incident_id, joined_at);

-- What people did to an incident, for its timeline: acknowledged it, or took that back.
CREATE TABLE alerting.incident_event (
    id           bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    incident_id  uuid        NOT NULL REFERENCES alerting.incident (id) ON DELETE CASCADE,
    -- 1 = acknowledged, 2 = unacknowledged.
    kind         smallint    NOT NULL CHECK (kind BETWEEN 1 AND 2),
    actor_id     uuid,
    actor_name   text        NOT NULL,
    at           timestamptz NOT NULL
);

CREATE INDEX incident_event_incident ON alerting.incident_event (incident_id, at);
