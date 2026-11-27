import { useState } from "react";
import { Link } from "react-router";

import type { LogEntry } from "../../api/types";
import { formatTime } from "../../logs/format";
import { levelName } from "../../logs/levelWords";
import { useRelatedLogs } from "../../logs/useRelatedLogs";
import { HelpTip } from "../../ui/HelpTip";

/** The attributes the Details tab shows in blocks of their own instead of as rows. */
const exceptionType = "exception.type";
const exceptionMessage = "exception.message";
const exceptionStack = "exception.stacktrace";

/** Props for {@link ConsoleLogInspector}. */
export interface ConsoleLogInspectorProps {
  /** The record shown. */
  entry: LogEntry;
  /** Where it sits among the loaded records, from 0. */
  index: number;
  /** How many records match, as the server counted them; null before the count arrives. */
  total: number | null;
  /** The searched window, for the records of the same trace. */
  window: { from: string; to: string };
  /** Shows the record before it, or null at the first. */
  onPrevious: (() => void) | null;
  /** Shows the record after it, or null at the last loaded one. */
  onNext: (() => void) | null;
  /** Narrows the search to the record's trace. */
  onTrace(traceId: string): void;
  /** Shows what happened a minute either side of the record. */
  onAround(entry: LogEntry): void;
}

/** The inspector's tabs, as reference screenshot 6 names them. */
type Tab = "details" | "related" | "trace" | "json";

/**
 * Console's event inspector (VEY-74, reference screenshot 6): the record's level, time, and message, then tabs for its
 * details (exception, stack trace, attributes, and correlation), the other records of its trace, the trace itself,
 * and the record as JSON. Previous and Next step through the loaded records.
 */
export function ConsoleLogInspector({
  entry,
  index,
  total,
  window,
  onPrevious,
  onNext,
  onTrace,
  onAround,
}: ConsoleLogInspectorProps) {
  const [tab, setTab] = useState<Tab>("details");
  const related = useRelatedLogs(entry.traceId, window.from, window.to);
  const host = entry.hostName ?? entry.resourceAttributes["host.name"] ?? null;
  const relatedCount = related.items === null ? null : related.items.length;
  const tabs: [Tab, string][] = [
    ["details", "Details"],
    ["related", relatedCount === null ? "Related" : `Related (${relatedCount}${related.more ? "+" : ""})`],
    ["trace", "Trace"],
    ["json", "JSON"],
  ];

  return (
    <section className="cn-card cn-inspector" aria-labelledby="cn-inspector-title">
      <header>
        <h2 id="cn-inspector-title">Event</h2>
        <HelpTip label="Event">
          One record with everything it carries. Details shows its exception and attributes and what ties it to a trace;
          Related lists the other records of its trace in the searched window; JSON is the record as the API returns it.
        </HelpTip>
        <span className="cn-step">
          <button type="button" aria-label="Previous event" disabled={!onPrevious} onClick={onPrevious ?? undefined}>
            ◀
          </button>
          {index + 1} of {total === null ? "…" : total.toLocaleString("en")}
          <button type="button" aria-label="Next event" disabled={!onNext} onClick={onNext ?? undefined}>
            ▶
          </button>
        </span>
      </header>
      <div className="cn-inspector-body">
        <p className="cn-inspector-when">
          <span className={`cn-level ${entry.severity.toLowerCase()}`}>{levelName[entry.severity]}</span>
          <span className="mono">{formatTime(entry.timestamp)}</span>
        </p>
        <h3 className="cn-inspector-message">{entry.body}</h3>
        <p className="cn-inspector-where">
          {/* The service and the host open their own screens (VEY-83). */}
          {entry.serviceName && (
            <Link className="cn-cell-link" to={`/metrics/services/${encodeURIComponent(entry.serviceName)}`}>
              {entry.serviceName}
            </Link>
          )}
          {host && (
            <>
              {entry.serviceName && " · "}
              <Link className="cn-cell-link" to={`/metrics/hosts/${encodeURIComponent(host)}`}>
                {host}
              </Link>
            </>
          )}
          {entry.environment && (
            <>
              {(entry.serviceName || host) && " · "}
              {entry.environment}
            </>
          )}
        </p>
      </div>
      <div className="cn-tabs" role="tablist" aria-label="Event">
        {tabs.map(([id, label]) => (
          <button
            key={id}
            type="button"
            role="tab"
            id={`cn-inspector-tab-${id}`}
            aria-selected={tab === id}
            aria-controls="cn-inspector-panel"
            onClick={() => setTab(id)}
          >
            {label}
          </button>
        ))}
      </div>
      <div
        className="cn-inspector-body"
        role="tabpanel"
        id="cn-inspector-panel"
        aria-labelledby={`cn-inspector-tab-${tab}`}
      >
        {tab === "details" && <Details entry={entry} host={host} onAround={onAround} />}
        {tab === "related" && <Related items={related.items} more={related.more} entry={entry} onTrace={onTrace} />}
        {tab === "trace" && <Trace entry={entry} onTrace={onTrace} />}
        {tab === "json" && <pre className="cn-code">{JSON.stringify(entry, null, 2)}</pre>}
      </div>
    </section>
  );
}

