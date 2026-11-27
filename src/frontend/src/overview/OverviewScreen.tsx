import type { ReactNode, RefObject } from "react";
import { Link, useSearchParams } from "react-router";

import { alertFixPath } from "../alerts/rulePath";
import { formatMeasure, ruleSentence, ruleSummary, subjectLabel } from "../alerts/ruleWords";
import { api } from "../api/client";
import type { AgentState, HomeAlert, HostRow, Overview, ServiceRow } from "../api/types";
import { useShell } from "../app/shellContext";
import { carryTime } from "../time/carryTime";
import { formatAge, formatBytes, formatRatio } from "../home/homeFormat";
import { LineChart } from "../metrics/LineChart";
import { formatSeconds, formatSpan } from "../metrics/metricFormat";
import { useMetricData } from "../metrics/useMetricData";
import { changeTone, changeWords, seenWords, shortCount } from "../resources/resourceFormat";
import { HealthPill } from "../resources/ResourceScreen";
import { MiniLine } from "../resources/MiniLine";
import { useResourcePages } from "../resources/useResourcePages";
import { agentStateWords } from "../settings/monitoringFormat";
import { HelpTip } from "../ui/HelpTip";
import { useViewMode } from "../ui/ViewToggle";
import { ScreenBar } from "../ui/ScreenBar";
import { OverviewPanel } from "./OverviewPanel";
import { StackedBars } from "./StackedBars";
import { useFitRows } from "./useFitRows";
import "./overview.css";

/** The window outside a shell, as in a component test: the last 24 hours. */
function outsideWindow(): { from: string; to: string } {
  return { from: new Date(Date.now() - 86_400_000).toISOString(), to: new Date().toISOString() };
}

/**
 * The fewest rows each list asks for, what fits a laptop; a list in a taller tile asks for as many as fit (VEY-81).
 * The full lists are a click away.
 */
const least = { services: 8, hosts: 8, databases: 12, pipelines: 8, alerts: 5 };

/**
 * The Overview (VEY-69, reference screenshot 5; ADR 0023): the whole environment on one screen. The Simple view leaves
 * out the panels about Veyra's own plumbing, telemetry arriving and the collectors; Advanced shows them (VEY-83). Figures with their
 * change against the period before, what needs attention, request activity, telemetry arriving, the applications,
 * infrastructure, and databases worst first, the jobs, the hosts' resources, and the collectors. The server works out
 * every figure, order, and filter; the screen lays them out.
 */
