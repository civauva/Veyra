import { useOutletContext } from "react-router";

import type { HomeSnapshot } from "../api/types";
import type { TimeChoice } from "./time";

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

/**
 * The time every screen follows (ADR 0022): the range in the address, how often screens ask again, and whether Live
 * is on. The look's top bar changes them; screens only read them.
 */
export interface ShellTime {
  /** The range the address names, or the last 24 hours. */
  choice: TimeChoice;
  /** How often screens ask the server again, in milliseconds; null when refreshing is off. Every 5 s while Live. */
  refreshMs: number | null;
  /** Whether Live is on: the window ends now and moves, and new records stream in. */
  live: boolean;
  /** The window the range covers at this moment, as ISO instants; a preset is worked out against now. */
  window(): { from: string; to: string };
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
  /**
   * The frame's bar across the top of the screen, which ScreenBar fills (VEY-62); null outside the frame, where
   * ScreenBar draws in place.
   */
  bar: HTMLElement | null;
  /** The shared time; missing outside a shell, as in a component test, where each screen keeps its own timing. */
  time?: ShellTime;
  /**
   * Whether the shell's own top bar chooses the time range, as Console's does. Screens then leave out range choices
   * of their own, so the page has one place to change it; where it is false or missing, screens show their own.
   */
  timeInBar?: boolean;
  /**
   * The place in the shell's top bar for the Simple and Advanced toggle, which ScreenBar fills; null or missing where
   * the toggle ends the screen's own bar, as in Forecast and on phones.
   */
  viewSlot?: HTMLElement | null;
  /**
   * Whether the look shows Simple and Advanced as one Advanced switch instead of a pair of buttons, as Console does;
   * false or missing for the pair.
   */
  viewSwitch?: boolean;
}

/** What a screen sees outside the shell, as in a component test: every environment, and no scene to paint. */
const outside: ShellContext = {
  status: { snapshot: null, error: null, environment: null, choose: () => undefined },
  setScene: () => undefined,
  bar: null,
};

/**
 * The shell's offer to the current screen.
 *
 * @returns The shared status and the scene setter; outside the shell, every environment and no scene.
 */
export function useShell(): ShellContext {
  return useOutletContext<ShellContext | undefined>() ?? outside;
}

/**
 * How often a screen's data asks the server again: the shell's refresh interval, or the screen's own interval outside
 * a shell (ADR 0022).
 *
 * @param own - The screen's own interval in milliseconds, used outside a shell.
 * @returns The interval, or null when the person switched refreshing off.
 */
export function useRefreshEvery(own: number): number | null {
  const { time } = useShell();

  return time ? time.refreshMs : own;
}

/**
 * Whether the shell's top bar chooses the time range, so the screen leaves out range choices of its own (ADR 0022).
 *
 * @returns True inside a shell whose top bar holds the range, as Console's.
 */
export function useTimeInBar(): boolean {
  return useShell().timeInBar === true;
}
