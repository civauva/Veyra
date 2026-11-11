import { useSearchParams } from "react-router";

import { api } from "../api/client";
import type { MetricChart, MetricKind, MetricRange } from "../api/types";
import { LineChart } from "./LineChart";
import { formatAmount, formatRate, formatSeconds } from "./metricFormat";
import { useMetricData } from "./useMetricData";

/** The colours of the browser's lines, in order. */
export const lineColors = [
  "var(--metric-violet)",
  "var(--warning-fg)",
  "var(--accent)",
  "var(--ok-fg)",
  "var(--error-fg)",
  "var(--metric-teal)",
  "var(--metric-pink)",
  "var(--muted)",
];

/** What each kind can show. */
const shows: Record<MetricKind, string[]> = {
  Gauge: ["value"],
  Sum: ["value", "rate"],
  Histogram: ["p50", "p95", "p99", "rate"],
};

/** Props for {@link MetricBrowser}. */
interface MetricBrowserProps {
  /** The range of the chart. */
  range: MetricRange;
}

/**
 * Every metric name, searchable, and one chosen metric charted: as one line or split by an attribute, showing values,
 * rates, or percentiles as its kind allows. The choice is kept in the URL.
 */
export function MetricBrowser({ range }: MetricBrowserProps) {
  const [params, setParams] = useSearchParams();
  const search = params.get("q") ?? "";
  const metric = params.get("metric");
  const show = params.get("show");
  const split = params.get("split");
  const names = useMetricData(`names-${range}-${search}`, (signal) => api.metricNames(search, range, signal));
  const chart = useMetricData(`chart-${range}-${metric ?? ""}-${show ?? ""}-${split ?? ""}`, (signal) =>
    metric ? api.metricChart({ name: metric, show, split, range }, signal) : Promise.resolve(null),
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

  const list = names.data ?? [];
  const selected = list.find((name) => name.name === metric);

  return (
    <div className="metric-browser">
      <div className="metric-list">
        <label className="metric-list-search">
          <span className="visually-hidden">Find a metric</span>
          <input
            type="search"
            value={search}
            placeholder="Find a metric, for example http.server"
            onChange={(event) => update({ q: event.target.value })}
          />
        </label>
        <ul aria-label="Metric names">
          {list.map((name) => (
            <li key={name.name}>
              <button
                type="button"
                className={name.name === metric ? "on" : undefined}
                aria-pressed={name.name === metric}
                onClick={() => update({ metric: name.name, show: null, split: null })}
              >
                <code>{name.name}</code>
                <span className="metric-kind">{name.kind.toLowerCase()}</span>
                <small>
                  {[name.unit || null, `${name.series} series`, name.sources.join(", ") || null]
                    .filter(Boolean)
                    .join(" · ")}
                </small>
              </button>
            </li>
          ))}
        </ul>
        {names.data && list.length === 0 && <p className="metrics-empty">No metric name contains “{search}”.</p>}
      </div>

      <section className="metric-panel metric-chart-panel" aria-label={metric ?? "Chart"}>
        {!metric ? (
          <p className="metrics-empty">Pick a metric on the left to chart it.</p>
        ) : chart.data ? (
          <ChartView
            chart={chart.data}
            description={selected?.description ?? ""}
            onShow={(next) => update({ show: next })}
            onSplit={(next) => update({ split: next })}
          />
        ) : chart.notFound ? (
          <p className="metrics-empty">
            No series of {metric} within the last {range}.
          </p>
        ) : (
          <p className="metrics-empty">Charting {metric}…</p>
        )}
      </section>
    </div>
  );
}

/** Props for {@link ChartView}. */
interface ChartViewProps {
  /** The chart. */
  chart: MetricChart;
  /** The metric's description, as sent. */
  description: string;
  /**
   * Called when the person picks what to show.
   *
   * @param show - Value, rate, or a percentile.
   */
  onShow(show: string): void;
  /**
   * Called when the person picks an attribute to split by, or none.
   *
   * @param split - The attribute, or null for one line.
   */
  onSplit(split: string | null): void;
}

function ChartView({ chart, description, onShow, onSplit }: ChartViewProps) {
  const format = unitFormat(chart.unit, chart.show);

  return (
    <>
      <div className="metric-panel-head">
        <h2>{chart.name}</h2>
        <span>
          {chart.show} · {chart.unit || "no unit"} · {chart.kind.toLowerCase()}
        </span>
      </div>
      {description && <p className="metric-description">{description}</p>}
      <div className="chip-row" role="group" aria-label="Show">
        <span>Show</span>
        {shows[chart.kind].map((option) => (
          <button
            key={option}
            type="button"
            className={option === chart.show ? "chip on" : "chip"}
            aria-pressed={option === chart.show}
            onClick={() => onShow(option)}
          >
            {option}
          </button>
        ))}
      </div>
      <div className="chip-row" role="group" aria-label="Split by">
        <span>Split by</span>
        <button
          type="button"
          className={chart.split === null ? "chip on" : "chip"}
          aria-pressed={chart.split === null}
          onClick={() => onSplit(null)}
        >
          nothing
        </button>
        {chart.splitKeys.map((key) => (
          <button
            key={key}
            type="button"
            className={key === chart.split ? "chip on" : "chip"}
            aria-pressed={key === chart.split}
            onClick={() => onSplit(key)}
          >
            {key}
          </button>
        ))}
      </div>
      <LineChart
        title={`${chart.name} ${chart.show}`}
        height={300}
        format={format}
        series={chart.lines.map((line, index) => ({ line, color: lineColors[index % lineColors.length] ?? "" }))}
      />
      {chart.kind === "Histogram" && chart.show !== "rate" && (
        <p className="metric-description">
          Percentiles are estimated from the histogram&apos;s buckets, the way Prometheus does it. Values above the
          highest bucket count as that bucket&apos;s bound.
        </p>
      )}
    </>
  );
}

/**
 * How values of a unit read.
 *
 * @param unit - The unit as sent, with `/s` for rates.
 * @param show - What the chart shows.
 * @returns A formatter.
 */
export function unitFormat(unit: string, show: string): (value: number) => string {
  const base = unit.replace(/\/s$/, "");
  const rate = unit.endsWith("/s");

  if (base === "s" && !rate) {
    return formatSeconds;
  }

  if (base === "By") {
    return (value) => formatAmount(value, rate);
  }

  if (base === "1" && show === "value") {
    return (value) => formatRate(value);
  }

  const suffix = base && !base.startsWith("{") ? ` ${base}` : "";

  return (value) => `${formatRate(value)}${suffix}${rate ? "/s" : ""}`;
}
