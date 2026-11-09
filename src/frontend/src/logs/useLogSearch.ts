import { useCallback, useEffect, useRef, useState } from "react";

import { api } from "../api/client";
import type { LogEntry, LogSummary } from "../api/types";
import { filtersFromUrl, filtersToApiParams, filtersToUrl, timeWindow, type LogFilters } from "./logFilters";

/** How often "Follow new" asks for new records. */
export const followIntervalMilliseconds = 5_000;

/** The answer to one search, remembered with the request it answers. */
interface SearchResult {
  /** The filters and refresh count the result belongs to; a different value means a newer search is running. */
  requestKey: string;
  /** The end of the window. Later pages reuse it, so paging never skips or repeats records. */
  now: Date;
  /** The records loaded so far, newest first. */
  items: LogEntry[];
  /** The cursor of the next page; null on the last page. */
  nextCursor: string | null;
  /** The counts for the histogram and the service filter; null when they could not be loaded. */
  summary: LogSummary | null;
  /** Why the search failed; null when it did not. */
  error: string | null;
}

/** What {@link useLogSearch} gives the Log Explorer. */
export interface LogSearch {
  /** The records loaded so far, newest first. Stays filled while a newer search runs, so the list does not flicker. */
  items: LogEntry[];
  /** The histogram and service counts, or null before they arrive or when they failed. */
  summary: LogSummary | null;
  /** Whether a search for the current filters is still running. */
  loading: boolean;
  /** Whether the next page is being loaded. */
  loadingMore: boolean;
  /** Whether more records exist beyond those loaded. */
  hasMore: boolean;
  /** Why the search failed, or null. */
  error: string | null;
  /** The start of the searched window. */
  from: Date;
  /** The end of the searched window. */
  to: Date;
  /** Loads the next page. Does nothing while one is loading or when none is left. */
  loadMore: () => void;
}

/** The message of anything thrown. */
function messageOf(failure: unknown): string {
  return failure instanceof Error ? failure.message : String(failure);
}

/**
 * Searches logs for the filters, and searches again when they change or, while following, every five seconds.
 * Earlier requests are cancelled, so a slow answer never replaces a newer one.
 *
 * @param filters - What the person chose.
 * @param follow - Whether to look for new records every {@link followIntervalMilliseconds}.
 * @returns The records, the counts, and the state of the search.
 */
export function useLogSearch(filters: LogFilters, follow: boolean): LogSearch {
  // The URL form is a stable value for the effect: equal filters give an equal string, a new object does not matter.
  const filtersKey = filtersToUrl(filters);
  const [refreshes, setRefreshes] = useState(0);
  const requestKey = `${filtersKey}#${refreshes}`;
  const [result, setResult] = useState<SearchResult | null>(null);
  const [loadingMore, setLoadingMore] = useState(false);
  const loadingMoreRef = useRef(false);
  // Labels the window before the first answer arrives; later, the answer carries the end it searched up to.
  const [mountedAt] = useState(() => new Date());

  useEffect(() => {
    const controller = new AbortController();
    const now = new Date();
    const params = filtersToApiParams(filtersFromUrl(new URLSearchParams(filtersKey)), now);
    const summaryParams = new URLSearchParams(params);
    summaryParams.delete("limit");

    void Promise.allSettled([
      api.searchLogs(params, controller.signal),
      api.summarizeLogs(summaryParams, controller.signal),
    ]).then(([page, summary]) => {
      if (controller.signal.aborted) {
        return;
      }

      setResult({
        requestKey,
        now,
        items: page.status === "fulfilled" ? page.value.items : [],
        nextCursor: page.status === "fulfilled" ? page.value.nextCursor : null,
        summary: summary.status === "fulfilled" ? summary.value : null,
        error: page.status === "rejected" ? messageOf(page.reason) : null,
      });
    });

    return () => controller.abort();
  }, [filtersKey, requestKey]);

  useEffect(() => {
    if (!follow) {
      return;
    }

    const timer = setInterval(() => setRefreshes((count) => count + 1), followIntervalMilliseconds);

    return () => clearInterval(timer);
  }, [follow]);

  const loadMore = useCallback(() => {
    if (!result?.nextCursor || result.requestKey !== requestKey || loadingMoreRef.current) {
      return;
    }

    const params = filtersToApiParams(filters, result.now, result.nextCursor);
    loadingMoreRef.current = true;
    setLoadingMore(true);

    api
      .searchLogs(params)
      .then((page) =>
        setResult((current) =>
          current?.requestKey === result.requestKey
            ? { ...current, items: [...current.items, ...page.items], nextCursor: page.nextCursor }
            : current,
        ),
      )
      .catch((failure: unknown) =>
        setResult((current) =>
          current?.requestKey === result.requestKey ? { ...current, error: messageOf(failure) } : current,
        ),
      )
      .finally(() => {
        loadingMoreRef.current = false;
        setLoadingMore(false);
      });
  }, [filters, requestKey, result]);

  const window = timeWindow(filters, result?.now ?? mountedAt);

  return {
    items: result?.items ?? [],
    summary: result?.summary ?? null,
    loading: result?.requestKey !== requestKey,
    loadingMore,
    hasMore: result?.nextCursor != null,
    error: result?.error ?? null,
    from: window.from,
    to: window.to,
    loadMore,
  };
}
