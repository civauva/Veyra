import type { TraceItem, TracesSummary } from "../api/types";
import { zoneParts } from "../time/zone";
import type { TraceOrder, TraceShow } from "./traceFilters";

/*
 * The words of the trace screens every look can use (ADR 0029): the choices of the filters and what the server's
 * figures mean. They word what the server sent and work out nothing over a list.
 */

/** What the Show choice offers, with its words. */
export const showChoices: [TraceShow, string][] = [
  ["all", "All"],
  ["failed", "Failed"],
  ["slow", "Slow"],
  ["failed-slow", "Both failed and slow"],
];

/** What the Order choice offers, with its words. */
export const orderChoices: [TraceOrder, string][] = [
  ["worst", "Worst first"],
  ["newest", "Newest"],
];

/** The shortest durations the Duration choice offers, in milliseconds, with their words; null for any. */
export const durationChoices: [number | null, string][] = [
  [null, "Any"],
  [100, "> 100 ms"],
  [300, "> 300 ms"],
  [1000, "> 1 s"],
  [5000, "> 5 s"],
  [30_000, "> 30 s"],
];

/**
 * The change in the number of traces against the period before.
 *
 * @param summary - The server's figures of the window.
 * @returns The change as a share, such as 0.06 for 6% more; null when the period before had none.
 */
export function traceChange(summary: TracesSummary): number | null {
  const before = summary.previousTraces;

  return before === null || before === 0 ? null : (summary.traces - before) / before;
}

/**
 * The share of traces that failed, with two decimals, so a few failures among many still show.
 *
 * @param failed - The failed traces the server counted.
 * @param traces - All traces it counted.
 * @returns For example `0.02%`, or a dash without traces.
 */
export function failedShare(failed: number, traces: number): string {
  return traces > 0 ? `${((failed / traces) * 100).toFixed(2)}%` : "—";
}

/**
 * How a trace of the list stands: failed, slower than the window's p95, or fine.
 *
 * @param item - The trace.
 * @param p95Ms - The window's 95th percentile from the server, or null.
 * @returns Its status.
 */
export function traceStatus(item: TraceItem, p95Ms: number | null): "failed" | "slow" | "ok" {
  if (item.failed) {
    return "failed";
  }

  return p95Ms !== null && item.durationMs >= p95Ms ? "slow" : "ok";
}

/**
 * The time of day of an instant in the display zone, to the second.
 *
 * @param instant - The instant.
 * @returns For example `14:32:08`.
 */
export function clockSeconds(instant: string): string {
  const { hour, minute, second } = zoneParts(instant);
  const pad = (value: number) => String(value).padStart(2, "0");

  return `${pad(hour)}:${pad(minute)}:${pad(second)}`;
}