/** The record's exception, stack trace, attributes, and correlation, with the actions that pivot from it. */
function Details({ entry, host, onAround }: { entry: LogEntry; host: string | null; onAround(entry: LogEntry): void }) {
  const [copied, setCopied] = useState(false);
  const type = entry.attributes[exceptionType];
  const message = entry.attributes[exceptionMessage];
  const stack = entry.attributes[exceptionStack];
  const attributes = Object.entries(entry.attributes).filter(
    ([key]) => key !== exceptionType && key !== exceptionMessage && key !== exceptionStack,
  );

  async function copy() {
    try {
      await navigator.clipboard.writeText(JSON.stringify(entry, null, 2));
      setCopied(true);
    } catch {
      setCopied(false);
    }
  }

  return (
    <>
      {(type || message) && (
        <>
          <h4 className="cn-section">Exception</h4>
          <pre className="cn-code">
            {type && <span className="cn-code-bad">{type}</span>}
            {type && message ? ": " : ""}
            {message}
          </pre>
        </>
      )}
      {stack && (
        <>
          <h4 className="cn-section">Stack trace</h4>
          <pre className="cn-code">{stack}</pre>
        </>
      )}
      <h4 className="cn-section">Attributes</h4>
      {attributes.length === 0 ? (
        <p className="cn-empty-line">The record has no attributes of its own.</p>
      ) : (
        <dl className="cn-kv">
          {attributes.map(([key, value]) => (
            <div key={key}>
              <dt>{key}</dt>
              <dd>{value}</dd>
            </div>
          ))}
        </dl>
      )}
      <h4 className="cn-section">Correlation</h4>
      <dl className="cn-correlation">
        <div>
          <dt>Trace</dt>
          <dd className="mono">
            {entry.traceId ? <Link to={`/traces/${entry.traceId}`}>{entry.traceId.slice(0, 12)}…</Link> : "—"}
          </dd>
        </div>
        <div>
          <dt>Span</dt>
          <dd className="mono">{entry.spanId ? `${entry.spanId.slice(0, 8)}…` : "—"}</dd>
        </div>
        <div>
          <dt>Host</dt>
          <dd>{host ?? "—"}</dd>
        </div>
        <div>
          <dt>Environment</dt>
          <dd>{entry.environment ?? "—"}</dd>
        </div>
      </dl>
      <div className="cn-inspector-actions">
        {entry.traceId && (
          <Link className="cn-button primary" to={`/traces/${entry.traceId}`}>
            View trace →
          </Link>
        )}
        <button type="button" className="cn-button" onClick={() => onAround(entry)}>
          Logs around this
        </button>
        <button type="button" className="cn-button" onClick={() => void copy()}>
          {copied ? "Copied" : "Copy JSON"}
        </button>
      </div>
    </>
  );
}

/** The other records of the trace, oldest first. */
function Related({
  items,
  more,
  entry,
  onTrace,
}: {
  items: LogEntry[] | null;
  more: boolean;
  entry: LogEntry;
  onTrace(traceId: string): void;
}) {
  if (!entry.traceId) {
    return <p className="cn-empty-line">The record belongs to no trace, so nothing ties other records to it.</p>;
  }

  if (items === null) {
    return <p className="cn-empty-line">Reading the trace's records…</p>;
  }

  return (
    <>
      <ol className="cn-related">
        {items.map((item) => (
          <li key={item.id} aria-current={item.id === entry.id ? "true" : undefined}>
            <span className="mono">{formatTime(item.timestamp)}</span>
            <span className={`cn-level ${item.severity.toLowerCase()}`}>{levelName[item.severity]}</span>
            <span className="cn-related-text">
              <b>{item.serviceName}</b> {item.body}
            </span>
          </li>
        ))}
      </ol>
      <button type="button" className="cn-button" onClick={() => onTrace(entry.traceId ?? "")}>
        {more ? "Show all of the trace's records" : "Show only the trace's records"}
      </button>
    </>
  );
}

/** The trace the record belongs to, and the ways into it. */
function Trace({ entry, onTrace }: { entry: LogEntry; onTrace(traceId: string): void }) {
  if (!entry.traceId) {
    return <p className="cn-empty-line">The record belongs to no trace.</p>;
  }

  return (
    <>
      <dl className="cn-kv">
        <div>
          <dt>Trace ID</dt>
          <dd className="mono">{entry.traceId}</dd>
        </div>
        <div>
          <dt>Span ID</dt>
          <dd className="mono">{entry.spanId ?? "—"}</dd>
        </div>
      </dl>
      <div className="cn-inspector-actions">
        <Link className="cn-button primary" to={`/traces/${entry.traceId}`}>
          Open the trace →
        </Link>
        <button type="button" className="cn-button" onClick={() => onTrace(entry.traceId ?? "")}>
          Filter by this trace
        </button>
      </div>
    </>
  );
}
