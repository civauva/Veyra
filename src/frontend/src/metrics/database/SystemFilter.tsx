import { createContext, useCallback, useContext, useSyncExternalStore } from "react";

import type { AlertListItem, HiddenRows } from "../../api/types";
import "./systemFilter.css";

/*
 * Whether a database server's screen shows what the server does for itself (VEY-64): its own databases, statements
 * that only read or tidy its catalog, the jobs it sets up, and its idle waits. They are hidden until someone asks, so
 * what the screen lists is the applications' work. The choice is sent with every request, and the server leaves the
 * rows out and counts them (VEY-66); the screen never filters. Alerts still watch the hidden rows, and a tab names a
 * hidden one that has an alert open.
 */

/** Where the choice is remembered, in this browser only. One choice serves every server and tab. */
const storageKey = "veyra.system";

/** Components that show or hide system rows, told when the choice changes. */
const listeners = new Set<() => void>();

/** The choice when storage refuses to keep it, as in some private windows. */
let fallback = false;

function read(): boolean {
  try {
    return localStorage.getItem(storageKey) === "shown";
  } catch {
    return fallback;
  }
}

function write(shown: boolean): void {
  fallback = shown;

  try {
    if (shown) {
      localStorage.setItem(storageKey, "shown");
    } else {
      localStorage.removeItem(storageKey);
    }
  } catch {
    // Storage refused: the fallback keeps the choice for this visit.
  }

  listeners.forEach((listener) => listener());
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);

  return () => listeners.delete(listener);
}

/**
 * Whether system rows are shown: off until the person switches them on, then for every server and tab at once.
 *
 * @returns Whether they are shown, and a function that changes it.
 */
export function useSystemShown(): [boolean, (shown: boolean) => void] {
  const shown = useSyncExternalStore(subscribe, read, () => false);
  const set = useCallback((next: boolean) => write(next), []);

  return [shown, set];
}

/**
 * The server's open alerts, for a tab's note to name a hidden database or job that has one. The screen provides them;
 * without it, the note names none.
 */
export const ServerAlerts = createContext<AlertListItem[] | null>(null);

/** The System switch in the server's bar, before Simple and Advanced. */
export function SystemToggle() {
  const [shown, setShown] = useSystemShown();

  return (
    <button
      type="button"
      className="system-toggle beside-trail"
      aria-pressed={shown}
      title={
        shown
          ? "Showing what the server does for itself: its own databases, catalog reads, jobs, and idle waits"
          : "Show what the server does for itself: its own databases, catalog reads, jobs, and idle waits"
      }
      onClick={() => setShown(!shown)}
    >
      <span className="system-toggle-mark" aria-hidden="true" />
      <span className="system-toggle-label">System</span>
    </button>
  );
}

/** A small tag beside a system row's name while system rows are shown, so they stand apart from the applications'. */
export function SystemTag() {
  return (
    <span className="system-tag" title="The server's own, shown because System is on">
      system
    </span>
  );
}

/** Props for {@link SystemHidden}. */
export interface SystemHiddenProps {
  /** What the server left out of the list; its names are matched with the open alerts. */
  hidden: HiddenRows;
  /** What a row is, in the singular: `statement`, `database`, `job`, `wait type`, `index`, or `table`. */
  noun: string;
  /** The plural, when it is not the noun with an s, such as `indexes`. */
  plural?: string;
}

/**
 * One line under a tab's list while system rows are hidden: how many, which of them have an alert open, and a button
 * that shows them. Nothing while none are hidden.
 */
export function SystemHidden({ hidden, noun, plural = `${noun}s` }: SystemHiddenProps) {
  const [shown, setShown] = useSystemShown();
  const alerts = useContext(ServerAlerts);

  const count = hidden.count;

  if (shown || count === 0) {
    return null;
  }

  const names = new Set(hidden.names.map((name) => name.toLowerCase()));
  const alerting = [
    ...new Set(
      (alerts ?? [])
        .map((alert) => alert.block.part)
        .filter((part): part is string => part !== null && names.has(part.toLowerCase())),
    ),
  ];

  return (
    <p className="system-hidden" role="note">
      <span>
        {count.toLocaleString("en-US")} system {count === 1 ? noun : plural} hidden.
        {alerting.length > 0 && (
          <b>
            {" "}
            {words(alerting)} {alerting.length === 1 ? "has an open alert" : "have open alerts"}.
          </b>
        )}
      </span>
      <button type="button" className="link-button" onClick={() => setShown(true)}>
        Show them
      </button>
    </p>
  );
}

/** Names as words: `msdb`, `msdb and master`, or `msdb, master, and model`. */
function words(names: string[]): string {
  if (names.length <= 2) {
    return names.join(" and ");
  }

  return `${names.slice(0, -1).join(", ")}, and ${names[names.length - 1]}`;
}
