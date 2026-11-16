import { useState } from "react";

import type { Session } from "../api/types";
import { browserZone } from "../time/zone";

const storageKey = "veyra.times";

/** What {@link useTimes} gives the shell. */
export interface Times {
  /** The zone every screen shows times in now: the person's, or UTC when they switched to it. */
  zone: string;
  /** The person's own zone: the one picked on the Account page, or the browser's until then. */
  personal: string;
  /** Whether the person switched to UTC in this browser. */
  utc: boolean;
  /**
   * Switches between the person's zone and UTC, and remembers it in this browser.
   *
   * @param on - True for UTC.
   */
  setUtc(on: boolean): void;
}

/** Whether this browser was switched to UTC. Blocked storage means it was not. */
function readUtc(): boolean {
  try {
    return localStorage.getItem(storageKey) === "utc";
  } catch {
    return false;
  }
}

/**
 * The time zone of every screen (VEY-22): the zone kept on the account, the browser's until the person picks one, or
 * UTC when they switch to it in the account menu. The switch is kept in this browser only, for comparing with logs
 * elsewhere; a failure to save it is ignored.
 *
 * @param session - The signed-in person, with the zone they picked.
 * @returns The zone and the switch.
 */
export function useTimes(session: Session): Times {
  const [utc, setUtcState] = useState(readUtc);
  const personal = session.timeZone ?? browserZone();

  return {
    zone: utc ? "UTC" : personal,
    personal,
    utc,
    setUtc(on: boolean) {
      setUtcState(on);

      try {
        localStorage.setItem(storageKey, on ? "utc" : "local");
      } catch {
        // Storage blocked: the switch holds until the page is reloaded.
      }
    },
  };
}
