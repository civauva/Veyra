import { useSearchParams } from "react-router";

import { api } from "../api/client";
import type { MetricChart, MetricKind, MetricNameInfo, MetricRange } from "../api/types";
import { useMetricData, type MetricDataState } from "./useMetricData";

/** What each kind of metric can show: a gauge its value, a sum its value or rate, a histogram percentiles or rate. */
export const metricShows: Record<MetricKind, string[]> = {
  Gauge: ["value"],
  Sum: ["value", "rate"],
  Histogram: ["p50", "p95", "p99", "rate"],
};

/** What {@link useMetricExplorer} returns. */
export interface MetricExplorer {
  /** The words the metric names are searched for, `q` in the address. */
  search: string;
  /** The chosen metric, `metric` in the address, or null. */
  metric: string | null;
  /** What to show, `show` in the address, or null for what the server picks. */
  show: string | null;
  /** The attribute to split by, `split` in the address, or null for one line. */
  split: string | null;
  /** The service the chart keeps to, `service` in the address, or null for every one (ADR 0029). */
  service: string | null;
  /** The host the chart keeps to, `host` in the address, or null for every one (ADR 0029). */
  host: string | null;
  /** The metric names the server found for the search, ranked by the server. */
  names: MetricDataState<MetricNameInfo[]>;
  /** The chosen metric charted, with each line's figures; null data while nothing is chosen. */
  chart: MetricDataState<MetricChart | null>;
  /** The chosen metric among the names, when it is listed. */
  selected: MetricNameInfo | undefined;
  /**
   * Changes choices in the address, replacing the entry; null or empty removes one.
   *
   * @param changes - The address parameters to change.
   */
  update(changes: Record<string, string | null>): void;
}

/**
 * The metric explorer's state and reads, for every look (ADR 0021): the search, the chosen metric, what it shows, its
 * split, and the service and host it keeps to, all in the address and all sent to the server, which searches, ranks,
 * filters, and works out each line's figures.
 *
 * @param range - The time range of the names and the chart.
 * @returns The choices, the names, the chart, and how to change the choices.
 */
export function useMetricExplorer(range: MetricRange): MetricExplorer {
  const [params, setParams] = useSearchParams();
  const search = params.get("q") ?? "";
  const metric = params.get("metric");
  const show = params.get("show");
  const split = params.get("split");
  const service = params.get("service");
  const host = params.get("host");
  const names = useMetricData(`names-${range}-${search}`, (signal) => api.metricNames(search, range, signal));
  const chart = useMetricData(
    `chart-${range}-${metric ?? ""}-${show ?? ""}-${split ?? ""}-${service ?? ""}-${host ?? ""}`,
    (signal) =>
      metric ? api.metricChart({ name: metric, show, split, service, host, range }, signal) : Promise.resolve(null),
  );

  function update(changes: Record<string, string | null>) {
    const next = new URLSearchParams(params);

    for (const [key, value] of Object.entries(changes)) {
      if (value === null || value === "") {
        next.delete(key);
      } else {
        next.set(key, value);
      }
    }

    setParams(next, { replace: true });
  }

  return {
    search,
    metric,
    show,
    split,
    service,
    host,
    names,
    chart,
    selected: names.data?.find((name) => name.name === metric),
    update,
  };
}
