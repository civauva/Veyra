import type { AlertDelivery, AlertDetails, AlertEvent, AlertRule } from "../api/types";
import { formatClock, formatCount } from "../logs/format";
import { deliveryNote, deliveryTag, eventTitle } from "./alertFormat";
import { formatMeasure, notFillingDays } from "./ruleWords";

/** Props for {@link AlertTimeline}. */
interface AlertTimelineProps {
  /** The alert, with its events and their messages. */
  alert: AlertDetails;
}

/**
 * What happened to the alert, oldest first: every change of severity, and under each change the messages it sent to
 * channels, with their state. A message that keeps failing says why and when it is tried again.
 */
export function AlertTimeline({ alert }: AlertTimelineProps) {
  const notified = new Set(alert.events.flatMap((event) => event.deliveries.map((d) => d.channelId))).size;

  return (
    <section className="glass alert-panel" aria-labelledby="alert-timeline">
      <h2 id="alert-timeline" className="cap">
        Timeline
      </h2>
      <ol className="timeline">
        {alert.events.flatMap((event) => [
          <EventRow key={`${event.kind}-${event.at}`} event={event} rule={alert.rule} />,
          ...event.deliveries.map((delivery) => (
            <DeliveryRow key={`${event.kind}-${event.at}-${delivery.channelId}`} at={event.at} delivery={delivery} />
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

function EventRow({ event, rule }: { event: AlertEvent; rule: AlertRule }) {
  const tone = event.kind === "Resolved" ? "resolved" : event.severity === "Warning" ? "warning" : "caution";

  return (
    <li className={`timeline-row event ${tone}`}>
      <time dateTime={event.at}>{formatClock(event.at)}</time>
      <i className="timeline-dot" aria-hidden="true" />
      <span>
        <b>{eventTitle(event)}</b>
        <small>{eventMeasure(rule, event.value)}</small>
      </span>
    </li>
  );
}

function DeliveryRow({ at, delivery }: { at: string; delivery: AlertDelivery }) {
  const tag = deliveryTag(delivery);
  const note = deliveryNote(delivery);
  const kind = delivery.channelKind === "Email" ? "Email" : "Webhook";
  const time = delivery.sentAt ?? delivery.lastAttemptAt ?? at;

  return (
    <li className={`timeline-row delivery ${tag.tone}`}>
      <time dateTime={time}>{formatClock(time)}</time>
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

/** What the rule measured at a change, in its own words: `6.2% errors`, `p95 3.1 s`, `91% in use`, `full in 13 days`. */
function eventMeasure(rule: AlertRule, value: number): string {
  switch (rule) {
    case "latency-p95":
      return `p95 ${formatMeasure(rule, value)}`;
    case "disk-space":
      return `${formatMeasure(rule, value)} in use`;
    case "disk-filling":
      return value >= notFillingDays ? "not filling" : `full in ${formatMeasure(rule, value)}`;
    case "error-rate":
      return `${formatMeasure(rule, value)} errors`;
  }
}
