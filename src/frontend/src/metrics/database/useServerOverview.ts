import { api } from "../../api/client";
import type { MetricRange, ServerBackups, ServerOverview } from "../../api/types";
import { useMetricData, type MetricDataState } from "../useMetricData";

/**
 * A SQL Server's overview in one read (ADR 0025): the machine, the figures with their change, performance, storage,
 * blocking, and coverage. It asks again on the shell's refresh, as every screen's data does (ADR 0022).
 *
 * @param engine - The engine, such as `microsoft.sql_server`.
 * @param server - The server.
 * @param range - The range of the top bar.
 * @param system - Whether the server's own databases count too (VEY-66).
 * @returns The overview, whether the server sent nothing yet, and the latest error.
 */
export function useServerOverview(
  engine: string,
  server: string,
  range: MetricRange,
  system: boolean,
): MetricDataState<ServerOverview> {
  return useMetricData(
    `server-overview-${engine}-${server}-${range}`,
    (signal) => api.databaseOverview(engine, server, range, system, signal),
    { choices: String(system) },
  );
}

/**
 * A SQL Server's backups of the range (ADR 0025): each backup, each day, the last 7 days, and freshness per database.
 *
 * @param engine - The engine.
 * @param server - The server.
 * @param range - The range of the top bar.
 * @param system - Whether the server's own databases count too.
 * @returns The backups, whether the server sent nothing yet, and the latest error.
 */
export function useServerBackups(
  engine: string,
  server: string,
  range: MetricRange,
  system: boolean,
): MetricDataState<ServerBackups> {
  return useMetricData(
    `server-backups-${engine}-${server}-${range}`,
    (signal) => api.databaseBackups(engine, server, range, system, signal),
    { choices: String(system) },
  );
}
