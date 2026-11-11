import { Link, useParams, useSearchParams } from "react-router";

import { api } from "../api/client";
import type { ChartLine, HostMetrics } from "../api/types";
import { LineChart } from "./LineChart";
import { HostIcon } from "./MetricCards";
import { formatAgo, formatAmount, formatFull, formatRate, formatShare, shareTone } from "./metricFormat";
import { RangePicker, rangeFrom } from "./RangePicker";
import { useMetricData } from "./useMetricData";
import "./metrics.css";

/**
 * One host: CPU by state, memory, every disk with when it is full, network, disk I/O, and load, over the chosen
 * range. Reached from its card on the Metrics overview.
 */
export function HostScreen() {
  const { host = "" } = useParams();
  const [params, setParams] = useSearchParams();
  const range = rangeFrom(params.get("range"));
  const { data, notFound, error } = useMetricData(`host-${host}-${range}`, (signal) =>
    api.hostMetrics(host, range, signal),
  );

  return (
    <main className="metrics">
      <Link className="metrics-back" to={`/metrics?range=${range}`}>
        ← Metrics
      </Link>
      <div className="metrics-detail-head">
        <span className="metric-icon large">
          <HostIcon size={26} />
        </span>
        <div>
          <h1>{host}</h1>
          {data && <Tags host={data} />}
        </div>
        <RangePicker value={range} onChange={(next) => setParams({ range: next }, { replace: true })} />
      </div>

      {error && (
        <p className="metrics-error" role="alert">
          {data ? `Could not refresh: ${error}.` : `The host could not be loaded: ${error}`}
        </p>
      )}
      {notFound && (
        <p className="metrics-empty">
          {host} sent no host metrics within the last {range}.
        </p>
      )}
      {data && <HostPanels host={data} />}
    </main>
  );
}

function Tags({ host }: { host: HostMetrics }) {
  const { card } = host;
  const tags = [
    card.os,
    card.cpus === null ? null : `${card.cpus} CPUs`,
    card.memoryBytes === null ? null : formatAmount(card.memoryBytes),
    card.environment,
    `last point ${formatAgo(card.lastSeen, new Date())}`,
  ].filter((tag): tag is string => Boolean(tag));

  return (
    <ul className="metric-tags">
      {tags.map((tag) => (
        <li key={tag}>{tag}</li>
      ))}
    </ul>
  );
}

function HostPanels({ host }: { host: HostMetrics }) {
  const line = (lines: ChartLine[], label: string) => lines.find((one) => one.label === label);
  const series = (lines: ChartLine[], picks: [string, string, string?][]) =>
    picks.flatMap(([label, color, name]) => {
      const found = line(lines, label);

      return found ? [{ line: found, color, name: name ?? label }] : [];
    });
  const { card } = host;

  return (
    <div className="metric-panels">
      <section className="metric-panel" aria-labelledby="host-cpu">
        <div className="metric-panel-head">
          <h2 id="host-cpu">CPU</h2>
          <span className="metric-big">{formatShare(card.cpuRatio)}</span>
        </div>
        <LineChart
          title="CPU in use"
          max={1}
          format={formatShare}
          series={[
            ...series(host.cpu, [["total", "var(--accent)"]]).map((one) => ({ ...one, filled: true })),
            ...series(host.cpu, [
              ["user", "var(--metric-violet)"],
              ["system", "var(--metric-teal)"],
              ["wait", "var(--warning-fg)", "iowait"],
            ]),
          ]}
        />
        <p className="metric-note">
          Load {host.load.map((one) => (one.latest === null ? "—" : formatRate(one.latest))).join(" · ")} (1, 5, 15
          minutes)
        </p>
      </section>

      <section className="metric-panel" aria-labelledby="host-memory">
        <div className="metric-panel-head">
          <h2 id="host-memory">Memory</h2>
          <span className="metric-big">
            {formatShare(card.memoryRatio)}
            {card.memoryBytes !== null && card.memoryRatio !== null && (
              <small>
                {" "}
                · {formatAmount(card.memoryRatio * card.memoryBytes)} of {formatAmount(card.memoryBytes)}
              </small>
            )}
          </span>
        </div>
        <LineChart
          title="Memory"
          max={1}
          format={formatShare}
          series={[
            ...series(host.memory, [["used", "var(--ok-fg)"]]).map((one) => ({ ...one, filled: true })),
            ...series(host.memory, [["cache", "var(--muted)", "cache and buffers"]]).map((one) => ({
              ...one,
              dashed: true,
            })),
          ]}
        />
      </section>

      <section className="metric-panel wide" aria-labelledby="host-disks">
        <div className="metric-panel-head">
          <h2 id="host-disks">Disks</h2>
          <span>
            {host.disks.filter((disk) => disk.daysUntilFull !== null).length === 0
              ? "none filling"
              : `${host.disks.filter((disk) => disk.daysUntilFull !== null).length} filling · at the pace of the last day`}
          </span>
        </div>
        {host.disks.length === 0 ? (
          <p className="metrics-empty">The Collector reports no filesystems for this host.</p>
        ) : (
          <ul className="disk-list">
            {host.disks.map((disk) => (
              <li key={disk.mountpoint}>
                <code>{disk.mountpoint}</code>
                <span
                  className="disk-bar"
                  role="meter"
                  aria-label={`${disk.mountpoint} in use`}
                  aria-valuemin={0}
                  aria-valuemax={100}
                  aria-valuenow={Math.round(disk.ratio * 100)}
                >
                  <i className={shareTone(disk.ratio)} style={{ width: `${disk.ratio * 100}%` }} />
                </span>
                <span className="disk-text">
                  <b>{formatShare(disk.ratio)}</b> · {formatAmount(disk.usedBytes)} of {formatAmount(disk.totalBytes)} ·{" "}
                  <span className={disk.daysUntilFull !== null && disk.daysUntilFull <= 30 ? "warn" : undefined}>
                    {formatFull(disk.daysUntilFull)}
                  </span>
                </span>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="metric-panel" aria-labelledby="host-network">
        <div className="metric-panel-head">
          <h2 id="host-network">Network</h2>
        </div>
        <LineChart
          title="Network"
          format={(value) => formatAmount(value, true)}
          series={series(host.network, [
            ["received", "var(--accent)"],
            ["sent", "var(--metric-violet)"],
          ])}
        />
      </section>

      <section className="metric-panel" aria-labelledby="host-io">
        <div className="metric-panel-head">
          <h2 id="host-io">Disk I/O</h2>
        </div>
        <LineChart
          title="Disk I/O"
          format={(value) => formatAmount(value, true)}
          series={series(host.diskIo, [
            ["read", "var(--accent)"],
            ["written", "var(--warning-fg)"],
          ])}
        />
      </section>
    </div>
  );
}
