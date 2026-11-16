import { useMemo, useState, type CSSProperties } from "react";

import type { TraceSpan } from "../api/types";
import { serviceColor } from "./serviceColors";
import { criticalPath, endOf, formatDuration, startOf, traceBounds, waterfallRows } from "./traceMath";

/** Props for {@link Waterfall}. */
interface WaterfallProps {
  /** Every span of the trace. */
  spans: TraceSpan[];
  /** The selected span's ID, or null. */
  selected: string | null;
  /** Called with the span the person selects. */
  onSelect: (spanId: string) => void;
  /** Whether to draw the critical path. */
  showCriticalPath: boolean;
  /** Whether to show only failed spans and the spans above them. */
  errorsOnly: boolean;
}

/**
 * The spans of a trace on a shared timeline, one row each, indented under their parents. Bars take each service's
 * colour on a track that spans the whole trace, so each shows where it sat in the request; failed spans glow red;
 * exceptions show as red marks at the moment they happened. With the critical path on, the spans that decided the
 * duration stay bright and the rest dim. A span whose parent has not arrived is marked, so a gap is visible where it
 * is (ADR 0014).
 */
export function Waterfall({ spans, selected, onSelect, showCriticalPath, errorsOnly }: WaterfallProps) {
  const [collapsed, setCollapsed] = useState<Set<string>>(new Set());
  const bounds = traceBounds(spans);
  const critical = useMemo(
    () => (showCriticalPath ? criticalPath(spans) : new Set<string>()),
    [spans, showCriticalPath],
  );
  const kept = useMemo(() => (errorsOnly ? failedAndAbove(spans) : null), [spans, errorsOnly]);
  const rows = waterfallRows(spans, collapsed).filter((row) => kept === null || kept.has(row.span.spanId));
  const ticks = [0, 0.25, 0.5, 0.75, 1];
  const at = (time: number) => ((time - bounds.start) / Math.max(bounds.durationMs, 0.001)) * 100;

  function toggle(spanId: string) {
    const next = new Set(collapsed);

    if (next.has(spanId)) {
      next.delete(spanId);
    } else {
      next.add(spanId);
    }

    setCollapsed(next);
  }

  return (
    <div className="waterfall">
      <div className="wf-ruler" aria-hidden="true">
        <span>Span</span>
        <span className="wf-ticks">
          {ticks.map((tick) => (
            <span key={tick} style={{ left: `${tick * 100}%` }}>
              {formatDuration(bounds.durationMs * tick)}
            </span>
          ))}
        </span>
      </div>
      <ul className="wf-rows" aria-label="Spans">
        {rows.map((row) => {
          const span = row.span;
          const left = at(startOf(span));
          const width = Math.max(0.25, at(endOf(span)) - left);
          const failed = span.status === "Error";
          const exceptions = span.events.filter((event) => event.name === "exception");

          return (
            <li
              key={span.spanId}
              className={[
                "wf-row",
                span.spanId === selected ? "selected" : "",
                row.missingParent ? "orphan" : "",
                failed ? "failed" : "",
              ]
                .filter(Boolean)
                .join(" ")}
            >
              <span className="wf-who" style={{ paddingLeft: `${row.depth * 14}px` }}>
                {row.children > 0 ? (
                  <button
                    type="button"
                    className="wf-twist"
                    aria-expanded={!collapsed.has(span.spanId)}
                    aria-label={`${collapsed.has(span.spanId) ? "Show" : "Hide"} the ${row.children} spans under ${span.name}`}
                    onClick={() => toggle(span.spanId)}
                  >
                    {collapsed.has(span.spanId) ? "▸" : "▾"}
                  </button>
                ) : (
                  <span className="wf-twist" />
                )}
                <i className="wf-service" style={{ background: serviceColor(span.service) }} aria-hidden="true" />
                <button
                  type="button"
                  className="wf-name"
                  aria-pressed={span.spanId === selected}
                  title={`${span.service} · ${span.name}`}
                  onClick={() => onSelect(span.spanId)}
                >
                  {span.name}
                  {row.missingParent && <small> · parent not received</small>}
                </button>
                {span.kind !== "Internal" && span.kind !== "Unspecified" && (
                  <span className="wf-kind">{kindLabel[span.kind]}</span>
                )}
              </span>
              <span className="wf-track" aria-hidden="true">
                {ticks.slice(1, -1).map((tick) => (
                  <i key={tick} className="wf-grid" style={{ left: `${tick * 100}%` }} />
                ))}
                <i className="wf-line" />
                <span
                  className={[
                    "wf-bar",
                    failed ? "failed" : "",
                    showCriticalPath && !critical.has(span.spanId) ? "dim" : "",
                  ]
                    .filter(Boolean)
                    .join(" ")}
                  style={{ left: `${left}%`, width: `${width}%`, "--c": serviceColor(span.service) } as CSSProperties}
                />
                {exceptions.map((event) => (
                  <i
                    key={event.timestamp}
                    className="wf-exception"
                    style={{ left: `${at(Date.parse(event.timestamp))}%` }}
                  />
                ))}
                <em
                  className={left + width > 78 ? "wf-time before" : "wf-time"}
                  style={{
                    left: left + width > 78 ? undefined : `calc(${left + width}% + 6px)`,
                    right: left + width > 78 ? `calc(${100 - left}% + 6px)` : undefined,
                  }}
                >
                  {formatDuration(span.durationMs)}
                </em>
              </span>
            </li>
          );
        })}
      </ul>
    </div>
  );
}

const kindLabel: Record<TraceSpan["kind"], string> = {
  Unspecified: "",
  Internal: "",
  Server: "server",
  Client: "client",
  Producer: "producer",
  Consumer: "consumer",
};

/** The failed spans and every span above them, so "errors only" keeps the path to each failure. */
function failedAndAbove(spans: TraceSpan[]): Set<string> {
  const byId = new Map(spans.map((span) => [span.spanId, span]));
  const kept = new Set<string>();

  for (const span of spans.filter((candidate) => candidate.status === "Error")) {
    for (let current: TraceSpan | undefined = span; current && !kept.has(current.spanId);) {
      kept.add(current.spanId);
      current = current.parentSpanId === null ? undefined : byId.get(current.parentSpanId);
    }
  }

  return kept;
}
