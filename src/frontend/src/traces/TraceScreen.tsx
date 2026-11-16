import { useState } from "react";
import { Link, useParams, useSearchParams } from "react-router";

import { api } from "../api/client";
import type { LogEntry, Trace, TraceSpan } from "../api/types";
import { formatInstant, formatTime } from "../logs/format";
import "../logs/logs.css";
import { SeverityBadge } from "../logs/SeverityBadge";
import { useMetricData } from "../metrics/useMetricData";
import { serviceColor } from "./serviceColors";
import { SpanPanel } from "./SpanPanel";
import { formatDuration, traceBounds, waterfallRows } from "./traceMath";
import "./traces.css";
import { Waterfall } from "./Waterfall";
import { useViewMode, ViewToggle } from "../app/ViewToggle";

/** The tabs of the trace view. */
type TraceTab = "spans" | "logs" | "attributes";

/**
 * One trace (ADR 0014, VEY-27 mockups): its spans on a waterfall with the critical path, the selected span beside it,
 * and the trace's log records. The trace is read again every 30 seconds, because spans of other services may still
 * be arriving; the view says when part of it is missing instead of hiding the gap.
 */
export function TraceScreen() {
  const { traceId = "" } = useParams();
  const [searchParams] = useSearchParams();
  const at = searchParams.get("at");
  const trace = useMetricData(`trace-${traceId}-${at ?? ""}`, (signal) => api.trace(traceId, at, signal));

  if (trace.notFound) {
    return (
      <main className="trace-view trace-missing">
        <p className="crumbs">
          <Link to="/traces">‹ Traces</Link>
        </p>
        <h1>Trace not found</h1>
        <p>
          No span of trace <span className="mono">{traceId}</span> is stored. It may be older than the traces kept, or
          its services did not sample it.
        </p>
      </main>
    );
  }

  if (trace.data === null) {
    return (
      <main className="trace-view" aria-busy="true">
        <p className="crumbs">
          <Link to="/traces">‹ Traces</Link>
        </p>
        {trace.error ? (
          <p className="explorer-error" role="alert">
            {trace.error}
          </p>
        ) : (
          <p className="filter-note">Loading the trace…</p>
        )}
      </main>
    );
  }

  // The API answers 404 for a trace without spans, so a root is always there; the first span stands in for a missing one.
  const root = trace.data.spans.find((span) => span.parentSpanId === null) ?? trace.data.spans[0];

  if (!root) {
    return null;
  }

  return <TraceView key={trace.data.traceId} trace={trace.data} root={root} error={trace.error} />;
}

/** Props for {@link TraceView}. */
interface TraceViewProps {
  /** The trace. */
  trace: Trace;
  /** Its root span, or its first span when the root has not arrived. */
  root: TraceSpan;
  /** Why the latest refresh failed, or null. */
  error: string | null;
}

