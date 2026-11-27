import { useState, type ReactNode } from "react";
import { Link, useSearchParams } from "react-router";

import { databasePath, engineName } from "../../../alerts/ruleWords";
import type { MetricChart, MetricRange, MetricsOverview } from "../../../api/types";
import { useShell } from "../../../app/shellContext";
import { LineChart } from "../../../metrics/LineChart";
import { unitFormat } from "../../../metrics/MetricBrowser";
import {
  formatAmount,
  formatCpuPercent,
  formatFull,
  formatRate,
  formatSeconds,
  formatShare,
} from "../../../metrics/metricFormat";
import { rangeOf } from "../../../metrics/metricRange";
import { metricShows, useMetricExplorer, type MetricExplorer } from "../../../metrics/useMetricExplorer";
import { useMetricsOverview } from "../../../metrics/useMetricsOverview";
import { HelpTip } from "../../../ui/HelpTip";
import { ScreenBar } from "../../../ui/ScreenBar";
import { useViewMode } from "../../../ui/ViewToggle";
import { ConsoleIcon } from "../ConsoleIcon";
import { SqlFigure } from "../database/sqlParts";
import "./consoleMetrics.css";

/** The colours of the chart's lines in Console, in the order the server sends the lines. */
const lineColors = [
  "var(--accent)",
  "var(--info-fg)",
  "var(--cn-violet, #a78bfa)",
  "var(--warning-fg)",
  "var(--error-fg)",
  "var(--ok-fg)",
  "var(--metric-pink, #f472b6)",
  "var(--faint)",
];

/**
 * Console's Metrics (VEY-79, ADR 0029): the Overview of every host, service, database server, and container that sends
 * metrics, and the Explorer of every metric name: the server finds and ranks the names, charts the chosen one with what
 * to show, its split, and the service and host it keeps to, and works out each line's latest, average, and highest
 * value. Every choice lives in the address. The time range is the top bar's.
 */
export function ConsoleMetrics() {
  const [params, setParams] = useSearchParams();
  const range = rangeOf(params);
  const explorer = params.get("tab") === "all";
  const [mode] = useViewMode();
  const advanced = mode === "advanced";
  const [copied, setCopied] = useState(false);

  function tab(all: boolean) {
    const next = new URLSearchParams(params);

    if (all) {
      next.set("tab", "all");
    } else {
      for (const key of ["tab", "metric", "show", "split", "service", "host", "q"]) {
        next.delete(key);
      }
    }

    setParams(next, { replace: true });
  }

  return (
    <main className="cn-screen sq mx">
      <ScreenBar trail={[{ label: "Metrics" }]} view />
      <div className="cn-head">
        <div>
          <h1>Metrics</h1>
          <p>
            {explorer
              ? "Every metric Veyra receives. Pick one, choose what to show, and split it by an attribute."
              : "Every host, service, database server, and container that sends metrics, and how each is doing now."}
          </p>
        </div>
        <div className="cn-head-actions">
          <button
            type="button"
            className="cn-button"
            onClick={() => {
              void navigator.clipboard?.writeText(window.location.href);
              setCopied(true);
            }}
          >
            {copied ? "Link copied" : "Copy link"}
          </button>
        </div>
      </div>

      {(advanced || explorer) && (
        <div className="cn-tabs mx-tabs" role="tablist" aria-label="Views">
          <button type="button" role="tab" aria-selected={!explorer} onClick={() => tab(false)}>
            Overview
          </button>
          <button type="button" role="tab" aria-selected={explorer} onClick={() => tab(true)}>
            Explorer
          </button>
        </div>
      )}

      {explorer ? <Explorer range={range} /> : <Overview range={range} advanced={advanced} />}
    </main>
  );
}

/** Props for {@link Overview}. */
interface OverviewProps {
  /** The range of the overview. */
  range: MetricRange;
  /** Whether the Advanced view is on, which adds the usage figures. */
  advanced: boolean;
}

