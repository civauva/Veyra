import { Link, useParams, useSearchParams } from "react-router";

import { api } from "../api/client";
import type { ChartLine, HostMetrics } from "../api/types";
import { useViewMode, ViewToggle } from "../app/ViewToggle";
import { BlockStatus } from "./BlockStatus";
import { ContainerPanel } from "./ContainerPanel";
import { LineChart } from "./LineChart";
import { HostIssues } from "./HostIssues";
import { HostIcon } from "./MetricCards";
import { formatAgo, formatAmount, formatFull, formatRate, formatShare, shareTone } from "./metricFormat";
import { RangePicker, rangeFrom } from "./RangePicker";
import { Ring } from "./Ring";
import { useMetricData } from "./useMetricData";
import "./metrics.css";

/**
 * One host over the chosen range. Simple shows how it is, its rings now, CPU and memory, and its disks; Advanced adds
 * CPU by state and load, cache, network, and disk I/O (VEY-37). Both views list its Docker containers (ADR 0018).
 * Reached from its card on the Metrics overview, and from a container's card at that container's row.
 */
export function HostScreen() {
  const { host = "" } = useParams();
  const [params, setParams] = useSearchParams();
  const range = rangeFrom(params.get("range"));
  const { data, notFound, error } = useMetricData(`host-${host}-${range}`, (signal) =>
    api.hostMetrics(host, range, signal),
  );
  const [mode] = useViewMode();

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
        <ViewToggle />
        <RangePicker value={range} onChange={(next) => setParams({ range: next }, { replace: true })} />
      </div>
      {data && <BlockStatus kind="host" name={host} />}

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
      {data && <HostNow host={data} />}
      {data && <HostIssues host={host} />}
      {data && (mode === "advanced" ? <HostPanels host={data} /> : <HostBasics host={data} />)}
      {data && data.containers.length > 0 && (
        <div className="metric-panels">
          <ContainerPanel host={host} containers={data.containers} range={range} />
        </div>
      )}
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

/** The rings: CPU, memory, and the fullest disk, now. In both views. */
function HostNow({ host }: { host: HostMetrics }) {
  const { card } = host;
  const fullest = card.fullestDisk;

  return (
    <section className="metric-panel metric-now" aria-labelledby="host-now">
      <div className="metric-panel-head">
        <h2 id="host-now">Now</h2>
      </div>
      <div className="rings">
        <Ring share={card.cpuRatio} label="CPU" detail={card.cpus === null ? "" : `of ${card.cpus} cores`} />
        <Ring
          share={card.memoryRatio}
          label="Memory"
          detail={card.memoryBytes === null ? "" : `of ${formatAmount(card.memoryBytes)}`}
        />
        {fullest && (
          <Ring
            share={fullest.ratio}
            label={`Disk ${fullest.mountpoint}`}
            detail={`${formatAmount(fullest.usedBytes)} of ${formatAmount(fullest.totalBytes)}`}
          />
        )}
      </div>
    </section>
  );
}

/** The Simple view: CPU and memory on one chart, and the disks. */
function HostBasics({ host }: { host: HostMetrics }) {
  const total = host.cpu.find((line) => line.label === "total");
  const used = host.memory.find((line) => line.label === "used");

  return (
    <div className="metric-panels">
      <section className="metric-panel" aria-labelledby="host-cpu-memory">
        <div className="metric-panel-head">
          <h2 id="host-cpu-memory">CPU and memory</h2>
        </div>
        <LineChart
          title="CPU and memory"
          max={1}
          format={formatShare}
          series={[
            ...(total ? [{ line: total, color: "var(--accent)", name: "CPU", filled: true }] : []),
            ...(used ? [{ line: used, color: "var(--ok-fg)", name: "Memory" }] : []),
          ]}
        />
      </section>
      <DiskPanel host={host} />
      <p className="metric-more">Load, CPU by state, cache, network, and disk I/O are in the Advanced view.</p>
    </div>
  );
}

/** Every disk with how full it is and when it is full. In both views. */
function DiskPanel({ host }: { host: HostMetrics }) {
  const filling = host.disks.filter((disk) => disk.daysUntilFull !== null).length;

  return (
    <section className="metric-panel wide" aria-labelledby="host-disks">
      <div className="metric-panel-head">
        <h2 id="host-disks">Disks</h2>
        <span>{filling === 0 ? "none filling" : `${filling} filling · at the pace of the last day`}</span>
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
                <b>{formatShare(disk.ratio)}</b> · {sizeOf(disk)} ·{" "}
                <span className={disk.daysUntilFull !== null && disk.daysUntilFull <= 30 ? "warn" : undefined}>
                  {formatFull(disk.daysUntilFull)}
                </span>
              </span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

/** The Advanced view: every panel, the disks included. */
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

      <DiskPanel host={host} />

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

/** How much of a filesystem is used, such as "41 GB of 80 GB". */
function sizeOf(disk: { usedBytes: number; totalBytes: number }): string {
  return `${formatAmount(disk.usedBytes)} of ${formatAmount(disk.totalBytes)}`;
}
