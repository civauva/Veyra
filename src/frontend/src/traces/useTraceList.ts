import { useCallback, useEffect, useState } from "react";

import { api, ApiError } from "../api/client";
import type { TraceDurations, TraceItem, TraceService } from "../api/types";
import { filtersToApiParams, type TraceFilters } from "./traceFilters";

/** How often a window that ends now looks again. Spans arrive within seconds, but the list need not move under the eye. */
const refreshMilliseconds = 30_000;

/** What {@link useTraceList} returns. */
export interface TraceList {
  /** The traces loaded so far, newest first; null before the first page arrives. */
  items: TraceItem[] | null;
  /** The chart's points and the 95th percentile; null before they arrive. */
  durations: TraceDurations | null;
  /** The services of the window, most traces first. */
  services: TraceService[];
  /** Whether older traces can be loaded. */
  hasOlder: boolean;
  /** Whether a request is running. */
  loading: boolean;
  /** Why the latest request failed, or null. */
  error: string | null;
  /** The window the list shows. */
  now: Date;
  /** Loads the next page of older traces. */
  loadOlder: () => void;
}

/**
 * Loads the trace list, its duration chart, and the services of the window for the chosen filters. The three share
 * one window, so the chart and the list agree. A window that ends now looks again every 30 seconds.
 *
 * @param filters - What the person chose.
 * @param environment - The environment chosen in the top bar, or null for all.
 * @returns The list, the chart, the services, and how to load older traces.
 */
export function useTraceList(filters: TraceFilters, environment: string | null): TraceList {
  const picked = filters.from !== null && filters.to !== null;
  const key = `${JSON.stringify(filters)}#${environment ?? ""}`;
  const [tick, setTick] = useState(0);
  const [state, setState] = useState(() => initial(key));

  // Other filters start from nothing; a refresh keeps what is on screen until the new answer replaces it.
  if (state.key !== key) {
    setState({ ...initial(key), tick });
  } else if (state.tick !== tick) {
    setState((previous) => ({ ...previous, tick, loading: true }));
  }

  useEffect(() => {
    if (picked) {
      return undefined;
    }

    const timer = window.setInterval(() => setTick((value) => value + 1), refreshMilliseconds);

    return () => window.clearInterval(timer);
  }, [picked]);

  useEffect(() => {
    const controller = new AbortController();
    const now = new Date();
    const params = filtersToApiParams(filters, now, environment);
    const servicesParams = filtersToApiParams({ ...filters, services: [] }, now, environment);

    Promise.all([
      api.listTraces(params, controller.signal),
      api.traceDurations(params, controller.signal),
      api.traceServices(servicesParams, controller.signal),
    ]).then(
      ([page, durations, services]) =>
        setState((previous) => ({
          ...previous,
          now,
          items: page.items,
          cursor: page.nextCursor,
          durations,
          services,
          loading: false,
          error: null,
        })),
      (error: unknown) => {
        if (!controller.signal.aborted) {
          setState((previous) => ({ ...previous, loading: false, error: message(error) }));
        }
      },
    );

    return () => controller.abort();
    // The key stands for the filters and the environment; tick asks again.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, tick]);

  const loadOlder = useCallback(() => {
    if (state.cursor === null || state.loading) {
      return;
    }

    setState((previous) => ({ ...previous, loading: true }));
    api.listTraces(filtersToApiParams(filters, state.now, environment, state.cursor)).then(
      (page) =>
        setState((previous) => ({
          ...previous,
          items: [...(previous.items ?? []), ...page.items],
          cursor: page.nextCursor,
          loading: false,
        })),
      (error: unknown) => setState((previous) => ({ ...previous, loading: false, error: message(error) })),
    );
  }, [filters, environment, state.cursor, state.loading, state.now]);

  return {
    items: state.items,
    durations: state.durations,
    services: state.services,
    hasOlder: state.cursor !== null,
    loading: state.loading,
    error: state.error,
    now: state.now,
    loadOlder,
  };
}

function initial(key: string) {
  return {
    key,
    tick: 0,
    now: new Date(),
    items: null as TraceItem[] | null,
    cursor: null as string | null,
    durations: null as TraceDurations | null,
    services: [] as TraceService[],
    loading: true,
    error: null as string | null,
  };
}

function message(error: unknown): string {
  return error instanceof ApiError ? error.message : "Veyra could not be reached.";
}
