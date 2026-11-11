import { useEffect, useState } from "react";

import { api, ApiError } from "../api/client";
import type { AlertDetails } from "../api/types";

/** How often an open alert is fetched again: as often as the home, so both tell the same story. */
export const refreshMilliseconds = 15_000;

/** What {@link useAlertDetails} returns. */
export interface AlertDetailsState {
  /** The latest details, kept when a later refresh fails; null before they arrive. */
  alert: AlertDetails | null;
  /** True when there is no such alert, or it is older than 30 days. */
  notFound: boolean;
  /** Why the latest request failed, or null when it worked. */
  error: string | null;
}

/**
 * Fetches one alert and keeps it fresh while it is open. A resolved alert does not change, so polling stops.
 *
 * @param id - The alert ID from the URL.
 * @returns The details, whether the alert exists, and the latest error.
 */
export function useAlertDetails(id: string): AlertDetailsState {
  const [state, setState] = useState<AlertDetailsState & { id: string }>({
    id,
    alert: null,
    notFound: false,
    error: null,
  });

  // Another alert in the URL starts from nothing, not from the previous alert's picture.
  if (state.id !== id) {
    setState({ id, alert: null, notFound: false, error: null });
  }

  const resolved = state.id === id && state.alert?.resolvedAt != null;

  useEffect(() => {
    let controller: AbortController | null = null;
    let stopped = false;

    async function load() {
      controller?.abort();
      controller = new AbortController();

      try {
        const alert = await api.alert(id, controller.signal);

        if (!stopped) {
          setState({ id, alert, notFound: false, error: null });
        }
      } catch (error) {
        if (stopped || (error instanceof DOMException && error.name === "AbortError")) {
          return;
        }

        if (error instanceof ApiError && error.status === 404) {
          setState({ id, alert: null, notFound: true, error: null });

          return;
        }

        const message = error instanceof ApiError ? error.message : "Veyra could not be reached.";
        setState((previous) => ({ ...previous, error: message }));
      }
    }

    void load();
    const timer = resolved ? null : window.setInterval(() => void load(), refreshMilliseconds);

    return () => {
      stopped = true;

      if (timer !== null) {
        window.clearInterval(timer);
      }

      controller?.abort();
    };
  }, [id, resolved]);

  return { alert: state.alert, notFound: state.notFound, error: state.error };
}
