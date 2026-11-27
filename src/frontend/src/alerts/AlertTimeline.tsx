import type { AlertDelivery, AlertDetails, AlertEvent, AlertRule } from "../api/types";
import { HelpTip } from "../ui/HelpTip";
import { formatClock, formatCount } from "../logs/format";
import { deliveryNote, deliveryTag, eventTitle } from "./alertFormat";
import { formatMeasure, notFillingDays } from "./ruleWords";
import { isCatalogRule } from "./ruleCatalog";
import { localDay, shortDate } from "../time/zone";

/** Props for {@link AlertTimeline}. */
interface AlertTimelineProps {
  /** The alert, with its events and their messages. */
  alert: AlertDetails;
}

/**
 * What happened to the alert, oldest first: every change of severity, and under each change the messages it sent to
 * channels, with their state. A message that keeps failing says why and when it is tried again. A row of another day
 * than the alert's last check carries its date.
 */
export function AlertTimeline({ alert }: AlertTimelineProps) {
  // The server lists each channel told once (VEY-68).
  const notified = alert.told.length;
  const day = localDay(alert.resolvedAt ?? alert.updatedAt);

  return (
    <section className="glass alert-panel" aria-labelledby="alert-timeline">
      <div className="alert-name">
        <h2 id="alert-timeline" className="cap">
          Timeline
        </h2>
        <HelpTip label="Timeline">
          Every change of the alert, oldest first: when it opened, rose to warning, eased to caution, and resolved.
          Under each change are the messages sent to channels, and whether they went through.
        </HelpTip>
      </div>
      <ol className="timeline">
        {alert.events.flatMap((event) => [
          <EventRow key={`${event.kind}-${event.at}`} event={event} rule={alert.rule} day={day} />,
          ...event.deliveries.map((delivery) => (
            <DeliveryRow
              key={`${event.kind}-${event.at}-${delivery.channelId}`}
              at={event.at}
              delivery={delivery}
              day={day}
            />
          )),
        ])}
      </ol>
      <p className="fine">
        {notified === 0
          ? "No channel was told about this alert. Administrators add channels on the Notifications screen."
          : alert.resolvedAt
            ? "Channels that were told about the alert also heard that it resolved, if they asked to."
            : "When it resolves, every channel that was told about it hears so, if it asked to."}
      </p>
    </section>
  );
}

function EventRow({ event, rule, day }: { event: AlertEvent; rule: AlertRule; day: string }) {
  const tone = event.kind === "Resolved" ? "resolved" : event.severity === "Warning" ? "warning" : "caution";

  return (
    <li className={`timeline-row event ${tone}`}>
      <When at={event.at} day={day} />
      <i className="timeline-dot" aria-hidden="true" />
      <span>
        <b>{eventTitle(event)}</b>
        <small>{eventMeasure(rule, event.value)}</small>
      </span>
    </li>
  );
}

function DeliveryRow({ at, delivery, day }: { at: string; delivery: AlertDelivery; day: string }) {
  const tag = deliveryTag(delivery);
  const note = deliveryNote(delivery);
  const kind = delivery.channelKind === "Email" ? "Email" : "Webhook";
  const time = delivery.sentAt ?? delivery.lastAttemptAt ?? at;

  return (
    <li className={`timeline-row delivery ${tag.tone}`}>
      <When at={time} day={day} />
      <i className="timeline-dot" aria-hidden="true" />
      <span>
        {kind} <b>{delivery.channelName}</b>
        {note ? (
          <small>{note}</small>
        ) : (
          <small>{delivery.attempts > 1 ? `After ${formatCount(delivery.attempts)} tries` : "First try"}</small>
        )}
      </span>
      <span className={`tag ${tag.tone}`}>{tag.text}</span>
    </li>
  );
}

/** The time of a row, with its date above it when it is not on the day of the alert's last check. */
function When({ at, day }: { at: string; day: string }) {
  return (
    <time dateTime={at}>
      {localDay(at) !== day && <span className="timeline-day">{shortDate(at)}</span>}
      {formatClock(at)}
    </time>
  );
}

/** What the rule measured at a change, in its own words: `6.2% errors`, `p95 3.1 s`, `91% in use`, `full in 13 days`. */
function eventMeasure(rule: AlertRule, value: number): string {
  if (isCatalogRule(rule)) {
    return formatMeasure(rule, value);
  }

  switch (rule) {
    case "latency-p95":
      return `p95 ${formatMeasure(rule, value)}`;
    case "disk-space":
      return `${formatMeasure(rule, value)} in use`;
    case "disk-filling":
      return value >= notFillingDays ? "not filling" : `full in ${formatMeasure(rule, value)}`;
    case "error-rate":
      return `${formatMeasure(rule, value)} errors`;
    case "container-restarts":
      return `${formatMeasure(rule, value)} in 15 min`;
    case "container-memory":
      return `${formatMeasure(rule, value)} of its limit`;
    case "db-blocking":
      return `${formatMeasure(rule, value)} blocked`;
    case "db-deadlocks":
      return `${formatMeasure(rule, value)} in 15 min`;
    case "db-long-query":
      return `running for ${formatMeasure(rule, value)}`;
    case "db-backup-age":
      return `no backup for ${formatMeasure(rule, value)}`;
    case "db-agent-job":
      return `last run ${formatMeasure(rule, value)}`;
    case "db-connections":
      return `${formatMeasure(rule, value)} of the maximum`;
    case "db-wraparound":
      return `${formatMeasure(rule, value)} of the limit`;
  }
}
