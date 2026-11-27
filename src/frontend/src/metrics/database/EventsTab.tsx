import { api } from "../../api/client";
import type { MetricRange } from "../../api/types";
import { formatClockTime } from "../metricFormat";
import { rangeWords } from "../metricRange";
import { useMetricData } from "../useMetricData";
import { PanelHead } from "./helpNames";

/** Props for {@link EventsTab}. */
export interface EventsTabProps {
  /** The engine. */
  engine: string;
  /** The server. */
  server: string;
  /** The range. */
  range: MetricRange;
  /** Whether the Advanced view is on. */
  advanced: boolean;
}

/**
 * A SQL Server's Events tab (ADR 0020, area 11): what its built-in system_health session recorded. The Simple view
 * keeps the events that need a look, in words; the Advanced view lists every event with its fields and message.
 */
export function EventsTab({ engine, server, range, advanced }: EventsTabProps) {
  // The server leaves out the events that need nothing in the Simple view (VEY-67).
  const { data, error } = useMetricData(
    `events-${engine}-${server}-${range}`,
    (signal) => api.databaseEvents(engine, server, range, !advanced, signal),
    { choices: String(advanced) },
  );

  if (!data) {
    return error ? (
      <p className="metrics-error" role="alert">
        The events could not be loaded: {error}
      </p>
    ) : null;
  }

  const shown = data;

  return (
    <section className="metric-panel" aria-labelledby="events-head">
      <PanelHead
        id="events-head"
        name="Events"
        help={
          "What SQL Server's built-in system_health session recorded, read every minute: " +
          "errors of severity 20 and above, errors 823 and 824 (damaged pages), memory " +
          "errors, lock waits over 30 seconds, non-yielding schedulers, and security and " +
          "connectivity records. Any severe or memory error needs a DBA at once."
        }
      >
        <span>from SQL Server&apos;s system_health session, {rangeWords(range, "over")}</span>
      </PanelHead>
      {shown.length === 0 ? (
        <p className="metrics-empty">No error, long wait, or failed login {rangeWords(range, "over")}.</p>
      ) : (
        <ul className="live-findings">
          {shown.map((event, index) => (
            <li key={`${event.at}-${index}`} className={`live-finding ${event.severity}`}>
              <i aria-hidden="true" />
              <div>
                <b>
                  {formatClockTime(event.at)} · {event.words}
                </b>
                {advanced && (
                  <p>
                    {event.name}
                    {event.error ? ` · error ${event.error}` : ""}
                    {event.errorSeverity ? ` · severity ${event.errorSeverity}` : ""}
                    {event.wait ? ` · ${event.wait}` : ""}
                    {event.message ? ` · ${event.message}` : ""}
                  </p>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
