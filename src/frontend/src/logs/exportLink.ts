import { api } from "../api/client";
import { filtersToApiParams, type LogFilters } from "./logFilters";

/** The formats the server exports in (ADR 0024). */
export type LogExportFormat = "ndjson" | "csv";

/**
 * Starts the server's export of every record the filters match, not only the loaded page (ADR 0024). The browser
 * follows the address as a download, so the file streams from the server and nothing is held here.
 *
 * @param filters - What the screen filters by.
 * @param environment - The environment chosen in the top bar, or null for all.
 * @param format - NDJSON or CSV.
 */
export function exportLogs(filters: LogFilters, environment: string | null, format: LogExportFormat): void {
  const params = filtersToApiParams(filters, new Date());

  if (environment) {
    params.set("environment", environment);
  }

  window.location.assign(api.logExportUrl(params, format));
}
