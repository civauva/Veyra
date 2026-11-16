import { useEffect, useState } from "react";

import { ApiError } from "../api/client";

/** How often a metrics screen asks again. Host metrics arrive every 30 seconds, so faster shows nothing new. */
export const refreshMilliseconds = 30_000;

/** What {@link useMetricData} returns. */
export interface MetricDataState<T> {
  /** The latest answer, kept when a later refresh fails; null before the first one. */
  data: T | null;
  /** True when the API answered 404: the host or service sent nothing within the range. */
  notFound: boolean;
  /** Why the latest request failed, or null when it worked. */
  error: string | null;
}

/** How {@link useMetricData} keeps its data fresh. */
export interface MetricDataOptions {
  /** How often it asks again, in milliseconds; {@link refreshMilliseconds} by default. */
  refreshMs?: number;
  /** True to fetch once and not ask again, as when looking back at a past moment. */
  paused?: boolean;
}

/**
 * Fetches metrics for a screen and keeps them fresh. A failed refresh keeps the last answer on screen with the error
 * beside it. A change of key, such as another range, starts over. While the browser tab is hidden it does not ask, and
 * it asks at once when the tab shows again.
 *
 * @param key - Identifies what is fetched; a new key fetches again at once.
 * @param fetch - Fetches the data, honouring the abort signal.
 * @param options - How often it asks again, or not at all.
 * @returns The data, whether it does not exist, and the latest error.
 */
export function useMetricData<T>(
  key: string,
  fetch: (signal: AbortSignal) => Promise<T>,
  options: MetricDataOptions = {},
): MetricDataState<T> {
  const refreshMs = options.refreshMs ?? refreshMilliseconds;
  const paused = options.paused ?? false;
  const [state, setState] = useState<MetricDataState<T> & { key: string }>({
    key,
    data: null,
    notFound: false,
    error: null,
  });

  // Another key starts from nothing, not from the previous answer.
  if (state.key !== key) {
    setState({ key, data: null, notFound: false, error: null });
  }

  useEffect(() => {
    let controller: AbortController | null = null;
    let stopped = false;

    async function load() {
      controller?.abort();
      controller = new AbortController();

      try {
        const data = await fetch(controller.signal);

        if (!stopped) {
          setState({ key, data, notFound: false, error: null });
        }
      } catch (error) {
        if (stopped || (error instanceof DOMException && error.name === "AbortError")) {
          return;
        }

        if (error instanceof ApiError && error.status === 404) {
          setState({ key, data: null, notFound: true, error: null });

          return;
        }

        const message = error instanceof ApiError ? error.message : "Veyra could not be reached.";
        setState((previous) => ({ ...previous, error: message }));
      }
    }

    function shown() {
      if (document.visibilityState === "visible") {
        void load();
      }
    }

    void load();

    if (paused) {
      return () => {
        stopped = true;
        controller?.abort();
      };
    }

    const timer = window.setInterval(() => {
      if (document.visibilityState !== "hidden") {
        void load();
      }
    }, refreshMs);
    document.addEventListener("visibilitychange", shown);

    return () => {
      stopped = true;
      window.clearInterval(timer);
      document.removeEventListener("visibilitychange", shown);
      controller?.abort();
    };
    // The key stands for everything fetch depends on; fetch itself is a new function on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, refreshMs, paused]);

  return { data: state.data, notFound: state.notFound, error: state.error };
}