export function OverviewScreen() {
  const { status, time } = useShell();
  const [mode] = useViewMode();
  const environment = status.environment;
  const key = JSON.stringify({ choice: time?.choice ?? null, environment });
  const windowNow = () => (time ? time.window() : outsideWindow());
  const fit = {
    services: useFitRows("tbody > tr", least.services),
    hosts: useFitRows("tbody > tr", least.hosts),
    databases: useFitRows("tbody > tr", least.databases),
    pipelines: useFitRows("tbody > tr", least.pipelines),
    alerts: useFitRows(".ov-needs > li", least.alerts),
  };
  const rows = { databases: fit.databases.rows, pipelines: fit.pipelines.rows };
  const { data, error } = useMetricData(
    `overview-${key}`,
    (signal) => api.overview(windowNow(), environment, signal, rows),
    { choices: JSON.stringify(rows) },
  );
  const services = useResourcePages(
    `overview-services-${key}`,
    (_cursor, signal) =>
      api.resourceServices(
        { ...windowNow(), environment, filters: { limit: String(fit.services.rows) }, cursor: null },
        signal,
      ),
    String(fit.services.rows),
  );
  const hosts = useResourcePages(
    `overview-hosts-${key}`,
    (_cursor, signal) =>
      api.resourceHosts(
        { ...windowNow(), environment, filters: { limit: String(fit.hosts.rows) }, cursor: null },
        signal,
      ),
    String(fit.hosts.rows),
  );
  const alerts = (status.snapshot?.alerts ?? []).filter((alert) => alert.n !== null);
  const title = environment ? `${environment[0]?.toUpperCase() ?? ""}${environment.slice(1)} overview` : "Overview";

  return (
    <main className="ov" data-view={mode}>
      <ScreenBar trail={[{ label: "Overview" }]} view />
      <header className="ov-head">
        <div>
          <h1>
            {title}
            {alerts.length > 0 && <AlertsPill alerts={alerts} />}
          </h1>
          <p>Every service, host, container, and database in this environment, worst first.</p>
        </div>
      </header>

      {error && (
        <p className="ov-error" role="alert">
          {error}
        </p>
      )}

      <Figures overview={data} />

      <div className="ov-row ov-row-wide">
        <NeedsAttention alerts={alerts} shownAtMost={fit.alerts.rows} bodyRef={fit.alerts.box} />
        <OverviewPanel
          title="Request activity"
          to="/traces"
          whole
          help="HTTP requests and server errors (5xx) across every service that sends HTTP metrics, in each step of the time range."
          meta={data ? `${shortCount(data.figures.requests.count)} requests` : undefined}
          flush
        >
          {data && <ActivityChart overview={data} />}
        </OverviewPanel>
        <OverviewPanel
          title="Telemetry ingestion"
          className="advanced-only"
          to="/logs"
          whole
          help="Log records, spans, and metric points that arrived in each step, counted when they were written."
          flush
        >
          {data && <IngestionBars overview={data} />}
        </OverviewPanel>
      </div>

      <div className="ov-row ov-row-tables">
        <OverviewPanel
          title="Applications"
          to="/resources/services"
          help="Services worst first: warnings, cautions, silent ones, then the rest, with their requests, p95, and errors."
          meta={`${services.total} services`}
          foot={<Link to="/resources/services">All services →</Link>}
          flush
          bodyRef={fit.services.box}
        >
          <ServicesTable rows={services.items} />
        </OverviewPanel>
        <OverviewPanel
          title="Infrastructure"
          to="/resources/hosts"
          help="Hosts and containers worst first, with CPU, memory, the fullest disk, and what runs on them."
          meta={data ? `${data.figures.hosts.online} of ${data.figures.hosts.known} online` : undefined}
          foot={<Link to="/resources/hosts">All hosts and containers →</Link>}
          flush
          bodyRef={fit.hosts.box}
        >
          <HostsTable rows={hosts.items} />
        </OverviewPanel>
        <OverviewPanel
          title="SQL Server databases"
          to="/resources/databases"
          anchor="databases"
          settled={data !== null && !services.loading && !hosts.loading}
          help="Every database of every server, worst first: a late backup or a database that is not online first."
          meta={data ? `${data.databases.total} databases` : undefined}
          foot={<Link to="/resources/databases">All database servers →</Link>}
          flush
          bodyRef={fit.databases.box}
        >
          {data && <DatabasesTable overview={data} />}
        </OverviewPanel>
      </div>

      <div className="ov-row ov-row-wide ov-row-tables">
        <OverviewPanel
          title="Pipeline executions"
          to="/jobs"
          help="The latest run of each job, failed ones first: SQL Server Agent jobs, and jobs recognised from traces. Open one for its runs and their steps."
          meta={data ? `${data.figures.jobs.runs} runs` : undefined}
          foot={<Link to="/jobs">All jobs and pipelines →</Link>}
          flush
          bodyRef={fit.pipelines.box}
        >
          {data && <PipelinesTable overview={data} />}
        </OverviewPanel>
        <OverviewPanel
          title="Host resource trends"
          to="/resources/hosts"
          whole
          help="The average CPU and memory across hosts, and the fullest disk of any host, in each step of the time range."
          flush
        >
          {data && <HostTrendsChart overview={data} />}
        </OverviewPanel>
        <OverviewPanel
          title="Collector health"
          className="advanced-only"
          to="/settings?section=Agents"
          help="The agents that read your systems for Veyra: their state and when they last reported. Only Admins see them."
          meta={data?.collectors ? `${data.collectors.length} agents` : undefined}
          flush
        >
          {data && <CollectorsTable overview={data} />}
        </OverviewPanel>
      </div>
    </main>
  );
}

/** The state beside the name: open warnings and cautions. */
function AlertsPill({ alerts }: { alerts: HomeAlert[] }) {
  const warnings = alerts.filter((alert) => alert.severity === "Warning").length;
  const cautions = alerts.length - warnings;
  const words = [
    warnings > 0 ? `${warnings} ${warnings === 1 ? "warning" : "warnings"}` : "",
    cautions > 0 ? `${cautions} ${cautions === 1 ? "caution" : "cautions"}` : "",
  ]
    .filter(Boolean)
    .join(" · ");

  return <span className={warnings > 0 ? "ov-pill bad" : "ov-pill warn"}>{words}</span>;
}

