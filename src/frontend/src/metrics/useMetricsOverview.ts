import { api } from "../api/client";
import type { MetricRange, MetricsOverview } from "../api/types";
import { useMetricData, type MetricDataState } from "./useMetricData";

/**
 * The Metrics overview for every look: the hosts, services, database servers, and containers that send metrics, with
 * the counts over them. The server keeps only those of the search and the environment (VEY-67); a change of either asks
 * again at once and keeps the last answer on screen until the new one arrives.
 *
 * @param range - The window of the cards' lines.
 * @param search - The words a host or service must hold.
 * @param environment - The environment chosen in the shell, or null for all.
 * @returns The overview, and the latest error.
 */
export function useMetricsOverview(
  range: MetricRange,
  search: string,
  environment: string | null,
): MetricDataState<MetricsOverview> {
  const choices = { search, environment };

  return useMetricData(`overview-${range}`, (signal) => api.metricsOverview(range, choices, signal), {
    choices: JSON.stringify(choices),
  });
}
