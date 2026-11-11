import { useCallback, useEffect, useState } from "react";

import { api, ApiError } from "../api/client";
import type { AlertListItem, AlertQuery, AlertSummary } from "../api/types";

/** How often the Alerts screen fetches again while it shows only its first page: twice a minute. */
export const historyRefreshMilliseconds = 30_000;

/** What {@link useAlertHistory} returns. */
export interface AlertHistoryState {
  /** The counts of the period; null before they arrive. */
  summary: AlertSummary | null;
  /** The alerts loaded so far, newest first; null before the first page arrives. */
  items: AlertListItem[] | null;
  /** Whether older alerts can be loaded. */
  hasMore: boolean;
  /** Whether older alerts are being loaded. */
  loadingMore: boolean;
  /** Why the latest request failed, or null when it worked. */
  error: string | null;
  /** Loads the next page of older alerts. */
  loadMore(): void;
}

interface Loaded {
  key: string;
  summary: AlertSummary | null;
  items: AlertListItem[] | null;
  next: string | null;
  pages: number;
  loadingMore: boolean;
  error: string | null;
}

/**
 * Fetches the counts and the first page of alerts for the filters, and keeps them fresh while only the first page is
 * shown. Older pages are added on request; a refresh would drop them, so it waits until the filters change.
 *
 * @param query - The filters of the Alerts screen.
 * @returns The counts, the alerts, and a way to load older ones.
 */
export function useAlertHistory(query: AlertQuery): AlertHistoryState {
  const key = JSON.stringify(query);
  const empty: Loaded = { key, summary: null, items: null, next: null, pages: 0, loadingMore: false, error: null };
  const [state, setState] = useState<Loaded>(empty);

  // Other filters start a new list; the counts stay until the new ones arrive, since only the period changes them.
  if (state.key !== key) {
    setState({ ...empty, summary: state.summary });
  }

  const pages = state.key === key ? state.pages : 0;

  useEffect(() => {
    if (pages > 1) {
      return;
    }

    let stopped = false;
    let controller: AbortController | null = null;
    const filters = JSON.parse(key) as AlertQuery;

    async function load() {
      controller?.abort();
      controller = new AbortController();

      try {
        const [summary, page] = await Promise.all([
          api.alertSummary(filters.days, filters.environment, controller.signal),
          api.alerts(filters, null, controller.signal),
        ]);

        if (!stopped) {
          setState((previous) =>
            previous.key === key && previous.pages > 1
              ? { ...previous, summary }
              : { key, summary, items: page.items, next: page.next, pages: 1, loadingMore: false, error: null },
          );
        }
      } catch (error) {
        if (stopped || (error instanceof DOMException && error.name === "AbortError")) {
          return;
        }

        const message = error instanceof ApiError ? error.message : "Veyra could not be reached.";
        setState((previous) => ({ ...previous, error: message }));
      }
    }

    void load();
    const timer = window.setInterval(() => void load(), historyRefreshMilliseconds);

    return () => {
      stopped = true;
      window.clearInterval(timer);
      controller?.abort();
    };
  }, [key, pages]);

  const loadMore = useCallback(() => {
    if (!state.next || state.loadingMore) {
      return;
    }

    const cursor = state.next;
    const filters = JSON.parse(key) as AlertQuery;

    setState((previous) => ({ ...previous, loadingMore: true }));
    api
      .alerts(filters, cursor)
      .then((page) =>
        setState((previous) =>
          previous.key === key
            ? {
                ...previous,
                items: [...(previous.items ?? []), ...page.items],
                next: page.next,
                pages: previous.pages + 1,
                loadingMore: false,
                error: null,
              }
            : previous,
        ),
      )
      .catch((error: unknown) => {
        const message = error instanceof ApiError ? error.message : "Veyra could not be reached.";
        setState((previous) => ({ ...previous, loadingMore: false, error: message }));
      });
  }, [key, state.next, state.loadingMore]);

  return {
    summary: state.summary,
    items: state.key === key ? state.items : null,
    hasMore: state.key === key && state.next !== null,
    loadingMore: state.loadingMore,
    error: state.error,
    loadMore,
  };
}
