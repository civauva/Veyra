import type { Trace, TraceSpan, TraceSummary } from "../api/types";

/*
 * The figures a trace's own page tells about the trace (ADR 0029): worked out from the spans and the summary of that
 * one trace, which the server sent whole. Lists of many traces never come through here; the server counts those.
 */

/**
 * A trace ID short enough for a header: its first ten and last four characters, as the mockups show it.
 *
 * @param traceId - The trace ID.
 * @returns For example `8fa21c9d4e…a71c`.
 */
export function shortTraceId(traceId: string): string {
  return traceId.length > 16 ? `${traceId.slice(0, 10)}…${traceId.slice(-4)}` : traceId;
}

/**
 * How many levels deep the trace's spans go, the root being the first.
 *
 * @param spans - The spans, each with the depth the server gave it.
 * @returns The levels; 0 without spans.
 */
export function traceLevels(spans: TraceSpan[]): number {
  return spans.reduce((deepest, span) => Math.max(deepest, span.depth + 1), 0);
}

/** The part of a trace its critical path waited on. */
export interface CriticalShare {
  /** The share of the trace's duration those spans took, from 0 to 1; null when the server named no path. */
  share: number | null;
  /** The spans at the end of the path, which the trace waited on, in the order they are listed. */
  spans: TraceSpan[];
}

/**
 * The spans the critical path ends in, the ones the trace waited on, and the share of its duration they took. A path of
 * the root alone took the whole trace.
 *
 * @param trace - The trace, with its spans and the server's critical path.
 * @returns The share and the spans.
 */
export function criticalShare(trace: Trace): CriticalShare {
  const path = new Set(trace.summary.criticalPath);
  const onPath = trace.spans.filter((span) => path.has(span.spanId));

  if (onPath.length === 0) {
    return { share: null, spans: [] };
  }

  const parents = new Set(onPath.map((span) => span.parentSpanId));
  const ends = onPath.filter((span) => !parents.has(span.spanId));
  const total = ends.reduce((sum, span) => sum + span.durationMs, 0);
  const duration = trace.summary.durationMs;

  return { share: duration > 0 ? Math.min(1, total / duration) : 1, spans: ends };
}

/** The exception behind a failed trace. */
export interface TraceException {
  /** The exception's type, or the span's name when it recorded none. */
  type: string;
  /** Its message, or the span's status message; empty when there is neither. */
  message: string;
  /** The service whose span threw it. */
  service: string;
}

/**
 * The exception of the span the server named as the cause of the failure.
 *
 * @param trace - The trace.
 * @returns The exception, or null when nothing failed or the cause span is not listed.
 */
export function firstException(trace: Trace): TraceException | null {
  const cause = trace.spans.find((span) => span.spanId === trace.summary.cause);

  if (!cause) {
    return null;
  }

  const exception = cause.events.find((event) => event.name === "exception");

  return {
    type: exception?.attributes["exception.type"] ?? cause.name,
    message: exception?.attributes["exception.message"] ?? cause.statusMessage ?? "",
    service: cause.service,
  };
}

/**
 * The window the trace's log records are asked for in: a minute either side of it, so records written just before the
 * first span or after the last are included.
 *
 * @param summary - The trace's figures.
 * @returns The window, as ISO instants.
 */
export function recordWindow(summary: TraceSummary): { from: string; to: string } {
  const start = Date.parse(summary.start);

  return {
    from: new Date(start - 60_000).toISOString(),
    to: new Date(start + summary.durationMs + 60_000).toISOString(),
  };
}
