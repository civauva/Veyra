import { Link } from "react-router";

import type { TraceItem } from "../api/types";
import { formatTime } from "../logs/format";
import { serviceColor } from "./serviceColors";
import { formatDuration } from "./traceMath";

/** Props for {@link TraceTable}. */
interface TraceTableProps {
  /** The traces, newest first. */
  items: TraceItem[];
  /** The slowest duration of the window, so duration bars compare across rows. */
  slowestMs: number;
  /** The 95th percentile, above which a trace counts as slow; null when unknown. */
  p95Ms: number | null;
}

/**
 * The traces of the list, one row per request: whether it failed, its name and service, why it failed, how long it
 * took, a ribbon of the services it went through, and its span count. A row opens the trace; on phones rows become
 * cards.
 */
export function TraceTable({ items, slowestMs, p95Ms }: TraceTableProps) {
  return (
    <div className="trace-table" role="table" aria-label="Traces">
      <div className="trace-row trace-head" role="row">
        <span role="columnheader">
          <span className="visually-hidden">Status</span>
        </span>
        <span role="columnheader">Trace</span>
        <span role="columnheader">Why it failed</span>
        <span role="columnheader" className="trace-dur">
          Duration
        </span>
        <span role="columnheader">Services</span>
        <span role="columnheader">Spans</span>
      </div>
      {items.map((item) => {
        const kind = item.failed ? "failed" : p95Ms !== null && item.durationMs >= p95Ms ? "slow" : "ok";

        return (
          <Link
            key={item.traceId}
            role="row"
            className={`trace-row trace-${kind}`}
            to={`/traces/${item.traceId}?at=${encodeURIComponent(item.start)}`}
          >
            <span
              role="cell"
              className="trace-status"
              aria-label={kind === "failed" ? "Failed" : kind === "slow" ? "Slow" : "OK"}
            />
            <span role="cell" className="trace-name">
              <strong>{item.name}</strong>
              <small>
                {item.service}
                {item.environment && ` · ${item.environment}`} · {formatTime(item.start)}
              </small>
            </span>
            <span role="cell" className="trace-cause">
              {item.cause ? `${item.cause.service}: ${item.cause.message}` : ""}
            </span>
            <span role="cell" className="trace-dur">
              <b>{formatDuration(item.durationMs)}</b>
              <span className="trace-bar" aria-hidden="true">
                <i
                  style={{ width: `${Math.max(2, Math.min(100, (item.durationMs / Math.max(1, slowestMs)) * 100))}%` }}
                />
              </span>
            </span>
            <span
              role="cell"
              className="trace-ribbon"
              title={item.services.map((s) => `${s.service}: ${s.spans}`).join(", ")}
            >
              {item.services.map((share) => (
                <i
                  key={share.service}
                  style={{ flexGrow: share.spans, background: serviceColor(share.service) }}
                  aria-label={`${share.service}, ${share.spans} spans`}
                />
              ))}
            </span>
            <span role="cell" className="trace-spans">
              {item.spans}
              {item.failedSpans > 0 && <span className="trace-pill">{item.failedSpans} failed</span>}
            </span>
          </Link>
        );
      })}
    </div>
  );
}