/** Props for one figure tile. */
interface FigureProps {
  label: string;
  /** The screen with the details, which the whole tile opens in the same range (VEY-82). */
  to: string;
  value: ReactNode;
  detail: ReactNode;
  help: string;
  tone?: "warn" | "bad";
  line?: (number | null)[];
}

/**
 * One figure tile: its name with its question mark, the figure, a line under it, and its trend when known. The whole
 * tile opens the screen behind the figure; its name is the link, so a screen reader hears where it leads.
 */
function Figure({ label, to, value, detail, help, tone, line }: FigureProps) {
  const [params] = useSearchParams();

  return (
    <div className={["ov-figure", tone].filter(Boolean).join(" ")}>
      <div className="ov-figure-name">
        <Link className="ov-figure-link" to={carryTime(to, params)}>
          {label}
        </Link>
        <HelpTip label={label}>{help}</HelpTip>
      </div>
      <div className="ov-figure-value">{value}</div>
      <div className="ov-figure-detail">{detail}</div>
      {line && <MiniLine values={line} tone={tone === "bad" ? "bad" : "ok"} className="ov-figure-line" />}
    </div>
  );
}

/**
 * Availability as a percentage with two decimals near the top, where the difference between 99.95 % and 100 % is the
 * whole point of the figure.
 *
 * @param ratio - The share, from 0 to 1, or null.
 * @returns The words, or a dash.
 */
export function availabilityWords(ratio: number | null): string {
  if (ratio === null) {
    return "—";
  }

  const percent = ratio * 100;

  return `${percent >= 99 && percent < 100 ? (Math.floor(percent * 100) / 100).toFixed(2) : formatRatio(ratio).replace("%", "")}%`;
}

/**
 * A change in a tile's few words, such as "↑ 12% vs before".
 *
 * @param change - The change, as the server works it out, or null.
 * @param points - Whether it is a change of a ratio, in percentage points.
 * @returns The words.
 */
function shortChange(change: number | null, points = false): string {
  return change === null
    ? "nothing to compare"
    : changeWords(change, points).replace(" vs the period before", " vs before");
}

/** A share as a percentage, amber from 70 % and red from 85 %, as the tables of screenshot 5 show it. */
function Percent({ ratio }: { ratio: number | null }) {
  if (ratio === null) {
    return <span className="ov-none">—</span>;
  }

  const percent = Math.round(ratio * 100);

  return <span className={percent >= 85 ? "ov-bad" : percent >= 70 ? "ov-warn" : undefined}>{percent}%</span>;
}

/** The figure strip of screenshot 5. */
function Figures({ overview }: { overview: Overview | null }) {
  if (!overview) {
    return <section className="ov-figures" aria-label="Figures" aria-busy="true" />;
  }

  const { services, hosts, databases, requests, errorRatio, openAlerts, jobs, availability } = overview.figures;
  const alertsOpen = openAlerts.warning + openAlerts.caution;

  return (
    <section className="ov-figures" aria-label="Figures">
      <Figure
        label="Services healthy"
        to="/resources/services"
        value={
          <>
            {services.healthy} <small>/ {services.total}</small>
          </>
        }
        detail={`${services.warning} warning · ${services.caution} caution · ${services.silent} silent`}
        tone={services.warning > 0 ? "bad" : services.caution > 0 ? "warn" : undefined}
        help="Services without an open alert, of every service Veyra knows. Silent services sent nothing lately."
      />
      <Figure
        label="Hosts online"
        to="/resources/hosts"
        value={
          <>
            {hosts.online} <small>/ {hosts.known}</small>
          </>
        }
        detail={hosts.known > hosts.online ? `${hosts.known - hosts.online} silent` : "all reporting"}
        tone={hosts.known > hosts.online ? "warn" : undefined}
        help="Hosts and containers that reported lately, of every one Veyra has seen while its metrics are kept."
      />
      <Figure
        label="Database instances"
        to="/resources/databases"
        value={databases.servers}
        detail={`${databases.databases} databases`}
        help="Database servers Veyra watches, and their databases without the system ones."
      />
      <Figure
        label="Request volume"
        to="/traces"
        line={overview.activity.points.map((point) => point.requests)}
        value={shortCount(requests.count)}
        detail={<span className={`ov-${changeTone(requests.change, false)}`}>{shortChange(requests.change)}</span>}
        help="HTTP requests the services answered in the time range, against the period of the same length before it."
      />
      <Figure
        label="Error rate"
        to="/traces?show=failed"
        line={overview.activity.points.map((point) =>
          point.requests && point.errors !== null ? point.errors / point.requests : null,
        )}
        value={formatRatio(errorRatio.value)}
        detail={
          <span className={`ov-${changeTone(errorRatio.change, true)}`}>{shortChange(errorRatio.change, true)}</span>
        }
        tone={changeTone(errorRatio.change, true) === "bad" ? "bad" : undefined}
        help="The share of requests answered with a server error (5xx), and its change in percentage points."
      />
      <Figure
        label="Open alerts"
        to="/alerts"
        value={alertsOpen}
        detail={`${openAlerts.warning} warning · ${openAlerts.caution} caution`}
        tone={openAlerts.warning > 0 ? "bad" : openAlerts.caution > 0 ? "warn" : undefined}
        help="Alerts open now, by severity. They become incidents when incidents arrive (VEY-69, step 5)."
      />
      <Figure
        label="Jobs succeeded"
        to={jobs.failed > 0 ? "/jobs?status=failing" : "/jobs"}
        value={formatRatio(jobs.succeededRatio)}
        detail={jobs.runs === 0 ? "no runs in the range" : `${jobs.failed} of ${jobs.runs} runs failed`}
        tone={jobs.failed > 0 ? "warn" : undefined}
        help="Job runs in the time range that succeeded, of all finished runs: SQL Server Agent jobs, and jobs recognised from traces."
      />
      <Figure
        label="Availability"
        to="/alerts"
        value={availabilityWords(availability.ratio)}
        detail={`${availability.days} days · ${shortChange(availability.change, true)}`}
        help={`The share of the last ${availability.days} days in which no service had an open warning, from the alert history. Veyra does not probe services, so this is not an uptime check.`}
      />
    </section>
  );
}