/** The figures, the search, and a table per kind of block, as the server found and counted them. */
function Overview({ range, advanced }: OverviewProps) {
  const [params, setParams] = useSearchParams();
  const search = params.get("search") ?? "";
  const { status } = useShell();
  const { data, error } = useMetricsOverview(range, search, status.environment);

  function find(words: string) {
    const next = new URLSearchParams(params);

    if (words) {
      next.set("search", words);
    } else {
      next.delete("search");
    }

    setParams(next, { replace: true });
  }

  return (
    <>
      {error && (
        <p className="cn-error" role="alert">
          {data ? `Could not refresh: ${error}.` : `Metrics could not be read: ${error}`}
        </p>
      )}
      {data && <OverviewFigures overview={data} advanced={advanced} />}
      <div className="mx-find">
        <input
          type="search"
          className="cn-find"
          aria-label="Find a host or service"
          placeholder="Find a host or service"
          value={search}
          onChange={(event) => find(event.target.value)}
        />
      </div>
      {!data ? (
        !error && <p className="cn-empty">Reading the metrics…</p>
      ) : !data.summary.anySent ? (
        <p className="cn-empty">
          No metrics yet. Point the OpenTelemetry SDK of a service, or a Collector with the hostmetrics receiver, at
          Veyra; hosts and services show here within a minute.
        </p>
      ) : (
        <OverviewTables overview={data} search={search} />
      )}
    </>
  );
}

/** The strip of the overview's figures, as the server counted them. */
function OverviewFigures({ overview, advanced }: { overview: MetricsOverview; advanced: boolean }) {
  const { summary, usage } = overview;

  return (
    <section className={advanced ? "sq-figures mx-figures-wide" : "sq-figures mx-figures"} aria-label="Figures">
      <SqlFigure
        label="Hosts"
        value={summary.hosts}
        detail={summary.fillingDisks === 0 ? "disks steady" : `${summary.fillingDisks} disks filling`}
        tone={summary.fillingDisks > 0 ? "warn" : undefined}
        help="Hosts sending metrics from the Collector's hostmetrics receiver, and how many of them have a disk that is full within 30 days at the pace of its last day."
      />
      <SqlFigure
        label="Services"
        value={summary.services}
        detail="with metrics"
        help="Services sending HTTP server or runtime metrics from their OpenTelemetry SDK."
      />
      <SqlFigure
        label="Databases"
        value={summary.databases}
        detail={summary.databases === 1 ? "server" : "servers"}
        help="Database servers a Collector or a Veyra agent reads, such as SQL Server and PostgreSQL."
      />
      <SqlFigure
        label="Containers"
        value={summary.containers}
        detail={summary.restarting === 0 ? "none restarting" : `${summary.restarting} restarting`}
        tone={summary.restarting > 0 ? "warn" : undefined}
        help="Docker containers from the Collector's docker_stats receiver, and how many restarted in the range."
      />
      {advanced && (
        <>
          <SqlFigure
            label="Series"
            value={usage.series.toLocaleString("en")}
            detail={`of ${usage.maxSeries.toLocaleString("en")}`}
            help="Each metric name with one set of attributes is one series. Points of new series beyond the limit are dropped, so a count near it needs a look, often at an attribute with too many values."
          />
          <SqlFigure
            label="Points"
            value={formatRate(usage.pointsPerSecond)}
            detail="per second"
            help="Metric points received per second over the last 5 minutes, from every sender. A sudden fall can mean a sender stopped."
          />
          <SqlFigure
            label="Storage"
            value={formatAmount(usage.usedBytes)}
            detail={`${usage.retentionDays} days kept`}
            help="The disk space metrics take in PostgreSQL, series and points with their indexes. Points are dropped after the days kept."
          />
        </>
      )}
    </section>
  );
}

