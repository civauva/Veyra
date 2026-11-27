import { useEffect } from "react";

import { loadRuleCatalog } from "../alerts/ruleCatalog";
import type { Session } from "../api/types";
import type { ShellStatus } from "./shellContext";
import { setDisplayZone } from "../time/zone";
import { useShellStatus } from "./useShellStatus";
import { useTheme, type ThemeName } from "./useTheme";
import { useTimes, type Times } from "./useTimes";

/** What every look's shell works from (ADR 0021). */
export interface ShellFrame {
  /** The chosen environment and its home snapshot, shared with the screens. */
  status: ShellStatus;
  /** The zone times are shown in, with the switch to UTC. */
  times: Times;
  /** The theme in this browser. */
  theme: ThemeName;
  /**
   * Picks another theme.
   *
   * @param choice - The theme.
   */
  setTheme(choice: ThemeName): void;
}

/**
 * What a shell does whatever it looks like, kept once so no look repeats it: it reads the shared status, holds the
 * theme and the times, sets the zone every formatter reads, and loads the alert rule catalog.
 *
 * @param session - The signed-in person, whose zone the times start from.
 * @returns The status, the times, and the theme.
 */
export function useShellFrame(session: Session): ShellFrame {
  const status = useShellStatus();
  const [theme, setTheme] = useTheme();
  const times = useTimes(session);

  // The catalog rules word their alerts from the API (ADR 0020); loaded once, quietly, for every screen.
  useEffect(() => {
    void loadRuleCatalog();
  }, []);

  // Every formatter reads the zone, so it is set before the screens below render (VEY-22).
  setDisplayZone(times.zone);

  return { status, times, theme, setTheme };
}