/**
 * What needs attention: the open alerts, worst first, as the shell counts them; the first one with its sentence. It
 * shows as many as fit in its tile, which its row may make taller (VEY-81).
 */
function NeedsAttention({
  alerts,
  shownAtMost,
  bodyRef,
}: {
  alerts: HomeAlert[];
  shownAtMost: number;
  bodyRef: RefObject<HTMLDivElement | null>;
}) {
  const now = new Date();
  const shown = alerts.slice(0, shownAtMost);
  const first = shown[0];

  return (
    <OverviewPanel
      title="Needs attention"
      to="/alerts"
      help="The open alerts, warnings first, numbered as the alert bell counts them. Open one for what it means and how to fix it."
      meta={`${alerts.length} open`}
      foot={<Link to="/alerts">All alerts →</Link>}
      flush
      bodyRef={bodyRef}
    >
      {shown.length === 0 ? (
        <p className="ov-empty">Nothing needs attention.</p>
      ) : (
        <ol className="ov-needs">
          {shown.map((alert) => (
            <li key={alert.id} className={alert.severity === "Warning" ? "bad" : "warn"}>
              <span className="ov-n">{alert.n}</span>
              <Link to={`/alerts/${alert.id}`} className="ov-needs-what">
                <b>{subjectLabel(alert)}</b>
                <small>{ruleSummary(alert.rule, alert.threshold, alert.windowSeconds)}</small>
              </Link>
              <span className="ov-needs-value">
                <b>{formatMeasure(alert.rule, alert.value)}</b>
                <small>{formatAge(alert.since, now)}</small>
              </span>
              {alert === first && (
                <p className="ov-needs-why">
                  {ruleSentence(alert.rule, alert.severity, alert.threshold, alert.windowSeconds)}{" "}
                  <Link to={alertFixPath(alert.id)}>What it means and how to fix it →</Link>
                </p>
              )}
            </li>
          ))}
        </ol>
      )}
    </OverviewPanel>
  );
}

/** A series of the Overview as a chart line. */
function lineOf<T extends { at: string }>(label: string, points: T[], value: (point: T) => number | null) {
  const values = points.map((point) => ({ at: point.at, value: value(point) }));
  const known = values.filter((point) => point.value !== null);

  return { label, latest: known.at(-1)?.value ?? null, points: values };
}

/** Requests and errors across services. */
function ActivityChart({ overview }: { overview: Overview }) {
  const points = overview.activity.points;

  return (
    <div className="ov-chart">
      <LineChart
        title="Request activity"
        format={(value) => shortCount(value)}
        height={210}
        series={[
          { line: lineOf("Requests", points, (point) => point.requests), color: "var(--accent)", filled: true },
          { line: lineOf("Server errors", points, (point) => point.errors), color: "var(--error-fg)" },
        ]}
      />
    </div>
  );
}