/** The loaded trace: header, notice, tabs, waterfall, and the selected span. */
function TraceView({ trace, root, error }: TraceViewProps) {
  const [mode] = useViewMode();
  const advanced = mode === "advanced";
  const spans = trace.spans;
  const bounds = traceBounds(spans);
  const failed = spans.filter((span) => span.status === "Error");
  const orphans = waterfallRows(spans).filter((row) => row.missingParent);
  const services = countServices(spans);
  const [tab, setTab] = useState<TraceTab>("spans");
  // Beside the waterfall, the cause is the first thing worth reading; on phones the span is a sheet over the
  // waterfall, so it opens only when the person taps a span.
  const [selected, setSelected] = useState<string | null>(() =>
    besideTheWaterfall() ? (deepestFailure(spans)?.spanId ?? root.spanId) : null,
  );
  const [showCriticalPath, setShowCriticalPath] = useState(true);
  const [errorsOnly, setErrorsOnly] = useState(false);
  const from = new Date(bounds.start - 60_000).toISOString();
  const to = new Date(bounds.start + bounds.durationMs + 60_000).toISOString();
  const logs = useMetricData(`trace-logs-${trace.traceId}-${from}`, (signal) =>
    api.searchLogs(new URLSearchParams({ traceId: trace.traceId, from, to, limit: "500" }), signal),
  );
  const logItems = logs.data?.items ?? null;
  const selectedSpan = spans.find((span) => span.spanId === selected) ?? null;

  return (
    <main className={selectedSpan ? "trace-view has-span" : "trace-view"}>
      <section className="trace-main">
        <p className="crumbs">
          <Link to="/traces">‹ Traces</Link> · {root.service}
        </p>
        <header className="trace-header">
          <ViewToggle className="trace-view-toggle" />
          <div>
            <h1>
              <span
                className={root.status === "Error" ? "trace-status-dot failed" : "trace-status-dot"}
                aria-hidden="true"
              />
              {root.name}
            </h1>
            <p className="trace-meta">
              <span className="trace-service-tag">
                <i style={{ background: serviceColor(root.service) }} aria-hidden="true" />
                {root.service}
              </span>
              {root.environment && <span>{root.environment}</span>}
              <span>{formatInstant(root.start)}</span>
              <span className="mono">{trace.traceId}</span>
              <button
                type="button"
                className="link-button"
                onClick={() => void navigator.clipboard?.writeText(window.location.href)}
              >
                Copy link
              </button>
            </p>
          </div>
          <dl className="trace-stats">
            <div>
              <dt>Duration</dt>
              <dd>{formatDuration(bounds.durationMs)}</dd>
            </div>
            <div>
              <dt>Spans</dt>
              <dd>{spans.length}</dd>
            </div>
            <div>
              <dt>Services</dt>
              <dd>{services.length}</dd>
            </div>
            <div className={failed.length > 0 ? "failed" : ""}>
              <dt>Failed</dt>
              <dd>{failed.length}</dd>
            </div>
          </dl>
        </header>

        {error && (
          <p className="explorer-error" role="alert">
            {error}
          </p>
        )}

        {orphans.length > 0 && (
          <p className="trace-notice" role="status">
            <b>Part of this trace has not arrived.</b> {orphans.length === 1 ? "1 span" : `${orphans.length} spans`} of{" "}
            {[...new Set(orphans.map((row) => row.span.service))].join(", ")} point to a parent Veyra never received. It
            may still arrive, or its service did not sample it.
          </p>
        )}

        <div className="trace-tabs" role="tablist" aria-label="Trace">
          <button type="button" role="tab" aria-selected={tab === "spans"} onClick={() => setTab("spans")}>
            Spans
          </button>
          <button type="button" role="tab" aria-selected={tab === "logs"} onClick={() => setTab("logs")}>
            Logs of this trace{logItems !== null && ` · ${logItems.length}`}
          </button>
          {advanced && (
            <button type="button" role="tab" aria-selected={tab === "attributes"} onClick={() => setTab("attributes")}>
              Attributes
            </button>
          )}
          {(tab === "spans" || (tab === "attributes" && !advanced)) && (
            <span className="trace-toggles">
              <button
                type="button"
                className="chip-button trace-critical"
                aria-pressed={showCriticalPath}
                onClick={() => setShowCriticalPath(!showCriticalPath)}
              >
                Critical path
              </button>
              <button
                type="button"
                className="chip-button"
                aria-pressed={errorsOnly}
                onClick={() => setErrorsOnly(!errorsOnly)}
                disabled={failed.length === 0}
              >
                Errors only
              </button>
            </span>
          )}
        </div>

        {tab === "spans" && (
          <div role="tabpanel" aria-label="Spans" className="trace-panel">
            <p className="trace-services">
              {services.map(([service, count]) => (
                <span key={service} className="trace-service-tag">
                  <i style={{ background: serviceColor(service) }} aria-hidden="true" />
                  {service} <span className="faint">{count}</span>
                </span>
              ))}
              {showCriticalPath && (
                <span className="faint trace-services-note">
                  Bright spans are the critical path: the chain that decided how long it took.
                </span>
              )}
            </p>
            <Waterfall
              spans={spans}
              selected={selected}
              onSelect={setSelected}
              showCriticalPath={showCriticalPath}
              errorsOnly={errorsOnly}
            />
          </div>
        )}

        {tab === "logs" && (
          <div role="tabpanel" aria-label="Logs of this trace" className="trace-panel">
            <TraceLogs
              logs={logItems}
              spans={spans}
              onSelect={(spanId) => {
                setSelected(spanId);
                setTab("spans");
              }}
            />
          </div>
        )}

        {tab === "attributes" && advanced && (
          <div role="tabpanel" aria-label="Attributes" className="trace-panel">
            <h3 className="section-title">Resource of {root.service}</h3>
            <dl className="span-attributes">
              {Object.entries(root.resourceAttributes)
                .sort(([a], [b]) => a.localeCompare(b))
                .map(([key, value]) => (
                  <div key={key}>
                    <dt>{key}</dt>
                    <dd className="mono">{value}</dd>
                  </div>
                ))}
            </dl>
          </div>
        )}
      </section>

      {selectedSpan && (
        <SpanPanel
          span={selectedSpan}
          traceId={trace.traceId}
          traceStart={bounds.start}
          traceDurationMs={bounds.durationMs}
          logs={logItems === null ? null : logItems.filter((log) => log.spanId === selectedSpan.spanId)}
          onClose={() => setSelected(null)}
        />
      )}
    </main>
  );
}

