import { api } from "../../api/client";
import type { MetricRange } from "../../api/types";
import { formatClockTime } from "../metricFormat";
import { useMetricData } from "../useMetricData";

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
  const { data, error } = useMetricData(`events-${engine}-${server}-${range}`, (signal) =>
    api.databaseEvents(engine, server, range, signal),
  );

  if (!data) {
    return error ? (
      <p className="metrics-error" role="alert">
        The events could not be loaded: {error}
      </p>
    ) : null;
  }

  const shown = advanced ? data : data.filter((event) => event.severity !== "ok");

  return (
    <section className="metric-panel" aria-labelledby="events-head">
      <div className="metric-panel-head">
        <h2 id="events-head">Events</h2>
        <span>from SQL Server&apos;s system_health session, over the last {range}</span>
      </div>
      {shown.length === 0 ? (
        <p className="metrics-empty">No error, long wait, or failed login over the last {range}.</p>
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
