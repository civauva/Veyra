import { useEffect, useState } from "react";

import { api, ApiError } from "../api/client";
import type { HomeSnapshot } from "../api/types";

/** How often the snapshot is fetched again (home dashboards design: polled every 15 seconds). */
export const refreshMilliseconds = 15_000;

/** What {@link useHomeSnapshot} returns. */
export interface HomeSnapshotState {
  /** The latest snapshot, kept when a later refresh fails; null before the first one arrives. */
  snapshot: HomeSnapshot | null;
  /** Why the latest request failed, or null when it worked. */
  error: string | null;
}

/**
 * Fetches the home snapshot and keeps it fresh every 15 seconds. A failed refresh keeps the last picture on screen
 * and reports the error beside it, so a short outage does not blank the home.
 *
 * @param environment - The environment to show, or null for all of them.
 * @returns The latest snapshot and the latest error.
 */
export function useHomeSnapshot(environment: string | null): HomeSnapshotState {
  const [state, setState] = useState<HomeSnapshotState>({ snapshot: null, error: null });

  useEffect(() => {
    let controller: AbortController | null = null;
    let stopped = false;

    async function load() {
      controller?.abort();
      controller = new AbortController();

      try {
        const snapshot = await api.home(environment, controller.signal);

        if (!stopped) {
          setState({ snapshot, error: null });
        }
      } catch (error) {
        if (stopped || (error instanceof DOMException && error.name === "AbortError")) {
          return;
        }

        const message = error instanceof ApiError ? error.message : "Veyra could not be reached.";
        setState((previous) => ({ snapshot: previous.snapshot, error: message }));
      }
    }

    void load();
    const timer = window.setInterval(() => void load(), refreshMilliseconds);

    return () => {
      stopped = true;
      window.clearInterval(timer);
      controller?.abort();
    };
  }, [environment]);

  return state;
}
