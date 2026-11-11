import { useSyncExternalStore } from "react";

/**
 * How the home is laid out for the width of the screen (VEY-24), at natural size in every case:
 *
 * - `standard`: the Forecast and Needs you, as in the approved mockups; below 1200 px they stack.
 * - `wide`: from 1800 px, a third column between them lists every service, host, and disk.
 * - `wall`: from 2400 px wide and 1200 px tall, bands fill the screen with the last 24 hours, the last 30 days, and the
 *   latest alerts. A screen as wide but less tall, such as an ultrawide 2560 × 1080, keeps the wide layout, since the
 *   bands need the height.
 */
export type HomeLayout = "standard" | "wide" | "wall";

/** The media query of the wide layout. */
export const wideQuery = "(min-width: 1800px)";

/** The media query of the wall. */
export const wallQuery = "(min-width: 2400px) and (min-height: 1200px)";

function matches(query: string): boolean {
  return typeof window.matchMedia === "function" && window.matchMedia(query).matches;
}

function read(): HomeLayout {
  return matches(wallQuery) ? "wall" : matches(wideQuery) ? "wide" : "standard";
}

function subscribe(changed: () => void): () => void {
  if (typeof window.matchMedia !== "function") {
    return () => {};
  }

  const lists = [window.matchMedia(wideQuery), window.matchMedia(wallQuery)];

  for (const list of lists) {
    list.addEventListener("change", changed);
  }

  return () => {
    for (const list of lists) {
      list.removeEventListener("change", changed);
    }
  };
}

/**
 * The layout for the current width, updated when the window is resized across a step.
 *
 * The layouts differ in what they show, not only in where it sits: the wall reads the alert history, which the
 * smaller layouts do not need, so the choice is made here rather than in CSS alone.
 *
 * @returns The layout.
 */
export function useHomeLayout(): HomeLayout {
  return useSyncExternalStore(subscribe, read, () => "standard");
}
