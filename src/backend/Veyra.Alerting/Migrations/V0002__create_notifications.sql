-- Alert history and notifications (VEY-18). Every change of an alert is an event; every event that a channel wants
-- becomes a delivery, written in the same transaction as the change. A dispatcher sends due deliveries later, so a
-- slow or failing channel never holds up rule evaluation.

-- When the alert reached its peak, for the details screen.
ALTER TABLE alerting.alert ADD COLUMN peak_at timestamptz;

CREATE TABLE alerting.alert_event (
    id          bigint           GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    alert_id    uuid             NOT NULL REFERENCES alerting.alert (id) ON DELETE CASCADE,
    -- 1 = opened, 2 = escalated (caution to warning), 3 = eased (warning to caution), 4 = resolved.
    kind        smallint         NOT NULL CHECK (kind BETWEEN 1 AND 4),
    -- The severity after the change; for a resolved alert, the severity it had last.
    severity    smallint         NOT NULL CHECK (severity BETWEEN 1 AND 2),
    -- The measurement that caused the change, in the rule's unit.
    value       double precision NOT NULL,
    at          timestamptz      NOT NULL
);

-- The timeline of one alert.
CREATE INDEX alert_event_alert ON alerting.alert_event (alert_id, at);

-- Alerts opened before this version have no history. Give them the events that can be told from the alert itself,
-- so their timeline is not empty. The opening measurement is not known; the peak stands in for it.
INSERT INTO alerting.alert_event (alert_id, kind, severity, value, at)
SELECT id, 1, severity, peak, opened_at FROM alerting.alert;

INSERT INTO alerting.alert_event (alert_id, kind, severity, value, at)
SELECT id, 4, severity, value, resolved_at FROM alerting.alert WHERE resolved_at IS NOT NULL;

-- Where notifications go. Administrators manage them on the Notifications screen.
CREATE TABLE alerting.channel (
    id                      uuid        PRIMARY KEY,
    name                    text        NOT NULL,
    -- 1 = webhook, 2 = email.
    kind                    smallint    NOT NULL CHECK (kind BETWEEN 1 AND 2),
    enabled                 boolean     NOT NULL,
    send_warning            boolean     NOT NULL,
    send_caution            boolean     NOT NULL,
    send_resolved           boolean     NOT NULL,
    -- Webhook URLs often carry a token, so they are stored encrypted with ASP.NET Data Protection. The hint is the
    -- start of the URL, shown instead of it.
    webhook_url_protected   text,
    webhook_url_hint        text,
    email_recipients        text[],
    created_at              timestamptz NOT NULL,
    updated_at              timestamptz NOT NULL,
    CHECK ((kind = 1 AND webhook_url_protected IS NOT NULL) OR (kind = 2 AND email_recipients IS NOT NULL))
);

-- Names identify channels on the screen and in the alert timeline.
CREATE UNIQUE INDEX channel_name ON alerting.channel (lower(name));

-- One message about one event to one channel.
CREATE TABLE alerting.delivery (
    id                bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    event_id          bigint      NOT NULL REFERENCES alerting.alert_event (id) ON DELETE CASCADE,
    channel_id        uuid        NOT NULL REFERENCES alerting.channel (id) ON DELETE CASCADE,
    -- 0 = pending, 1 = sent, 2 = failed for good.
    state             smallint    NOT NULL DEFAULT 0 CHECK (state BETWEEN 0 AND 2),
    attempts          integer     NOT NULL DEFAULT 0,
    -- When the dispatcher may try next. While a try runs, it is pushed into the future as a lease.
    next_attempt_at   timestamptz NOT NULL,
    last_attempt_at   timestamptz,
    -- The HTTP status of the last webhook try, if there was a response.
    last_status       integer,
    last_error        text,
    sent_at           timestamptz,
    UNIQUE (event_id, channel_id)
);

-- What the dispatcher picks up next.
CREATE INDEX delivery_due ON alerting.delivery (next_attempt_at) WHERE state = 0;

-- Recent results per channel, for the Notifications screen.
CREATE INDEX delivery_channel ON alerting.delivery (channel_id, last_attempt_at);