/** Telemetry arriving, by signal. */
function IngestionBars({ overview }: { overview: Overview }) {
  const points = overview.ingestion.points;
  const clock = (at: string | undefined) => (at ? at.slice(11, 16) : "");

  return (
    <StackedBars
      title="Telemetry ingestion"
      steps={points.map((point) => clock(point.at))}
      labels={[clock(points[0]?.at), clock(points[Math.floor(points.length / 2)]?.at), "now"]}
      layers={[
        { name: "Logs", color: "var(--accent)", values: points.map((point) => point.logs) },
        { name: "Spans", color: "var(--metric-violet)", values: points.map((point) => point.spans) },
        { name: "Metric points", color: "var(--info-fg)", values: points.map((point) => point.points) },
      ]}
    />
  );
}

/** The hosts' resources. */
function HostTrendsChart({ overview }: { overview: Overview }) {
  const points = overview.hostTrends.points;

  return (
    <div className="ov-chart">
      <LineChart
        title="Host resource trends"
        format={(value) => formatRatio(value)}
        max={1}
        height={190}
        series={[
          { line: lineOf("CPU, average", points, (point) => point.cpu), color: "var(--accent)" },
          { line: lineOf("Memory, average", points, (point) => point.memory), color: "var(--info-fg)" },
          { line: lineOf("Fullest disk", points, (point) => point.disk), color: "var(--warning-fg)" },
        ]}
      />
    </div>
  );
}

