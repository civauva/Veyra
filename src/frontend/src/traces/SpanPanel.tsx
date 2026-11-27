import { useState } from "react";
import { Link } from "react-router";

import type { LogEntry, TraceSpan } from "../api/types";
import { HelpTip } from "../ui/HelpTip";
import { formatTime } from "../logs/format";
import { SeverityBadge } from "../logs/SeverityBadge";
import { serviceColor } from "./serviceColors";
import { formatDuration, startOf } from "./traceMath";
import { useViewMode } from "../ui/ViewToggle";

/** How many attributes show before "All attributes". */
const collapsedAttributeCount = 8;

/** Props for {@link SpanPanel}. */
interface SpanPanelProps {
  /** The span. */
  span: TraceSpan;
  /** The trace ID, for links. */
  traceId: string;
  /** The start of the trace, in milliseconds since the epoch. */
  traceStart: number;
  /** How long the trace took, in milliseconds. */
  traceDurationMs: number;
  /** The log records of this span; null while they load. */
  logs: LogEntry[] | null;
  /** Closes the panel. */
  onClose: () => void;
}

/**
 * The selected span: its service, kind, and status, what share of the trace it took, its exception with the stack,
 * its attributes, its events, its log records, and links to similar traces and the service's metrics. On phones it
 * opens as a sheet from the bottom.
 */
export function SpanPanel({ span, traceId, traceStart, traceDurationMs, logs, onClose }: SpanPanelProps) {
  const [mode] = useViewMode();
  const [allAttributes, setAllAttributes] = useState(false);
  const [fullStack, setFullStack] = useState(false);
  const exception = span.events.find((event) => event.name === "exception");
  const otherEvents = span.events.filter((event) => event !== exception);
  // The server sends the attributes sorted by key (VEY-68).
  const attributes = Object.entries(span.attributes);
  const shown = allAttributes ? attributes : attributes.slice(0, collapsedAttributeCount);
  const stack = exception?.attributes["exception.stacktrace"] ?? "";
  const share = traceDurationMs > 0 ? Math.round((span.durationMs / traceDurationMs) * 100) : 100;
  const offset = startOf(span) - traceStart;

  return (
    <aside className="span-panel" aria-labelledby="span-panel-name">
      <div className="span-panel-top">
        <span className="trace-service-tag">
          <i style={{ background: serviceColor(span.service) }} aria-hidden="true" />
          {span.service}
        </span>
        {span.kind !== "Unspecified" && <span className="trace-tag">{span.kind.toLowerCase()}</span>}
        {span.status === "Error" && <span className="trace-tag failed">failed</span>}
        <button type="button" className="detail-close" aria-label="Close the span" onClick={onClose}>
          ×
        </button>
      </div>
      <h2 id="span-panel-name">{span.name}</h2>
      <p className="span-panel-meta">
        {formatDuration(span.durationMs)} · {share}% of the trace · started {formatDuration(offset)} in
        {span.host && ` · ${span.host}`}
      </p>

      <div className="span-actions">
        <Link
          className="button small"
          to={`/traces?service=${encodeURIComponent(span.service)}&name=${encodeURIComponent(span.name)}`}
        >
          Traces like this one
        </Link>
        <Link
          className="button small"
          to={`/metrics/services/${encodeURIComponent(span.service)}${span.environment ? `?environment=${encodeURIComponent(span.environment)}` : ""}`}
        >
          {span.service} metrics
        </Link>
        <button type="button" className="button small" onClick={() => void navigator.clipboard?.writeText(span.spanId)}>
          Copy span ID
        </button>
      </div>

      {exception && (
        <section className="span-exception" aria-label="Exception">
          <p>
            <b>{exception.attributes["exception.type"] ?? "Exception"}</b>{" "}
            <span className="faint">at {formatDuration(Date.parse(exception.timestamp) - traceStart)}</span>
          </p>
          {exception.attributes["exception.message"] && <p>{exception.attributes["exception.message"]}</p>}
          {stack && (
            <>
              <pre className={fullStack ? "" : "clipped"}>{stack}</pre>
              {stack.split("\n").length > 6 && (
                <button type="button" className="link-button" onClick={() => setFullStack(!fullStack)}>
                  {fullStack ? "Show less" : `Show all ${stack.split("\n").length} lines`}
                </button>
              )}
            </>
          )}
        </section>
      )}

      {span.status === "Error" && !exception && span.statusMessage && (
        <section className="span-exception" aria-label="Status">
          <p>{span.statusMessage}</p>
        </section>
      )}

      <section aria-labelledby="span-attributes">
        <div className="span-section-title">
          <h3 id="span-attributes" className="section-title">
            Attributes
          </h3>
          <HelpTip label="Attributes">
            What the instrumentation recorded about this step, such as the HTTP route, the status code, or the database
            statement. The OpenTelemetry SDK of the service sets them.
          </HelpTip>
        </div>
        {attributes.length === 0 ? (
          <p className="filter-note">This span has no attributes.</p>
        ) : (
          <dl className="span-attributes">
            {shown.map(([key, value]) => (
              <div key={key}>
                <dt>{key}</dt>
                <dd className="mono">{value}</dd>
              </div>
            ))}
          </dl>
        )}
        {attributes.length > collapsedAttributeCount && (
          <button type="button" className="link-button" onClick={() => setAllAttributes(!allAttributes)}>
            {allAttributes ? "Show fewer" : `All ${attributes.length} attributes`}
          </button>
        )}
        {span.dropped.attributes + span.dropped.events + span.dropped.links > 0 && (
          <p className="filter-note">
            Left out by the sender or Veyra&apos;s limits: {span.dropped.attributes} attributes, {span.dropped.events}{" "}
            events, {span.dropped.links} links.
          </p>
        )}
      </section>

      {mode === "advanced" && otherEvents.length > 0 && (
        <section aria-labelledby="span-events">
          <div className="span-section-title">
            <h3 id="span-events" className="section-title">
              Events
            </h3>
            <HelpTip label="Events">
              Moments the span marked while it ran, each at its time from the start of the trace. An exception shows
              above, with its message and stack.
            </HelpTip>
          </div>
          <ul className="span-events">
            {otherEvents.map((event) => (
              <li key={`${event.timestamp}-${event.name}`}>
                <span className="mono faint">{formatDuration(Date.parse(event.timestamp) - traceStart)}</span>{" "}
                {event.name}
              </li>
            ))}
          </ul>
        </section>
      )}

      <section aria-labelledby="span-logs">
        <div className="span-section-head">
          <div className="span-section-title">
            <h3 id="span-logs" className="section-title">
              Logs of this span{logs !== null && ` · ${logs.length}`}
            </h3>
            <HelpTip label="Logs of this span">
              The trace&apos;s log records that carry this span ID: the ones written while this step ran. Open in Log
              Explorer shows every record of the trace.
            </HelpTip>
          </div>
          <Link
            className="link-button"
            to={`/logs?traceId=${traceId}&around=${encodeURIComponent(new Date(traceStart).toISOString())}`}
          >
            Open in Log Explorer
          </Link>
        </div>
        {logs === null && <p className="filter-note">Loading…</p>}
        {logs !== null && logs.length === 0 && <p className="filter-note">No log records name this span.</p>}
        <ul className="span-logs">
          {logs?.map((log) => (
            <li key={log.id}>
              <span className="mono faint">{formatTime(log.timestamp)}</span>
              <SeverityBadge severity={log.severity} />
              <span>{log.body}</span>
            </li>
          ))}
        </ul>
      </section>
    </aside>
  );
}
