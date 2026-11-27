import { useEffect, useState } from "react";

/** Events per second arriving now, by signal, as the server's live stream says (ADR 0022). */
export interface LiveRate {
  /** Log records per second. */
  logs: number;
  /** Spans per second. */
  spans: number;
  /** Metric points per second. */
  points: number;
}

/**
 * The rate of telemetry arriving, from `GET /api/v1/live`, while Live is on. The browser's event source reconnects
 * by itself; when it is not available, as in component tests, nothing is asked.
 *
 * @param live - Whether Live is on; the stream is open only then.
 * @param environment - The chosen environment, or null for all.
 * @returns The latest rate, or null before the first one and while Live is off.
 */
export function useLiveRate(live: boolean, environment: string | null): LiveRate | null {
  const [rate, setRate] = useState<LiveRate | null>(null);

  useEffect(() => {
    if (!live || typeof EventSource !== "function") {
      return undefined;
    }

    const query = environment ? `?environment=${encodeURIComponent(environment)}` : "";
    const source = new EventSource(`/api/v1/live${query}`, { withCredentials: true });

    source.addEventListener("rate", (event) => {
      try {
        setRate(JSON.parse((event as MessageEvent<string>).data) as LiveRate);
      } catch {
        // A line the browser could not read is skipped; the next one follows within a second.
      }
    });

    return () => {
      source.close();
      setRate(null);
    };
  }, [live, environment]);

  return live ? rate : null;
}