/** A small table with headings. */
function Table({ heads, children }: { heads: { title: string; numeric?: boolean }[]; children: ReactNode }) {
  return (
    <div className="ov-scroll">
      <table className="ov-table">
        <thead>
          <tr>
            {heads.map((head) => (
              <th key={head.title} scope="col" className={head.numeric ? "num" : undefined}>
                {head.title}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>{children}</tbody>
      </table>
    </div>
  );
}

/** The applications, worst first. */
function ServicesTable({ rows }: { rows: ServiceRow[] }) {
  return (
    <Table
      heads={[
        { title: "Service" },
        { title: "Status" },
        { title: "Req / min", numeric: true },
        { title: "p95", numeric: true },
        { title: "Errors", numeric: true },
      ]}
    >
      {rows.map((row) => (
        <tr key={`${row.environment ?? ""}/${row.name}`}>
          <td>
            <Link className="ov-name" to={`/metrics/services/${encodeURIComponent(row.name)}`}>
              {row.name}
            </Link>
          </td>
          <td>
            <HealthPill health={row.health} silent={row.silent} />
          </td>
          <td className="num">{shortCount(row.requestsPerMinute)}</td>
          <td className="num">{formatSeconds(row.p95Seconds)}</td>
          <td className={row.health === "Warning" ? "num ov-bad" : "num"}>{formatRatio(row.errorRatio)}</td>
        </tr>
      ))}
    </Table>
  );
}

/** The infrastructure, worst first. */
function HostsTable({ rows }: { rows: HostRow[] }) {
  return (
    <Table
      heads={[
        { title: "Host" },
        { title: "Status" },
        { title: "CPU", numeric: true },
        { title: "RAM", numeric: true },
        { title: "Disk", numeric: true },
        { title: "Apps", numeric: true },
      ]}
    >
      {rows.map((row) => (
        <tr key={`${row.kind}/${row.name}`}>
          <td>
            <Link
              className="ov-name"
              to={
                row.kind === "host"
                  ? `/metrics/hosts/${encodeURIComponent(row.name)}`
                  : `/metrics?search=${encodeURIComponent(row.name)}`
              }
            >
              {row.name}
            </Link>
          </td>
          <td>
            <HealthPill health={row.health} silent={row.silent} />
          </td>
          <td className="num">
            <Percent ratio={row.cpuRatio} />
          </td>
          <td className="num">
            <Percent ratio={row.memoryRatio} />
          </td>
          <td className="num">
            <Percent ratio={row.fullestDiskRatio} />
          </td>
          <td className="num">{row.runsCount}</td>
        </tr>
      ))}
    </Table>
  );
}

/** Every server's databases, worst first. */
function DatabasesTable({ overview }: { overview: Overview }) {
  // Connections only where an engine reports them per database, so the column is left out when none does.
  const connections = overview.databases.items.some((row) => row.connections !== null);

  return (
    <Table
      heads={[
        { title: "Database" },
        { title: "Status" },
        { title: "Size", numeric: true },
        { title: "Growth / d", numeric: true },
        { title: "Full backup", numeric: true },
        ...(connections ? [{ title: "Conn.", numeric: true }] : []),
      ]}
    >
      {overview.databases.items.map((row) => (
        <tr key={`${row.server}/${row.name}`}>
          <td>
            <Link
              className="ov-name"
              to={`/metrics/databases/${encodeURIComponent(row.engine)}/${encodeURIComponent(row.server)}?tab=databases`}
              title={`${row.name} on ${row.server}`}
            >
              {row.name}
            </Link>
          </td>
          <td>
            <span
              className={
                row.late ? "ov-pill warn" : row.state === "Online" || row.state === null ? "ov-pill ok" : "ov-pill bad"
              }
              title={row.state === null ? "The engine reports no state for its databases" : undefined}
            >
              {row.late ? "Backup late" : row.state === "Online" ? "Healthy" : (row.state ?? "Reporting")}
            </span>
          </td>
          <td className="num">{row.sizeBytes === null ? "—" : formatBytes(row.sizeBytes)}</td>
          <td className="num">{row.growthBytesPerDay === null ? "—" : formatBytes(row.growthBytesPerDay)}</td>
          <td className={row.late ? "num ov-bad" : "num"}>
            {row.lastFullBackupAgeSeconds === null ? "—" : formatSpan(row.lastFullBackupAgeSeconds)}
          </td>
          {connections && <td className="num">{row.connections ?? "—"}</td>}
        </tr>
      ))}
    </Table>
  );
}

/** The latest run of each job. */
function PipelinesTable({ overview }: { overview: Overview }) {
  // The window ends now, as the server answered, so ages count from there.
  const now = Date.parse(overview.to);

  if (overview.pipelines.length === 0) {
    return <p className="ov-empty">No job ran in the time range.</p>;
  }

  return (
    <Table heads={[{ title: "Job" }, { title: "Last run" }, { title: "Duration", numeric: true }, { title: "Status" }]}>
      {overview.pipelines.map((run) => (
        <tr key={`${run.server}/${run.name}`}>
          <td>
            <Link
              className="ov-name"
              to={
                run.jobId
                  ? `/jobs/${encodeURIComponent(run.jobId)}`
                  : `/metrics/databases/${encodeURIComponent(run.engine ?? "")}/${encodeURIComponent(run.server)}?tab=jobs`
              }
              title={`${run.name} on ${run.server}`}
            >
              {run.name}
            </Link>
          </td>
          <td>{run.lastRunAt ? `${seenWords(run.lastRunAt, now)} ago` : "—"}</td>
          <td className="num">{run.durationSeconds === null ? "—" : formatSpan(run.durationSeconds)}</td>
          <td>
            <span
              className={
                run.status === "Failed" ? "ov-pill bad" : run.status === "Running" ? "ov-pill info" : "ov-pill ok"
              }
            >
              {run.status === "Succeeded" ? "Success" : run.status}
            </span>
          </td>
        </tr>
      ))}
    </Table>
  );
}

/** The agents, for Admins. */
function CollectorsTable({ overview }: { overview: Overview }) {
  // The window ends now, as the server answered, so ages count from there.
  const now = Date.parse(overview.to);

  if (overview.collectors === null) {
    return <p className="ov-empty">Agents are listed for Admins, on Settings → Agents.</p>;
  }

  return (
    <Table heads={[{ title: "Agent" }, { title: "Last seen" }, { title: "Queue", numeric: true }, { title: "Status" }]}>
      {overview.collectors.map((agent) => (
        <tr key={agent.name}>
          <td>
            <Link className="ov-name" to="/settings?section=Agents">
              {agent.name}
            </Link>
          </td>
          <td>{agent.lastSeenAt ? `${seenWords(agent.lastSeenAt, now)} ago` : "—"}</td>
          <td className="num" title="Agents do not report their queue yet">
            {agent.queue ?? "—"}
          </td>
          <td>
            <AgentPill state={agent.state} />
          </td>
        </tr>
      ))}
    </Table>
  );
}

/** An agent's state in the Agents page's words and colours: online in green, a problem in red, not yet connected in grey. */
function AgentPill({ state }: { state: AgentState }) {
  const words = agentStateWords(state);
  const tone = { ok: "ok", bad: "bad", wait: "off" }[words.tone];

  return <span className={`ov-pill ${tone}`}>{words.label}</span>;
}
