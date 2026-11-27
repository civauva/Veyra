import { useState } from "react";
import { Link, useParams, useSearchParams } from "react-router";

import type { LogEntry, Trace, TraceSpan } from "../../../api/types";
import { formatCount, formatTime } from "../../../logs/format";
import { levelName } from "../../../logs/levelWords";
import { termText } from "../../../logs/queryText";
import { criticalShare, firstException, shortTraceId, traceLevels } from "../../../traces/traceFigures";
import { endOf, formatDuration, startOf, waterfallRows } from "../../../traces/traceMath";
import { clockSeconds } from "../../../traces/traceWords";
import { useTrace, useTraceRecords } from "../../../traces/useTrace";
import { HelpTip } from "../../../ui/HelpTip";
import { ScreenBar } from "../../../ui/ScreenBar";
import { useViewMode } from "../../../ui/ViewToggle";
import { ConsoleIcon } from "../ConsoleIcon";
import { SqlFigure } from "../database/sqlParts";
import "./consoleTraces.css";

/** The tabs of a trace. */
type TraceTab = "spans" | "logs" | "attributes";

/**
 * Console's trace (VEY-79, ADR 0029): its head with the facts and the ways onward, the figures of the trace, and tabs
 * for the spans, the trace's records, and its resource. The waterfall marks the critical path; the chosen span shows
 * its attributes, its exception, and its records. The trace is read again every 30 seconds, as spans may still arrive.
 */