/** Props for {@link TraceLogs}. */
interface TraceLogsProps {
  /** The trace's log records, oldest first after sorting; null while they load. */
  logs: LogEntry[] | null;
  /** The spans, to name each record's span. */
  spans: TraceSpan[];
  /** Selects the record's span on the waterfall. */
  onSelect: (spanId: string) => void;
}

/** The log records of the trace in time order, each naming its span. */
function TraceLogs({ logs, spans, onSelect }: TraceLogsProps) {
  if (logs === null) {
    return <p className="filter-note">Loading the log records…</p>;
  }

  if (logs.length === 0) {
    return <p className="filter-note">No log record names this trace.</p>;
  }

  return (
    <ul className="trace-logs">
      {[...logs]
        .sort((a, b) => Date.parse(a.timestamp) - Date.parse(b.timestamp))
        .map((log) => {
          const span = spans.find((candidate) => candidate.spanId === log.spanId);

          return (
            <li key={log.id}>
              <span className="mono faint">{formatTime(log.timestamp)}</span>
              <SeverityBadge severity={log.severity} />
              <span className="trace-log-body">{log.body}</span>
              {span && (
                <button type="button" className="link-button" onClick={() => onSelect(span.spanId)}>
                  {span.service} · {span.name}
                </button>
              )}
            </li>
          );
        })}
    </ul>
  );
}

/** Whether the span panel sits beside the waterfall rather than over it, as on phones. */
function besideTheWaterfall(): boolean {
  return typeof window.matchMedia !== "function" || !window.matchMedia("(max-width: 767px)").matches;
}

/** The services of a trace with their span counts, most spans first. */
function countServices(spans: TraceSpan[]): [string, number][] {
  const counts = new Map<string, number>();

  for (const span of spans) {
    counts.set(span.service, (counts.get(span.service) ?? 0) + 1);
  }

  return [...counts].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]));
}

/**
 * The failed span none of whose children failed: the cause rather than the requests that passed it on. Spans with an
 * exception come first, because an exception explains more than a bare "timeout" on the call below it; the API picks
 * the list's cause the same way.
 */
function deepestFailure(spans: TraceSpan[]): TraceSpan | undefined {
  const failed = spans.filter((span) => span.status === "Error");
  const explained = failed.filter((span) => span.events.some((event) => event.name === "exception"));
  const pool = explained.length > 0 ? explained : failed;
  const parents = new Set(pool.map((span) => span.parentSpanId));

  return pool.find((span) => !parents.has(span.spanId)) ?? pool[0];
}
