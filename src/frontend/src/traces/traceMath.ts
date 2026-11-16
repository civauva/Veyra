import type { TraceSpan } from "../api/types";

/** One row of the waterfall: a span, how deep it sits, and what the tree around it looks like. */
export interface WaterfallRow {
  /** The span. */
  span: TraceSpan;
  /** How many ancestors it has among the spans that arrived; 0 for the root and for spans whose parent is missing. */
  depth: number;
  /** How many direct children arrived. */
  children: number;
  /** True when the span names a parent that has not arrived (ADR 0014: the view says so instead of hiding the gap). */
  missingParent: boolean;
}

/** Milliseconds since the epoch of a span's start. */
export function startOf(span: TraceSpan): number {
  return Date.parse(span.start);
}

/** Milliseconds since the epoch of a span's end. */
export function endOf(span: TraceSpan): number {
  return startOf(span) + span.durationMs;
}

/**
 * Orders spans for the waterfall: depth first from each root, children by start time. Spans whose parent has not
 * arrived become roots of their own, marked, so a gap in the trace is visible where it is.
 *
 * @param spans - Every span of the trace, in any order.
 * @param collapsed - Span IDs whose children are hidden.
 * @returns The rows, top to bottom.
 */
export function waterfallRows(spans: TraceSpan[], collapsed: ReadonlySet<string> = new Set()): WaterfallRow[] {
  const ids = new Set(spans.map((span) => span.spanId));
  const children = new Map<string, TraceSpan[]>();
  const tops: TraceSpan[] = [];

  for (const span of spans) {
    if (span.parentSpanId !== null && ids.has(span.parentSpanId)) {
      const siblings = children.get(span.parentSpanId) ?? [];
      siblings.push(span);
      children.set(span.parentSpanId, siblings);
    } else {
      tops.push(span);
    }
  }

  const byStart = (a: TraceSpan, b: TraceSpan) => startOf(a) - startOf(b) || a.spanId.localeCompare(b.spanId);
  const rows: WaterfallRow[] = [];
  const visit = (span: TraceSpan, depth: number) => {
    const own = (children.get(span.spanId) ?? []).sort(byStart);
    rows.push({
      span,
      depth,
      children: own.length,
      missingParent: depth === 0 && span.parentSpanId !== null,
    });

    if (!collapsed.has(span.spanId)) {
      own.forEach((child) => visit(child, depth + 1));
    }
  };

  // The real root first, then the spans that lost their parent, each by start.
  tops
    .sort((a, b) => Number(a.parentSpanId !== null) - Number(b.parentSpanId !== null) || byStart(a, b))
    .forEach((top) => visit(top, 0));

  return rows;
}

/**
 * The critical path: the chain of spans that decided how long the request took. From the end of each span on the path,
 * it walks back through the children that finished before the time reached so far, newest first, and continues from
 * where each started. Children that overlap work on the path are left out: making them faster would not have helped.
 *
 * @param spans - Every span of the trace.
 * @returns The span IDs on the path.
 */
export function criticalPath(spans: TraceSpan[]): Set<string> {
  const path = new Set<string>();
  const children = new Map<string, TraceSpan[]>();

  for (const span of spans) {
    if (span.parentSpanId !== null) {
      children.set(span.parentSpanId, [...(children.get(span.parentSpanId) ?? []), span]);
    }
  }

  const walk = (span: TraceSpan) => {
    path.add(span.spanId);
    let reached = endOf(span);

    for (const child of [...(children.get(span.spanId) ?? [])].sort((a, b) => endOf(b) - endOf(a))) {
      if (endOf(child) <= reached) {
        walk(child);
        reached = startOf(child);
      }
    }
  };

  const root = spans.find((span) => span.parentSpanId === null) ?? spans[0];

  if (root !== undefined) {
    walk(root);
  }

  return path;
}

/** Where a trace begins and how long it lasts, over every span that arrived. */
export function traceBounds(spans: TraceSpan[]): { start: number; durationMs: number } {
  const start = Math.min(...spans.map(startOf));
  const end = Math.max(...spans.map(endOf));

  return { start, durationMs: end - start };
}

/**
 * A duration as people read it: microseconds below a millisecond, whole milliseconds below a second, then seconds.
 *
 * @param milliseconds - The duration.
 * @returns For example `850 µs`, `412 ms`, or `1.84 s`.
 */
export function formatDuration(milliseconds: number): string {
  if (milliseconds < 1) {
    return `${Math.round(milliseconds * 1000)} µs`;
  }

  if (milliseconds < 1000) {
    return `${Math.round(milliseconds)} ms`;
  }

  const seconds = milliseconds / 1000;

  return `${seconds < 10 ? seconds.toFixed(2) : seconds.toFixed(1)} s`;
}
