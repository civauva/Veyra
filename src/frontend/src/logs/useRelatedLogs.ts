import { useEffect, useState } from "react";

import { api } from "../api/client";
import type { LogEntry } from "../api/types";

/** How many records of a trace the inspector lists. */
const relatedLimit = 50;

/** What {@link useRelatedLogs} gives the inspector. */
export interface RelatedLogs {
  /** The records of the same trace, oldest first, as a trace reads; null while they are read or without a trace. */
  items: LogEntry[] | null;
  /** Whether the trace has more records than are listed. */
  more: boolean;
}

/** The answer for one trace, remembered with the request it answers. */
interface Answer extends RelatedLogs {
  /** The trace and window the answer is for. */
  key: string;
}

/**
 * The other records of a record's trace, in the searched window, for the inspector's Related tab. The server filters
 * by the trace (`GET /api/v1/logs?traceId=…&order=oldest`).
 *
 * @param traceId - The trace, or null for a record without one.
 * @param from - The start of the searched window, as an ISO instant.
 * @param to - Its end.
 * @returns The records, oldest first.
 */
export function useRelatedLogs(traceId: string | null, from: string, to: string): RelatedLogs {
  const key = traceId ? `${traceId}#${from}#${to}` : "";
  const [answer, setAnswer] = useState<Answer | null>(null);

  useEffect(() => {
    if (!traceId) {
      return undefined;
    }

    const controller = new AbortController();
    const params = new URLSearchParams({ traceId, from, to, order: "oldest", limit: String(relatedLimit) });

    api.searchLogs(params, controller.signal).then(
      (page) => {
        if (!controller.signal.aborted) {
          setAnswer({ key: `${traceId}#${from}#${to}`, items: page.items, more: page.nextCursor !== null });
        }
      },
      () => {
        if (!controller.signal.aborted) {
          setAnswer({ key: `${traceId}#${from}#${to}`, items: [], more: false });
        }
      },
    );

    return () => controller.abort();
  }, [traceId, from, to]);

  return answer && answer.key === key ? { items: answer.items, more: answer.more } : { items: null, more: false };
}