export function ConsoleTrace() {
  const { traceId = "" } = useParams();
  const [params] = useSearchParams();
  const at = params.get("at");
  const [errorsOnly, setErrorsOnly] = useState(false);
  const trace = useTrace(traceId, at, errorsOnly);
  const trail = [{ label: "Traces", to: "/traces" }, { label: traceId.slice(0, 8) || "Trace" }];

  if (trace.notFound) {
    return (
      <main className="cn-screen sq tr">
        <ScreenBar trail={trail} view />
        <p className="cn-empty">
          No span of trace <span className="mono">{traceId}</span> is stored. It may be older than the traces kept, or
          its services did not sample it.
        </p>
      </main>
    );
  }

  if (trace.data === null) {
    return (
      <main className="cn-screen sq tr" aria-busy="true">
        <ScreenBar trail={trail} view />
        {trace.error ? (
          <p className="cn-error" role="alert">
            {trace.error}
          </p>
        ) : (
          <p className="cn-empty">Reading the trace…</p>
        )}
      </main>
    );
  }

  // The server answers 404 for a trace without spans; the first span stands in for a root that has not arrived.
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

/** The trace once it arrived: head, figures, tabs, and the tab's panels. */
function TraceView({ trace, root, error, errorsOnly, onErrorsOnly }: TraceViewProps) {
  const [mode] = useViewMode();
  const advanced = mode === "advanced";
  const { spans, summary } = trace;
  const [tab, setTab] = useState<TraceTab>("spans");
  // The cause is the first thing worth reading, so the trace opens on it, or on its root when nothing failed.
  const [selected, setSelected] = useState<string | null>(summary.cause ?? root.spanId);
  const records = useTraceRecords(trace, selected);
  const failed = summary.failed > 0;
  const exception = firstException(trace);
  const critical = criticalShare(trace);
  const [copied, setCopied] = useState(false);
  const selectedSpan = spans.find((span) => span.spanId === selected) ?? null;
  const logsOf = (spanId: string | null) =>
    `/logs?${new URLSearchParams({
      q: [termText("trace", trace.traceId), spanId ? termText("span", spanId) : null].filter(Boolean).join(" AND "),
      ...records.window,
    }).toString()}`;

  return (
    <main className="cn-screen sq tr">
      <ScreenBar trail={[{ label: "Traces", to: "/traces" }, { label: trace.traceId.slice(0, 8) }]} view />
      <div className="sq-head">
        <span className={failed ? "sq-icon tr-icon bad" : "sq-icon tr-icon"} aria-hidden="true">
          <ConsoleIcon name="traces" size={22} />
        </span>
        <div className="tr-head-text">
          <h1>
            {root.name}
            <span className={failed ? "cn-pill bad" : "cn-pill ok"}>{failed ? "Failed" : "OK"}</span>
          </h1>
          <p className="sq-facts tr-facts">
            <span className="mono" title={trace.traceId}>
              {shortTraceId(trace.traceId)}
            </span>
            <span>Started {clockSeconds(summary.start)}</span>
            <span>{formatDuration(summary.durationMs)}</span>
            <span>{summary.services.map((share) => share.service).join(" → ")}</span>
          </p>
        </div>
        <div className="cn-head-actions tr-actions">
          <Link className="cn-button" to={logsOf(null)}>
            <ConsoleIcon name="logs" />
            {records.logs === null ? "Logs" : `${formatCount(records.logs.length)} logs`}
          </Link>
          <Link className="cn-button" to={`/metrics/services/${encodeURIComponent(root.service)}`}>
            <ConsoleIcon name="services" />
            Open {root.service}
          </Link>
          <button
            type="button"
            className="cn-button"
            onClick={() => {
              void navigator.clipboard?.writeText(window.location.href);
              setCopied(true);
            }}
          >
            {copied ? "Link copied" : "Copy link"}
          </button>
        </div>
      </div>

      {error && (
        <p className="cn-error" role="alert">
          Could not refresh: {error}
        </p>
      )}

      <section className="sq-figures tr-figures" aria-label="Figures">
        <SqlFigure
          label="Duration"
          value={formatDuration(summary.durationMs)}
          detail="from the first span to the last"
          tone={failed ? "bad" : undefined}
          help="How long the request took: from the start of its first span to the end of its last, as far as its spans have arrived."
        />
        <SqlFigure
          label="Spans"
          value={formatCount(summary.spans)}
          detail={`${summary.services.length} ${summary.services.length === 1 ? "service" : "services"} · ${traceLevels(spans)} levels`}
          help="The steps of the request that have arrived, how many services sent them, and how deep they nest. Each span is one piece of work, such as answering a call or running a query."
        />
        <SqlFigure
          label="Errors"
          value={formatCount(summary.failed)}
          detail={exception ? `${exception.type} in ${exception.service}` : "no span failed"}
          tone={failed ? "bad" : undefined}
          help="Spans that ended in an error, and the exception of the one where the failure most likely began: the deepest failed span, which the server names."
        />
        <SqlFigure
          label="Critical path"
          value={critical.share === null ? "—" : `${Math.round(critical.share * 100)}%`}
          detail={
            critical.spans.length === 1
              ? `in ${critical.spans[0]?.name ?? "one span"}`
              : `in the ${critical.spans.length} spans it waited on`
          }
          help="The share of the trace's duration taken by the spans at the end of its critical path: the chain each step waited on. Making those faster makes the request faster."
        />
      </section>

      {summary.missingParents > 0 && (
        <p className="tr-notice" role="status">
          <b>Part of this trace has not arrived.</b>{" "}
          {summary.missingParents === 1 ? "1 span" : `${summary.missingParents} spans`} of{" "}
          {summary.missingParentServices.join(", ")} point to a parent Veyra never received. It may still arrive, or its
          service did not sample it.
        </p>
      )}

      <div className="cn-tabs tr-tabs" role="tablist" aria-label="Trace">
        <button type="button" role="tab" aria-selected={tab === "spans"} onClick={() => setTab("spans")}>
          Spans
        </button>
        <button type="button" role="tab" aria-selected={tab === "logs"} onClick={() => setTab("logs")}>
          Logs of this trace{records.logs !== null && <small>{formatCount(records.logs.length)}</small>}
        </button>
        {advanced && (
          <button type="button" role="tab" aria-selected={tab === "attributes"} onClick={() => setTab("attributes")}>
            Attributes
          </button>
        )}
      </div>

      {(tab === "spans" || (tab === "attributes" && !advanced)) && (
        <div className="sq-row tr-main" role="tabpanel" aria-label="Spans">
          <Waterfall
            trace={trace}
            selected={selected}
            errorsOnly={errorsOnly}
            onSelect={setSelected}
            onErrorsOnly={onErrorsOnly}
          />
          <div className="tr-side">
            {selectedSpan && (
              <SpanPanel
                span={selectedSpan}
                traceStart={Date.parse(summary.start)}
                logs={records.spanLogs}
                logsLink={logsOf(selectedSpan.spanId)}
              />
            )}
            <Services trace={trace} />
          </div>
        </div>
      )}

      {tab === "logs" && (
        <section className="cn-card" role="tabpanel" aria-labelledby="tr-records">
          <header>
            <h2 id="tr-records">Logs of this trace</h2>
            <HelpTip label="Logs of this trace">
              The log records that carry this trace&apos;s ID, oldest first, each with the span it was written in. The
              server finds them from a minute before the trace to a minute after it.
            </HelpTip>
            <Link className="cn-meta" to={logsOf(null)}>
              Open in Logs →
            </Link>
          </header>
          <Records
            logs={records.logs}
            spans={spans}
            onSpan={(spanId) => {
              setSelected(spanId);
              setTab("spans");
            }}
          />
        </section>
      )}

      {tab === "attributes" && advanced && (
        <section className="cn-card" role="tabpanel" aria-labelledby="tr-resource">
          <header>
            <h2 id="tr-resource">Resource of {root.service}</h2>
            <HelpTip label="Resource">
              What sent the trace&apos;s first span, the same for everything that process sends: service.name, its
              version, host.name, and the like. The OpenTelemetry SDK or the Collector sets them.
            </HelpTip>
          </header>
          <div className="sq-pad">
            <Attributes values={root.resourceAttributes} />
          </div>
        </section>
      )}
    </main>
  );
}

/** Props for {@link Waterfall}. */
interface WaterfallProps {
  /** The trace, with the spans the server listed for the view and the figures of every span. */
  trace: Trace;
  /** The chosen span, or null. */
  selected: string | null;
  /** Whether only the failed spans and those above them are listed. */
  errorsOnly: boolean;
  /** Called with the span the person picks. */
  onSelect: (spanId: string) => void;
  /** Asks for the failed spans only, or for every span again. */
  onErrorsOnly: (errorsOnly: boolean) => void;
}

/**
 * The spans on one timeline, in the server's tree order, indented under their parents. The spans of the critical
 * path carry a mark; failed spans are red. A span with children folds them away.
 */
function Waterfall({ trace, selected, errorsOnly, onSelect, onErrorsOnly }: WaterfallProps) {
  const { spans, summary } = trace;
  const [collapsed, setCollapsed] = useState<Set<string>>(new Set());
  const path = new Set(summary.criticalPath);
  const start = Date.parse(summary.start);
  const length = Math.max(summary.durationMs, 0.001);
  const at = (time: number) => ((time - start) / length) * 100;
  const rows = waterfallRows(spans, collapsed);

  function fold(spanId: string) {
    const next = new Set(collapsed);

    if (next.has(spanId)) {
      next.delete(spanId);
    } else {
      next.add(spanId);
    }

    setCollapsed(next);
  }

  return (
    <section className="cn-card" aria-labelledby="tr-waterfall">
      <header>
        <h2 id="tr-waterfall">Waterfall</h2>
        <HelpTip label="Waterfall">
          Every span of the trace on one timeline, indented under the span that called it. The bar shows when it ran and
          for how long; red ones failed. The mark at the left is the critical path: the chain of spans the trace waited
          on. Errors only asks the server for the failed spans and the spans above them.
        </HelpTip>
        <span className="cn-meta">
          <button
            type="button"
            className="tr-errors"
            aria-pressed={errorsOnly}
            disabled={summary.failed === 0 && !errorsOnly}
            onClick={() => onErrorsOnly(!errorsOnly)}
          >
            Errors only
          </button>{" "}
          {formatCount(spans.length)} of {formatCount(summary.spans)} spans
        </span>
      </header>
      <div className="tr-wf-row tr-wf-head" aria-hidden="true">
        <span>Span</span>
        <span className="tr-wf-ticks">
          {[0, 1 / 3, 2 / 3, 1].map((share) => (
            <span key={share}>{share === 0 ? "0" : formatDuration(summary.durationMs * share)}</span>
          ))}
        </span>
        <span className="tr-wf-time">Time</span>
      </div>
      <ul className="tr-waterfall" aria-label="Spans">
        {rows.map((row) => {
          const span = row.span;
          const left = at(startOf(span));
          const width = Math.max(0.3, at(endOf(span)) - left);
          const onPath = path.has(span.spanId);
          const bad = span.status === "Error";

          return (
            <li key={span.spanId} className={span.spanId === selected ? "tr-wf-row selected" : "tr-wf-row"}>
              <span className="tr-wf-name" style={{ paddingLeft: `${row.depth * 14}px` }}>
                {row.children > 0 ? (
                  <button
                    type="button"
                    className="tr-wf-twist"
                    aria-expanded={!collapsed.has(span.spanId)}
                    aria-label={`${collapsed.has(span.spanId) ? "Show" : "Hide"} the ${row.children} spans under ${span.name}`}
                    onClick={() => fold(span.spanId)}
                  >
                    {collapsed.has(span.spanId) ? "▸" : "▾"}
                  </button>
                ) : (
                  <span className="tr-wf-twist" />
                )}
                {onPath ? (
                  <i className="tr-wf-mark on" title="On the critical path" />
                ) : (
                  <i className="tr-wf-mark" aria-hidden="true" />
                )}
                <button
                  type="button"
                  className="tr-wf-pick"
                  aria-pressed={span.spanId === selected}
                  onClick={() => onSelect(span.spanId)}
                >
                  {span.name} <small>{span.service}</small>
                  {row.missingParent && <small> · parent not received</small>}
                </button>
              </span>
              <span className="tr-wf-track" aria-hidden="true">
                <i
                  className={bad ? "bad" : onPath ? "critical" : undefined}
                  style={{ left: `${left}%`, width: `${Math.min(width, 100 - left)}%` }}
                />
              </span>
              <span className={bad ? "tr-wf-time cn-bad" : "tr-wf-time"}>{formatDuration(span.durationMs)}</span>
            </li>
          );
        })}
      </ul>
      <footer>
        <span>
          <i className="tr-wf-mark on tr-wf-key" aria-hidden="true" /> marks the critical path: the spans the trace
          waited on
        </span>
        {errorsOnly && (
          <button type="button" className="link-button" onClick={() => onErrorsOnly(false)}>
            Show all {formatCount(summary.spans)} →
          </button>
        )}
      </footer>
    </section>
  );
}

/** Props for {@link SpanPanel}. */
interface SpanPanelProps {
  /** The span. */
  span: TraceSpan;
  /** When the trace started, in milliseconds since the epoch. */
  traceStart: number;
  /** The span's own records; null while they load. */
  logs: LogEntry[] | null;
  /** The Log explorer with the span's records. */
  logsLink: string;
}

/** The chosen span: its facts and attributes, its exception, and a link to its records. */
function SpanPanel({ span, traceStart, logs, logsLink }: SpanPanelProps) {
  const exception = span.events.find((event) => event.name === "exception");
  const failed = span.status === "Error";

  return (
    <section className="cn-card" aria-labelledby="tr-span">
      <header>
        <h2 id="tr-span">{span.name}</h2>
        <HelpTip label="Selected span">
          The span picked on the waterfall: the service that sent it, what kind of step it was, when it started after
          the trace began, how long it took, how it ended, and the attributes its instrumentation recorded.
        </HelpTip>
        <span className="cn-meta">selected span</span>
      </header>
      <div className="sq-pad">
        <dl className="tr-kv">
          <dt>Service</dt>
          <dd>{span.service}</dd>
          <dt>Kind</dt>
          <dd>{span.kind}</dd>
          <dt>Started</dt>
          <dd>+{formatDuration(startOf(span) - traceStart)}</dd>
          <dt>Duration</dt>
          <dd>{formatDuration(span.durationMs)}</dd>
          <dt>Status</dt>
          <dd className={failed ? "cn-bad" : undefined}>{failed ? "Error" : span.status === "Ok" ? "OK" : "Unset"}</dd>
          {/* The server sends the attributes sorted by key (VEY-68). */}
          {Object.entries(span.attributes).map(([key, value]) => (
            <AttributeRow key={key} name={key} value={value} />
          ))}
        </dl>
        {(exception || (failed && span.statusMessage)) && (
          <div className="tr-banner" role="note">
            <b>{exception?.attributes["exception.type"] ?? "Error"}</b>
            <small>{exception?.attributes["exception.message"] ?? span.statusMessage}</small>
          </div>
        )}
      </div>
      <footer>
        <span>Logs of this span</span>
        <Link className="link-button" to={logsLink}>
          {logs === null ? "Logs →" : `${formatCount(logs.length)} ${logs.length === 1 ? "log" : "logs"} →`}
        </Link>
      </footer>
    </section>
  );
}

/** One attribute as a row of a list of facts. */
function AttributeRow({ name, value }: { name: string; value: string }) {
  return (
    <>
      <dt>{name}</dt>
      <dd className="mono">{value}</dd>
    </>
  );
}

/** Attributes as a list of facts, in the order the server sent them. */
function Attributes({ values }: { values: Record<string, string> }) {
  const entries = Object.entries(values);

  if (entries.length === 0) {
    return <p className="sq-muted">None were sent.</p>;
  }

  return (
    <dl className="tr-kv">
      {entries.map(([key, value]) => (
        <AttributeRow key={key} name={key} value={value} />
      ))}
    </dl>
  );
}

/** The services of the trace with their spans, most first, as the server counted them. */
function Services({ trace }: { trace: Trace }) {
  return (
    <section className="cn-card" aria-labelledby="tr-trace-services">
      <header>
        <h2 id="tr-trace-services">Services in this trace</h2>
        <HelpTip label="Services in this trace">
          The services that sent spans of this trace, with how many each sent, as the server counted every span of the
          trace. Open one for its metrics.
        </HelpTip>
      </header>
      <div className="cn-table-scroll">
        <table className="cn-table">
          <thead>
            <tr>
              <th scope="col">Service</th>
              <th scope="col" className="num">
                Spans
              </th>
            </tr>
          </thead>
          <tbody>
            {trace.summary.services.map((share) => (
              <tr key={share.service}>
                <td>
                  <Link className="cn-table-name" to={`/metrics/services/${encodeURIComponent(share.service)}`}>
                    {share.service}
                  </Link>
                </td>
                <td className="num">{formatCount(share.spans)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

/** Props for {@link Records}. */
interface RecordsProps {
  /** The trace's records, oldest first; null while they load. */
  logs: LogEntry[] | null;
  /** The spans, to name each record's span. */
  spans: TraceSpan[];
  /** Picks a record's span on the waterfall. */
  onSpan: (spanId: string) => void;
}

/** The trace's records, oldest first, each naming the span it was written in. */
function Records({ logs, spans, onSpan }: RecordsProps) {
  if (logs === null) {
    return <p className="cn-empty">Reading the records…</p>;
  }

  if (logs.length === 0) {
    return <p className="cn-empty">No log record names this trace.</p>;
  }

  return (
    <ul className="tr-logs">
      {logs.map((log) => {
        const span = spans.find((candidate) => candidate.spanId === log.spanId);

        return (
          <li key={log.id}>
            <span className="mono sq-muted">{formatTime(log.timestamp)}</span>
            <span className={`cn-level ${log.severity.toLowerCase()}`}>{levelName[log.severity]}</span>
            <span>{log.body}</span>
            {span ? (
              <button type="button" className="link-button" onClick={() => onSpan(span.spanId)}>
                {span.service} · {span.name}
              </button>
            ) : (
              <span className="sq-muted">{log.serviceName}</span>
            )}
          </li>
        );
      })}
    </ul>
  );
}
