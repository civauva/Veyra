import type { HomeSnapshot } from "../api/types";

/** The open alerts of an environment, as every look's shell shows them beside Alerts. */
export interface AlertCounts {
  /** Open warnings. */
  warning: number;
  /** Open cautions. */
  caution: number;
  /** The same in words, for screen readers. */
  words: string;
}

/**
 * The open alerts of the chosen environment, in numbers and in words. The server counts them in the snapshot.
 *
 * @param snapshot - The latest snapshot, or null before it arrives.
 * @returns The counts and a sentence for screen readers, or null without a snapshot.
 */
export function alertCounts(snapshot: HomeSnapshot | null): AlertCounts | null {
  if (!snapshot) {
    return null;
  }

  const { warning, caution } = snapshot.counts;
  const parts = [
    warning > 0 ? `${warning} ${warning === 1 ? "warning" : "warnings"}` : "",
    caution > 0 ? `${caution} ${caution === 1 ? "caution" : "cautions"}` : "",
  ].filter(Boolean);

  return { warning, caution, words: parts.length === 0 ? "No open alerts" : parts.join(", ") };
}
