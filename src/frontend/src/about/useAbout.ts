import { useEffect, useState } from "react";

import { api } from "../api/client";
import type { About } from "../api/types";

/** How often an open page asks which build runs; a page shown again asks at once. */
const everyMs = 5 * 60_000;

/** What the shell shows of the running Veyra (VEY-82). */
export interface AboutState {
  /** The version this page loaded, such as `0.4.1`, or null before the first answer. */
  version: string | null;
  /** A newer build that runs now, which a reload would load, or null. */
  newer: About | null;
  /** Hides the offer of this build; a later one is offered again. */
  dismiss(): void;
}

/**
 * Which build runs now, if not the one the page loaded.
 *
 * @param loaded - What answered when the page loaded, or null before.
 * @param running - What answers now, or null before.
 * @returns The running one when it is another build, or null.
 */
export function newerBuild(loaded: About | null, running: About | null): About | null {
  return loaded && running && running.build !== loaded.build ? running : null;
}

/**
 * The running Veyra, for the version in the shell and the notice that a newer one runs (VEY-82). The first answer is
 * the build this page loaded; the page asks again every few minutes and whenever it is shown again, and a different
 * build means a release ran while the page stayed open. A failed read changes nothing.
 *
 * @returns The version, a newer build or null, and a way to dismiss the offer.
 */
export function useAbout(): AboutState {
  const [loaded, setLoaded] = useState<About | null>(null);
  const [running, setRunning] = useState<About | null>(null);
  const [dismissed, setDismissed] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();

    function read() {
      // Started inside a promise, so even a call that throws at once is a failed read: knowing the version must never
      // take a screen down.
      Promise.resolve()
        .then(() => api.about(controller.signal))
        .then((about) => {
          setLoaded((first) => first ?? about);
          setRunning(about);
        })
        .catch(() => undefined);
    }

    function shown() {
      if (document.visibilityState === "visible") {
        read();
      }
    }

    read();
    const timer = window.setInterval(read, everyMs);
    document.addEventListener("visibilitychange", shown);

    return () => {
      controller.abort();
      window.clearInterval(timer);
      document.removeEventListener("visibilitychange", shown);
    };
  }, []);

  const newer = newerBuild(loaded, running);

  return {
    version: loaded?.version ?? null,
    newer: newer && newer.build !== dismissed ? newer : null,
    dismiss: () => setDismissed(newer?.build ?? null),
  };
}
