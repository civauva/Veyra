import type { AlertAreaState, BlockHealth } from "../../api/types";

/** An area of a SQL Server, each with its own tab (ADR 0020). */
export type AreaId = "live" | "queries" | "waits" | "blocking" | "databases" | "jobs" | "events" | "server";

/** How an area is, from its open alerts, as the server ranks it (VEY-68). */
export interface AreaState extends AlertAreaState {
  /** The area. */
  area: AreaId;
  /** Warning, Caution, or Ok from its alerts; Unknown when the alerts could not be read. */
  health: BlockHealth;
}

/**
 * The areas when their alerts could not be read: each in the order of its tab, its weather unknown. Nothing is ranked
 * here; with the alerts, the server ranks them.
 *
 * @param areas - The areas to tell, in the order of their tabs.
 * @returns Each area, its weather unknown.
 */
export function unknownAreas(areas: AreaId[]): AreaState[] {
  return areas.map((area) => ({ area, health: "Unknown", alerts: [] }));
}
