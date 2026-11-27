import { useCallback, useSyncExternalStore } from "react";

import type { AlertListItem, AlertSeverity } from "../api/types";

/** Where the dismissed banners are remembered in this browser. */
const storageKey = "veyra.banner.dismissed";

/** The alerts open when a block's banner was dismissed: by alert ID, with its severity then. */
export type DismissedAlerts = Record<string, AlertSeverity>;

/** The dismissed alerts of every block, by block. */
type Dismissed = Record<string, DismissedAlerts>;

/** Nothing dismissed; one object, so a block without a dismissal keeps the same snapshot. */
const none: DismissedAlerts = {};

/** The parts of the screen to tell when a block's alerts are dismissed: the banner and the rail. */
const listeners = new Set<() => void>();

/** The last text read from storage, and what it holds, so a snapshot only changes when the text does. */
let cache: { saved: string | null; dismissed: Dismissed } = { saved: null, dismissed: {} };

/**
 * The alerts dismissed on one host, service, or database server, and a way to dismiss them (VEY-62). The banner and
 * the rail beside the screen share it (VEY-63), so hiding one hides the other; it is kept in this browser only.
 *
 * @param block - The block's kind and name, as `database:microsoft.sql_server sql-prod-01`.
 * @returns The dismissed alerts, and a function that dismisses the alerts open now.
 */
export function useDismissedAlerts(block: string): [DismissedAlerts, (open: AlertListItem[]) => void] {
  const dismissed = useSyncExternalStore(subscribe, () => readAll()[block] ?? none);

  const dismiss = useCallback(
    (open: AlertListItem[]) => {
      saveAll({ ...readAll(), [block]: Object.fromEntries(open.map((alert) => [alert.id, alert.severity])) });
      listeners.forEach((listener) => listener());
    },
    [block],
  );

  return [dismissed, dismiss];
}

/**
 * Whether every open alert was dismissed as it is now: a new alert, or one whose severity changed, shows again.
 *
 * @param open - The open alerts.
 * @param dismissed - The alerts dismissed.
 * @returns True when there is something open and all of it was dismissed.
 */
export function allDismissed(open: AlertListItem[], dismissed: DismissedAlerts): boolean {
  return open.length > 0 && open.every((alert) => dismissed[alert.id] === alert.severity);
}

/** Tells a part of the screen when a dismissal changes. */
function subscribe(listener: () => void): () => void {
  listeners.add(listener);

  return () => listeners.delete(listener);
}

/** The dismissed banners of this browser; none when storage is blocked or holds something else. */
function readAll(): Dismissed {
  let saved: string | null;

  try {
    saved = localStorage.getItem(storageKey);
  } catch {
    return cache.dismissed;
  }

  if (saved !== cache.saved) {
    cache = { saved, dismissed: parse(saved) };
  }

  return cache.dismissed;
}

/** The dismissals in a saved text, or none when it holds something else. */
function parse(saved: string | null): Dismissed {
  try {
    const parsed: unknown = saved === null ? null : JSON.parse(saved);

    return parsed !== null && typeof parsed === "object" ? (parsed as Dismissed) : {};
  } catch {
    return {};
  }
}

/**
 * Remembers the dismissals. It is a convenience, so storage that is blocked or full keeps them for this visit only.
 *
 * @param dismissed - Every block's dismissed alerts.
 */
function saveAll(dismissed: Dismissed): void {
  const saved = JSON.stringify(dismissed);

  cache = { saved, dismissed };

  try {
    localStorage.setItem(storageKey, saved);
  } catch {
    // Nothing to do: the cache keeps them until the page is loaded again.
  }
}
