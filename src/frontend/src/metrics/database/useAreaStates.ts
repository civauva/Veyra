import { api } from "../../api/client";
import { useMetricData } from "../useMetricData";
import { unknownAreas, type AreaId, type AreaState } from "./areas";

/** How often the areas are read again: a rule runs every minute at the most. */
const areasRefreshMilliseconds = 60_000;

/**
 * How each area of a database server is, ranked by the server from its open alerts, the worst first (VEY-68). While
 * they are read it is null; when they cannot be, each area is told in the order of its tab with its weather unknown.
 *
 * @param block - The server's block name, such as `microsoft.sql_server sql-prod-01`.
 * @param areas - The areas the screen shows, in the order of their tabs.
 * @param paused - True while the Overview tab is not shown, so nothing is read for it.
 * @returns The areas, or null until they are read.
 */
export function useAreaStates(block: string, areas: AreaId[], paused: boolean): AreaState[] | null {
  const key = areas.join(",");
  const { data, error } = useMetricData(
    `alert-areas-${block}-${key}`,
    // The server answers with the areas asked for, so each one is an AreaId.
    (signal) => api.alertAreas(block, areas, signal) as Promise<AreaState[]>,
    { refreshMs: areasRefreshMilliseconds, paused },
  );

  if (data === null && error !== null) {
    return unknownAreas(areas);
  }

  return data;
}
