import { useState, type ReactNode } from "react";
import { useParams, useSearchParams } from "react-router";

import type { LogEntry, Trace, TraceSpan } from "../api/types";
import { HelpTip } from "../ui/HelpTip";
import { ScreenBar } from "../ui/ScreenBar";
import { formatInstant, formatTime } from "../logs/format";
import "../logs/logs.css";
import { SeverityBadge } from "../logs/SeverityBadge";
import { serviceColor } from "./serviceColors";
import { SpanPanel } from "./SpanPanel";
import { formatDuration } from "./traceMath";
import "./traces.css";
import { useTrace, useTraceRecords } from "./useTrace";
import { Waterfall } from "./Waterfall";
import { useViewMode } from "../ui/ViewToggle";

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
  const [errorsOnly, setErrorsOnly] = useState(false);
  const trace = useTrace(traceId, at, errorsOnly);

  if (trace.notFound) {
    return (
      <main className="trace-view trace-missing">
        <ScreenBar trail={[{ label: "Traces", to: "/traces" }, { label: "Trace" }]} view />
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
        <ScreenBar trail={[{ label: "Traces", to: "/traces" }, { label: "Trace" }]} view />
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

  return (
    <TraceView
      key={trace.data.traceId}
      trace={trace.data}
      root={root}
      error={trace.error}
      errorsOnly={errorsOnly}
      onErrorsOnly={setErrorsOnly}
    />
  );
}

/** Props for {@link TraceView}. */
interface TraceViewProps {
  /** The trace. */
  trace: Trace;
  /** Its root span, or its first span when the root has not arrived. */
  root: TraceSpan;
  /** Why the latest refresh failed, or null. */
  error: string | null;
  /** Whether the server was asked for the failed spans and the spans above them only. */
  errorsOnly: boolean;
  /** Asks for the failed spans only, or for every span again. */
  onErrorsOnly: (errorsOnly: boolean) => void;
}

/** The loaded trace: header, notice, tabs, waterfall, and the selected span. */
function TraceView({ trace, root, error, errorsOnly, onErrorsOnly }: TraceViewProps) {
  const [mode] = useViewMode();
  const advanced = mode === "advanced";
  const { spans, summary } = trace;
  const bounds = { start: Date.parse(summary.start), durationMs: summary.durationMs };
  const [tab, setTab] = useState<TraceTab>("spans");
  // Beside the waterfall, the cause is the first thing worth reading; on phones the span is a sheet over the
  // waterfall, so it opens only when the person taps a span.
  const [selected, setSelected] = useState<string | null>(() =>
    besideTheWaterfall() ? (summary.cause ?? root.spanId) : null,
  );
  const [showCriticalPath, setShowCriticalPath] = useState(true);
  const records = useTraceRecords(trace, selected);
  const logItems = records.logs;
  const selectedSpan = spans.find((span) => span.spanId === selected) ?? null;
  const spanLogItems = records.spanLogs;

  return (
    <main className={selectedSpan ? "trace-view has-span" : "trace-view"}>
      <ScreenBar trail={[{ label: "Traces", to: "/traces" }, { label: root.name }]} view />
      <section className="trace-main">
        <header className="trace-header">
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
              <StatName name="Duration">
                How long the request took: from the start of its first span to the end of its last, as far as its spans
                have arrived.
              </StatName>
              <dd>{formatDuration(bounds.durationMs)}</dd>
            </div>
            <div>
              <StatName name="Spans">
                The steps of the request that have arrived. Each span is one piece of work a service did, such as
                answering a call or running a query.
              </StatName>
              <dd>{summary.spans}</dd>
            </div>
            <div>
              <StatName name="Services">
                How many services took part in the request. Each has its own colour in the waterfall below.
              </StatName>
              <dd>{summary.services.length}</dd>
            </div>
            <div className={summary.failed > 0 ? "failed" : ""}>
              <StatName name="Failed">
                Spans that ended in an error. A failure often passes up through the spans above it, so the trace opens
                on the deepest one, where it most likely began.
              </StatName>
              <dd>{summary.failed}</dd>
            </div>
          </dl>
        </header>

        {error && (
          <p className="explorer-error" role="alert">
            {error}
          </p>
        )}

        {summary.missingParents > 0 && (
          <p className="trace-notice" role="status">
            <b>Part of this trace has not arrived.</b>{" "}
            {summary.missingParents === 1 ? "1 span" : `${summary.missingParents} spans`} of{" "}
            {summary.missingParentServices.join(", ")} point to a parent Veyra never received. It may still arrive, or
            its service did not sample it.
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
              <span className="trace-critical-help">
                <button
                  type="button"
                  className="chip-button trace-critical"
                  aria-pressed={showCriticalPath}
                  onClick={() => setShowCriticalPath(!showCriticalPath)}
                >
                  Critical path
                </button>
                <HelpTip label="Critical path">
                  The chain of spans that decided how long the request took, each waiting on the next. Making one of
                  them faster makes the request faster; the spans beside the path ran alongside it and would not have.
                </HelpTip>
              </span>
              <button
                type="button"
                className="chip-button"
                aria-pressed={errorsOnly}
                onClick={() => onErrorsOnly(!errorsOnly)}
                disabled={summary.failed === 0}
              >
                Errors only
              </button>
            </span>
          )}
        </div>

        {tab === "spans" && (
          <div role="tabpanel" aria-label="Spans" className="trace-panel">
            <p className="trace-services">
              {summary.services.map(({ service, spans: count }) => (
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
              summary={summary}
              selected={selected}
              onSelect={setSelected}
              showCriticalPath={showCriticalPath}
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
            <div className="trace-panel-head">
              <h3 className="section-title">Resource of {root.service}</h3>
              <HelpTip label="Resource">
                What sent the trace&apos;s first span, the same for everything that process sends: service.name, its
                version, host.name, and the like. The OpenTelemetry SDK or the Collector sets them.
              </HelpTip>
            </div>
            <dl className="span-attributes">
              {Object.entries(root.resourceAttributes).map(([key, value]) => (
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
          logs={spanLogItems}
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
      {logs.map((log) => {
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

/** Props for {@link StatName}. */
interface StatNameProps {
  /** The number's name, such as "Duration". */
  name: string;
  /** What the number means, behind the question mark. */
  children: ReactNode;
}

/** The name of one of the trace's numbers, with a question mark that explains it (VEY-60). */
function StatName({ name, children }: StatNameProps) {
  return (
    <dt>
      <span>{name}</span>
      <HelpTip label={name}>{children}</HelpTip>
    </dt>
  );
}

/** Whether the span panel sits beside the waterfall rather than over it, as on phones. */
function besideTheWaterfall(): boolean {
  return typeof window.matchMedia !== "function" || !window.matchMedia("(max-width: 767px)").matches;
}
