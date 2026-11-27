import { api } from "../api/client";
import type { LogEntry, Trace } from "../api/types";
import { useMetricData, type MetricDataState } from "../metrics/useMetricData";
import { recordWindow } from "./traceFigures";

/**
 * One trace, read again every 30 seconds because spans of other services may still be arriving (ADR 0014). With
 * `errorsOnly` the server keeps the failed spans and the spans above them; the figures stay the whole trace's (VEY-67).
 * Every look's trace page reads it through here.
 *
 * @param traceId - The trace ID from the address.
 * @param at - When the trace is known to have happened, from the address, or null.
 * @param errorsOnly - Whether to ask for the failed spans and the spans above them only.
 * @returns The trace, whether it is unknown, and the latest error.
 */
export function useTrace(traceId: string, at: string | null, errorsOnly: boolean): MetricDataState<Trace> {
  return useMetricData(`trace-${traceId}-${at ?? ""}`, (signal) => api.trace(traceId, at, errorsOnly, signal), {
    choices: String(errorsOnly),
  });
}

/** What {@link useTraceRecords} returns. */
export interface TraceRecords {
  /** The trace's log records, oldest first; null while they load. */
  logs: LogEntry[] | null;
  /** The chosen span's own records, oldest first; null while they load or without a chosen span. */
  spanLogs: LogEntry[] | null;
  /** The window both were asked for, a minute either side of the trace. */
  window: { from: string; to: string };
}

/**
 * The log records of a trace, and of its chosen span asked for by its ID (VEY-67), so a span late in a busy trace has
 * all of its own. The server orders them oldest first.
 *
 * @param trace - The trace.
 * @param spanId - The chosen span, or null.
 * @returns The records and the window they were asked for in.
 */
export function useTraceRecords(trace: Trace, spanId: string | null): TraceRecords {
  const window = recordWindow(trace.summary);
  const { from, to } = window;
  const logs = useMetricData(`trace-logs-${trace.traceId}-${from}`, (signal) =>
    api.searchLogs(new URLSearchParams({ traceId: trace.traceId, from, to, limit: "500", order: "oldest" }), signal),
  );
  const spanLogs = useMetricData(`trace-span-logs-${trace.traceId}-${spanId ?? ""}-${from}`, (signal) =>
    spanId === null
      ? Promise.resolve(null)
      : api.searchLogs(
          new URLSearchParams({ traceId: trace.traceId, spanId, from, to, limit: "500", order: "oldest" }),
          signal,
        ),
  );

  return { logs: logs.data?.items ?? null, spanLogs: spanLogs.data?.items ?? null, window };
}
