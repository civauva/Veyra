import { api } from "../api/client";
import type { AlertContext } from "../api/types";
import { useMetricData, type MetricDataState } from "../metrics/useMetricData";

/**
 * What surrounds an alert (ADR 0029): the blocks it touches with their state now, and the warnings and worse they
 * logged around it, as the server picks them. Asked again with the shell's refresh while the alert is open; a resolved
 * alert's window no longer moves, so it is read once.
 *
 * @param id - The alert's ID.
 * @param resolved - Whether the alert has resolved.
 * @returns The context, whether the alert is unknown, and the latest error.
 */
export function useAlertContext(id: string, resolved: boolean): MetricDataState<AlertContext> {
  return useMetricData(`alert-context-${id}`, (signal) => api.alertContext(id, signal), { paused: resolved });
}