/** One table per kind of block, in the order the server sent them. */
function OverviewTables({ overview, search }: { overview: MetricsOverview; search: string }) {
  const { hosts, services, databases, containers } = overview;

  if (hosts.length + services.length + databases.length + containers.length === 0) {
    return <p className="cn-empty">No host or service matches “{search}”.</p>;
  }

  return (
    <div className="mx-tables">
      {hosts.length > 0 && (
        <Table
          id="mx-hosts"
          title="Hosts"
          help="Each host: CPU and memory over the last 5 minutes, its fullest disk, and when a disk is full at the pace of its last day. Open one for its charts."
          heads={["Host", "CPU", "Memory", "Fullest disk", "Disks"]}
        >
          {hosts.map((host) => (
            <tr key={host.name}>
              <td>
                <Link className="cn-table-name" to={`/metrics/hosts/${encodeURIComponent(host.name)}`}>
                  {host.name}
                </Link>
                <span className="cn-table-sub">{host.os ?? host.environment ?? "—"}</span>
              </td>
              <td className="num">{formatShare(host.cpuRatio)}</td>
              <td className="num">{formatShare(host.memoryRatio)}</td>
              <td className="num">
                {host.fullestDisk ? `${host.fullestDisk.mountpoint} ${formatShare(host.fullestDisk.ratio)}` : "—"}
              </td>
              <td className={host.fillingDisk ? "num cn-warn" : "num sq-muted"}>
                {formatFull(host.fillingDisk?.daysUntilFull ?? null)}
              </td>
            </tr>
          ))}
        </Table>
      )}
      {services.length > 0 && (
        <Table
          id="mx-services"
          title="Services"
          help="Each service: HTTP requests per second, the 95th percentile of their duration, and the share answered with a 5xx status, over the last 5 minutes, from http.server.request.duration."
          heads={["Service", "Requests/s", "p95", "5xx", "Instances"]}
        >
          {services.map((service) => (
            <tr key={`${service.name}-${service.environment ?? ""}`}>
              <td>
                <Link className="cn-table-name" to={`/metrics/services/${encodeURIComponent(service.name)}`}>
                  {service.name}
                </Link>
                <span className="cn-table-sub">{service.runtime ?? service.environment ?? "—"}</span>
              </td>
              <td className="num">{formatRate(service.requestsPerSecond)}</td>
              <td className="num">{formatSeconds(service.p95Seconds)}</td>
              <td className={(service.serverErrorRatio ?? 0) > 0.01 ? "num cn-bad" : "num"}>
                {formatShare(service.serverErrorRatio)}
              </td>
              <td className="num">{service.instances}</td>
            </tr>
          ))}
        </Table>
      )}
      {databases.length > 0 && (
        <Table
          id="mx-databases"
          title="Database servers"
          help="Each database server: requests per second, connections, sessions blocked now, and failed SQL Server Agent jobs. Open one for its own screen."
          heads={["Server", "Requests/s", "Connections", "Blocked", "Failed jobs"]}
        >
          {databases.map((database) => (
            <tr key={`${database.engine}-${database.server}`}>
              <td>
                <Link className="cn-table-name" to={databasePath(`${database.engine} ${database.server}`)}>
                  {database.server}
                </Link>
                <span className="cn-table-sub">{engineName(database.engine)}</span>
              </td>
              <td className="num">{formatRate(database.batchRequests ?? database.commitsPerSecond)}</td>
              <td className="num">{database.connections ?? "—"}</td>
              <td className={(database.blocked ?? 0) > 0 ? "num cn-bad" : "num"}>{database.blocked ?? "—"}</td>
              <td className={database.failedJobs > 0 ? "num cn-bad" : "num"}>{database.failedJobs}</td>
            </tr>
          ))}
        </Table>
      )}
      {containers.length > 0 && (
        <Table
          id="mx-containers"
          title="Containers"
          help="Each container: CPU over the last 5 minutes as a share of one CPU, memory of its limit, and restarts in the range."
          heads={["Container", "CPU", "Memory", "Restarts"]}
        >
          {containers.map((container) => (
            <tr key={`${container.host ?? ""}-${container.name}`}>
              <td>
                {container.host ? (
                  <Link className="cn-table-name" to={`/metrics/hosts/${encodeURIComponent(container.host)}`}>
                    {container.name}
                  </Link>
                ) : (
                  container.name
                )}
                <span className="cn-table-sub">{container.host ?? container.image ?? "—"}</span>
              </td>
              <td className="num">{formatCpuPercent(container.cpuPercent)}</td>
              <td className="num">{formatShare(container.memoryRatio)}</td>
              <td className={(container.restarts ?? 0) > 1 ? "num cn-warn" : "num"}>{container.restarts ?? "—"}</td>
            </tr>
          ))}
        </Table>
      )}
    </div>
  );
}

/** Props for {@link Table}. */
interface TableProps {
  /** The heading's element ID. */
  id: string;
  /** The panel's name. */
  title: string;
  /** What it shows, for its question mark. */
  help: string;
  /** The column heads; every one after the first is a number. */
  heads: string[];
  /** The rows. */
  children: ReactNode;
}

