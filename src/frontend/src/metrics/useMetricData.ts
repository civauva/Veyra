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

/**
 * Fetches metrics for a screen and keeps them fresh. A failed refresh keeps the last answer on screen with the error
 * beside it. A change of key, such as another range, starts over.
 *
 * @param key - Identifies what is fetched; a new key fetches again at once.
 * @param fetch - Fetches the data, honouring the abort signal.
 * @returns The data, whether it does not exist, and the latest error.
 */
export function useMetricData<T>(key: string, fetch: (signal: AbortSignal) => Promise<T>): MetricDataState<T> {
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

    void load();
    const timer = window.setInterval(() => void load(), refreshMilliseconds);

    return () => {
      stopped = true;
      window.clearInterval(timer);
      controller?.abort();
    };
    // The key stands for everything fetch depends on; fetch itself is a new function on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);

  return { data: state.data, notFound: state.notFound, error: state.error };
}
