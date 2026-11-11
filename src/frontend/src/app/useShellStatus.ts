import { useState } from "react";

import { useHomeSnapshot } from "../home/useHomeSnapshot";
import type { ShellStatus } from "./shellContext";

const storageKey = "veyra.home.environment";

/** The environment the person chose in this browser: a name, null for all, or undefined when never chosen. */
function readChoice(): string | null | undefined {
  try {
    const saved = localStorage.getItem(storageKey);

    if (saved === null) {
      return undefined;
    }

    const parsed = JSON.parse(saved) as { environment?: unknown };

    return typeof parsed.environment === "string" || parsed.environment === null ? parsed.environment : undefined;
  } catch {
    return undefined;
  }
}

/** Remembers the choice. It is a convenience, so storage that is blocked or full is ignored. */
function saveChoice(environment: string | null): void {
  try {
    localStorage.setItem(storageKey, JSON.stringify({ environment }));
  } catch {
    // Nothing to do: the choice holds until the page is reloaded.
  }
}

/**
 * The status every screen shares (VEY-23): the chosen environment and the home snapshot of it, which feeds the
 * environment pills, the master lights, the clock, and the weather behind the shell. The snapshot is fetched every
 * 15 seconds whichever screen is open.
 *
 * Until the person picks an environment, production is shown when it exists, otherwise all environments. The choice
 * is kept in this browser.
 *
 * @returns The status, with a way to choose another environment.
 */
export function useShellStatus(): ShellStatus {
  const [chosen, setChosen] = useState<string | null | undefined>(readChoice);
  const [productionSeen, setProductionSeen] = useState(false);

  const environment = chosen !== undefined ? chosen : productionSeen ? "production" : null;
  const { snapshot, error } = useHomeSnapshot(environment);

  // The first snapshot covers all environments; once it shows that production exists, the default moves there.
  if (!productionSeen && snapshot?.environments.some((item) => item.name === "production")) {
    setProductionSeen(true);
  }

  return {
    snapshot,
    error,
    environment,
    choose(next: string | null) {
      setChosen(next);
      saveChoice(next);
    },
  };
}