/** A panel with a table of blocks, its first column the block and the rest its numbers. */
function Table({ id, title, help, heads, children }: TableProps) {
  return (
    <section className="cn-card" aria-labelledby={id}>
      <header>
        <h2 id={id}>{title}</h2>
        <HelpTip label={title}>{help}</HelpTip>
      </header>
      <div className="cn-table-scroll">
        <table className="cn-table">
          <thead>
            <tr>
              {heads.map((head, index) => (
                <th key={head} scope="col" className={index === 0 ? undefined : "num"}>
                  {head}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>{children}</tbody>
        </table>
      </div>
    </section>
  );
}

/** The metric names beside the chosen metric's chart and its lines' figures. */
function Explorer({ range }: { range: MetricRange }) {
  const explorer = useMetricExplorer(range);
  const { metric, names, chart } = explorer;

  return (
    <div className="mx-explorer">
      <section className="cn-card mx-names" aria-labelledby="mx-names">
        <header>
          <h2 id="mx-names">Metrics</h2>
          <HelpTip label="Metrics">
            Every metric name Veyra received in the range. The server finds the names that hold the words and ranks
            them, closest first. Pick one to chart it.
          </HelpTip>
        </header>
        <div className="sq-pad mx-names-find">
          <input
            type="search"
            className="cn-find"
            aria-label="Find a metric"
            placeholder="Find a metric, for example http"
            value={explorer.search}
            onChange={(event) => explorer.update({ q: event.target.value })}
          />
        </div>
        <ul className="mx-name-list" aria-label="Metric names">
          {(names.data ?? []).map((name) => (
            <li key={name.name}>
              <button
                type="button"
                aria-pressed={name.name === metric}
                onClick={() =>
                  explorer.update({ metric: name.name, show: null, split: null, service: null, host: null })
                }
              >
                <b>{name.name}</b>
                <small>{[name.kind, name.unit || null, `${name.series} series`].filter(Boolean).join(" · ")}</small>
              </button>
            </li>
          ))}
        </ul>
        {names.data && names.data.length === 0 && <p className="cn-empty">No metric name holds “{explorer.search}”.</p>}
        <footer>
          <span>Found and ranked by the server</span>
        </footer>
      </section>

      <div className="mx-chart-side">
        {!metric ? (
          <p className="cn-empty mx-pick">Pick a metric to chart it.</p>
        ) : chart.data ? (
          <ChartPanels chart={chart.data} explorer={explorer} />
        ) : chart.notFound ? (
          <p className="cn-empty mx-pick">No series of {metric} in the range.</p>
        ) : (
          <p className="cn-empty mx-pick">
            {chart.error ? `Could not chart ${metric}: ${chart.error}` : `Charting ${metric}…`}
          </p>
        )}
      </div>
    </div>
  );
}

/** Props for {@link ChartPanels}. */
interface ChartPanelsProps {
  /** The chart the server drew. */
  chart: MetricChart;
  /** The explorer's choices and how to change them. */
  explorer: MetricExplorer;
}

/** The choices of the chart, the chart with its legend, and the table of its lines with their figures. */
function ChartPanels({ chart, explorer }: ChartPanelsProps) {
  const format = unitFormat(chart.unit, chart.show);
  const sources = explorer.selected?.sources ?? [];
  const bucket =
    chart.bucketSeconds >= 60 ? `${Math.round(chart.bucketSeconds / 60)}-minute` : `${chart.bucketSeconds}-second`;

  return (
    <>
      <div className="tr-filters mx-choices" role="group" aria-label="Chart">
        <div className="cn-seg" role="group" aria-label="Show">
          {metricShows[chart.kind].map((option) => (
            <button
              key={option}
              type="button"
              aria-pressed={option === chart.show}
              onClick={() => explorer.update({ show: option })}
            >
              {option === "value" ? "Value" : option === "rate" ? "Rate" : option}
            </button>
          ))}
        </div>
        <label className={chart.split ? "cn-filter on" : "cn-filter"}>
          <span>Split by:</span>
          <select
            aria-label="Split by"
            value={chart.split ?? ""}
            onChange={(event) => explorer.update({ split: event.target.value || null })}
          >
            <option value="">Nothing</option>
            {chart.splitKeys.map((key) => (
              <option key={key} value={key}>
                {key}
              </option>
            ))}
          </select>
        </label>
        <ChoiceBox
          label="Service"
          value={explorer.service}
          options={sources}
          onChange={(service) => explorer.update({ service })}
        />
        <ChoiceBox
          label="Host"
          value={explorer.host}
          options={sources}
          onChange={(host) => explorer.update({ host })}
        />
      </div>

      <section className="cn-card" aria-labelledby="mx-chart">
        <header>
          <h2 id="mx-chart">
            {chart.name} · {chart.show}
          </h2>
          <HelpTip label={`${chart.name} chart`}>
            {chart.split === null ? "One line for all its series" : `One line per ${chart.split}, at most eight`}, as
            the server works it out in each step of the range
            {explorer.service ? `, for the service ${explorer.service}` : ""}
            {explorer.host ? `, on the host ${explorer.host}` : ""}. {explorer.selected?.description ?? ""}
          </HelpTip>
          <span className="cn-meta">
            {chart.kind} · {chart.unit || "no unit"} · {bucket} buckets
          </span>
        </header>
        <div className="sq-chart">
          <LineChart
            title={`${chart.name} ${chart.show}`}
            height={260}
            format={format}
            series={chart.lines.map((line, index) => ({ line, color: lineColors[index % lineColors.length] ?? "" }))}
          />
        </div>
      </section>

      <section className="cn-card" aria-labelledby="mx-series">
        <header>
          <h2 id="mx-series">Series</h2>
          <HelpTip label="Series">
            Each line of the chart with its latest value, and its average and highest over the range. The server works
            them out from the same steps the chart draws.
          </HelpTip>
          <span className="cn-meta">in the order the server sent them</span>
        </header>
        <div className="cn-table-scroll">
          <table className="cn-table" aria-label="Series">
            <thead>
              <tr>
                <th scope="col">{chart.split ?? "Series"}</th>
                <th scope="col" className="num">
                  Now
                </th>
                <th scope="col" className="num">
                  Average
                </th>
                <th scope="col" className="num">
                  Highest
                </th>
              </tr>
            </thead>
            <tbody>
              {chart.lines.map((line, index) => (
                <tr key={line.label}>
                  <th scope="row" className="mx-line">
                    <span>
                      <i style={{ background: lineColors[index % lineColors.length] }} aria-hidden="true" />
                      {line.label}
                    </span>
                  </th>
                  <td className="num">{line.latest === null ? "—" : format(line.latest)}</td>
                  <td className="num">{line.average === null ? "—" : format(line.average)}</td>
                  <td className="num">{line.highest === null ? "—" : format(line.highest)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </>
  );
}

/** Props for {@link ChoiceBox}. */
interface ChoiceBoxProps {
  /** What it chooses, such as "Service". */
  label: string;
  /** The choice now, or null for all. */
  value: string | null;
  /** Names to suggest, as the server sent them. */
  options: string[];
  /** Called with the new choice, or null for all, once the person presses Enter or leaves the box. */
  onChange: (value: string | null) => void;
}

/** A chip with a box for one name, which the chart is then read for; empty means all. */
function ChoiceBox({ label, value, options, onChange }: ChoiceBoxProps) {
  const [draft, setDraft] = useState(value ?? "");
  const list = `mx-${label.toLowerCase()}-options`;

  function commit() {
    const next = draft.trim() || null;

    if (next !== value) {
      onChange(next);
    }
  }

  return (
    <label className={value ? "cn-filter on mx-choice" : "cn-filter mx-choice"}>
      <span>{label}:</span>
      <input
        aria-label={label}
        list={list}
        placeholder="All"
        value={draft}
        onChange={(event) => setDraft(event.target.value)}
        onBlur={commit}
        onKeyDown={(event) => {
          if (event.key === "Enter") {
            commit();
          }
        }}
      />
      <datalist id={list}>
        {options.map((option) => (
          <option key={option} value={option} />
        ))}
      </datalist>
      {value && (
        <button
          type="button"
          aria-label={`Every ${label.toLowerCase()}`}
          onClick={() => {
            setDraft("");
            onChange(null);
          }}
        >
          <ConsoleIcon name="close" size={12} />
        </button>
      )}
    </label>
  );
}
