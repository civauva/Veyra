import { useCallback, useState } from "react";
import { useSearchParams } from "react-router";

import type { ShellTime } from "./shellContext";
import { timeFrom, timeSearch, windowOf, type TimeChoice } from "./time";

/** The refresh intervals a person can pick, in milliseconds; null is off. */
export const refreshChoices: (number | null)[] = [null, 10_000, 30_000, 60_000, 300_000];

/** The interval until the person picks one. */
export const defaultRefresh = 30_000;

/** How often screens ask again while Live is on. */
export const liveRefresh = 5_000;

const storageKey = "veyra.refresh";

/** Reads the interval this browser keeps. Blocked storage means the default. */
function readRefresh(): number | null {
  try {
    const saved = localStorage.getItem(storageKey);

    if (saved === "off") {
      return null;
    }

    const value = Number(saved);

    return refreshChoices.includes(value) ? value : defaultRefresh;
  } catch {
    return defaultRefresh;
  }
}

/** The shared time with the ways a look's top bar changes it (ADR 0022). */
export interface ShellTimeControls extends ShellTime {
  /** The interval the person picked, whatever Live does to it. */
  refreshChoice: number | null;
  /**
   * Shows another range on every screen; it goes into the address, so a link carries it.
   *
   * @param choice - The range.
   */
  setChoice(choice: TimeChoice): void;
  /**
   * Picks how often screens ask again, kept in this browser.
   *
   * @param every - Milliseconds, or null for off.
   */
  setRefresh(every: number | null): void;
  /**
   * Switches Live on or off.
   *
   * @param on - Whether Live is on.
   */
  setLive(on: boolean): void;
}

/**
 * The shared time every look's shell holds and gives its screens: the range from the address, the refresh interval
 * kept in this browser, and Live, which moves the window with now and asks again every 5 seconds.
 *
 * @returns The time and the ways to change it.
 */
export function useShellTime(): ShellTimeControls {
  const [params, setParams] = useSearchParams();
  const [refreshChoice, setRefreshChoice] = useState<number | null>(readRefresh);
  const [live, setLive] = useState(false);
  const choice = timeFrom(params);

  const setChoice = useCallback(
    (next: TimeChoice) => {
      setParams((current) => {
        const kept = new URLSearchParams(current);

        for (const key of ["range", "from", "to"]) {
          kept.delete(key);
        }

        for (const [key, value] of new URLSearchParams(timeSearch(next))) {
          kept.set(key, value);
        }

        return kept;
      });

      // A window of one's own ends in the past, so it cannot be live.
      if (next.preset === null) {
        setLive(false);
      }
    },
    [setParams],
  );

  const setRefresh = useCallback((every: number | null) => {
    setRefreshChoice(every);

    try {
      localStorage.setItem(storageKey, every === null ? "off" : String(every));
    } catch {
      // Storage blocked: the interval still applies until the page is reloaded.
    }
  }, []);

  return {
    choice,
    window: () => windowOf(choice, Date.now()),
    refreshMs: live ? liveRefresh : refreshChoice,
    live,
    refreshChoice,
    setChoice,
    setRefresh,
    setLive,
  };
}
