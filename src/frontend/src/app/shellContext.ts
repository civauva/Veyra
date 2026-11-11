import { useOutletContext } from "react-router";

import type { HomeSnapshot } from "../api/types";

/** The scene a screen can paint behind the whole shell, top bar included. */
export type ShellScene = "clear" | "showers" | "storm";

/**
 * What every screen shares: the chosen environment and its home snapshot (VEY-23). The shell draws the environment
 * pills, the master lights, and the clock from it on every screen, and screens that can narrow to an environment
 * follow the choice.
 */
export interface ShellStatus {
  /** The latest snapshot of the chosen environment; null before the first one arrives. */
  snapshot: HomeSnapshot | null;
  /** Why the latest snapshot request failed, or null when it worked. */
  error: string | null;
  /** The chosen environment, or null for all of them. */
  environment: string | null;
  /**
   * Chooses another environment for every screen.
   *
   * @param environment - The environment, or null for all.
   */
  choose(environment: string | null): void;
}

/** What the shell offers the screen inside it. */
export interface ShellContext {
  /** The shared status, with the chosen environment. */
  status: ShellStatus;
  /**
   * Paints another scene behind the shell than the environment's weather, or goes back to it with null. The alert
   * details screen paints the alert's own weather; the screen that sets a scene removes it when it closes.
   *
   * @param scene - The scene, or null for the environment's weather.
   */
  setScene(scene: ShellScene | null): void;
}

/** What a screen sees outside the shell, as in a component test: every environment, and no scene to paint. */
const outside: ShellContext = {
  status: { snapshot: null, error: null, environment: null, choose: () => undefined },
  setScene: () => undefined,
};

/**
 * The shell's offer to the current screen.
 *
 * @returns The shared status and the scene setter; outside the shell, every environment and no scene.
 */
export function useShell(): ShellContext {
  return useOutletContext<ShellContext | undefined>() ?? outside;
}
