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
 * The waterfall's rows: the spans in the tree order the server sends (VEY-68), without the spans under a collapsed
 * one. Hiding them is the only thing done here; the order, depths, and gaps come from the server.
 *
 * @param spans - The spans in the server's tree order, each with its depth.
 * @param collapsed - Span IDs whose children are hidden.
 * @returns The rows, top to bottom.
 */
export function waterfallRows(spans: TraceSpan[], collapsed: ReadonlySet<string> = new Set()): WaterfallRow[] {
  const rows: WaterfallRow[] = [];

  // The depth of the collapsed span whose subtree is being passed over, or null when none is.
  let hiddenBelow: number | null = null;

  for (const span of spans) {
    if (hiddenBelow !== null && span.depth > hiddenBelow) {
      continue;
    }

    hiddenBelow = collapsed.has(span.spanId) ? span.depth : null;
    rows.push({ span, depth: span.depth, children: span.children, missingParent: span.missingParent });
  }

  return rows;
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
