import { api } from "../../api/client";
import { unreadText } from "./unread";
import type { MetricRange, ServerView } from "../../api/types";
import { LineChart } from "../LineChart";
import { formatAmount, formatRate, formatSeconds, formatSpan } from "../metricFormat";
import { useMetricData } from "../useMetricData";
import { AvailabilitySection } from "./AvailabilitySection";
import { ConfigurationSection } from "./ConfigurationSection";
import { PanelHead } from "./helpNames";
import { FindingItem } from "./StorageTab";
import { SystemHidden, SystemTag, useSystemShown } from "./SystemFilter";

/** Props for {@link ServerTab} and {@link WaitTypes}. */
export interface ServerTabProps {
  /** The engine. */
  engine: string;
  /** The server. */
  server: string;
  /** The range. */
  range: MetricRange;
  /** Whether the Advanced view is on. */
  advanced: boolean;
  /** Whether the login lacks a permission this tab needs, so an empty answer proves nothing (VEY-58). */
  blind?: boolean;
}

/** Reads the server as a whole once for the Server and Waits tabs; its idle waits only when System is on (VEY-66). */
function useServer(engine: string, server: string, range: MetricRange) {
  const [systemShown] = useSystemShown();

  return useMetricData(
    `server-${engine}-${server}-${range}`,
    (signal) => api.databaseServer(engine, server, range, systemShown, signal),
    { choices: String(systemShown) },
  );
}

/**
 * A SQL Server as a whole (ADR 0020, areas 1 and 14): Veyra's findings first; the Advanced view adds its version,
 * CPU shared with other processes, workers, memory, dumps, SQL Server Agent, and its workload against the usual for
 * this hour of the week.
 */
export function ServerTab({ engine, server, range, advanced, blind = false }: ServerTabProps) {
  const { data, notFound, error } = useServer(engine, server, range);

  if (notFound) {
    return (
      <p className="metrics-empty">
        The agent has not sent the server&apos;s own figures yet; it reads them every minute.
      </p>
    );
  }

  if (!data) {
    return error ? (
      <p className="metrics-error" role="alert">
        The server could not be loaded: {error}
      </p>
    ) : null;
  }

  return (
    <div className="metric-panels">
      <section className="metric-panel" aria-labelledby="server-findings">
        <PanelHead
          id="server-findings"
          name="The server"
          help={
            "Veyra's findings about the instance as a whole, read every minute: CPU, memory, " +
            "worker threads, memory grants, SQL Server Agent, restarts, memory dumps, and " +
            "whether its version is still supported. Each says what to do; nothing listed " +
            "means nothing stands out."
          }
        >
          <span>
            SQL Server {data.version} {data.updateLevel} · {data.edition}
            {data.uptimeSeconds !== null ? ` · up ${formatSpan(data.uptimeSeconds)}` : ""}
          </span>
        </PanelHead>
        {data.findings.length === 0 ? (
          <p className="metrics-empty">
            {blind
              ? unreadText
              : "Nothing stands out: CPU, memory, workers, SQL Server Agent, and the version all look right."}
          </p>
        ) : (
          <ul className="live-findings">
            {data.findings.map((finding) => (
              <FindingItem key={finding.kind} finding={finding} />
            ))}
          </ul>
        )}
      </section>
      <AvailabilitySection engine={engine} server={server} advanced={advanced} />
      <ConfigurationSection engine={engine} server={server} advanced={advanced} blind={blind} />
      {advanced && <Figures data={data} />}
    </div>
  );
}

/** The server's figures now and its CPU over the range. Advanced only. */
function Figures({ data }: { data: ServerView }) {
  const share = (value: number | null) => (value === null ? "–" : `${Math.round(value * 100)}%`);

  return (
    <section className="metric-panel" aria-labelledby="server-figures">
      <PanelHead
        id="server-figures"
        name="CPU, memory, and workload"
        help={
          "Read every minute: SQL Server's share of the CPU and everything else's, from the " +
          "scheduler monitor ring buffer, with tasks waiting for a CPU " +
          "(sys.dm_os_schedulers); worker threads in use; memory against its target; grants " +
          "waiting; and requests against the usual for this hour of the week. Others taking " +
          "CPU, workers near 100%, or grants waiting mean the server runs short."
        }
      />
      <dl className="live-facts">
        <dt>CPU</dt>
        <dd>
          SQL Server {share(data.sqlCpu)} · others {share(data.otherCpu)} · {data.runnable ?? "–"} tasks waiting for a
          CPU
        </dd>
        <dt>Workers</dt>
        <dd>{share(data.workers)} in use</dd>
        <dt>Memory</dt>
        <dd>
          uses {formatAmount(data.memoryCommitted)} of a target of {formatAmount(data.memoryTarget)} · machine free{" "}
          {formatAmount(data.osMemoryFree)}
          {data.osMemoryLow ? " (low)" : ""} · {data.grantsWaiting ?? 0} grants waiting
        </dd>
        <dt>Requests</dt>
        <dd>
          {formatRate(data.requests)} a second now
          {data.requestsUsual !== null ? `, ${formatRate(data.requestsUsual)} usual for this hour of the week` : ""}
        </dd>
        <dt>Agent</dt>
        <dd>{data.agentStopped === null ? "none on this edition" : data.agentStopped ? "not running" : "running"}</dd>
        <dt>Memory dumps</dt>
        <dd>{data.dumpsDay ?? 0} in the last day</dd>
      </dl>
      <LineChart
        title="CPU"
        format={(value) => `${Math.round(value * 100)}%`}
        max={1}
        series={[
          { line: data.sqlCpuLine, color: "var(--accent)", name: "SQL Server", filled: true },
          { line: data.otherCpuLine, color: "var(--warning-fg)", name: "Others" },
        ]}
      />
    </section>
  );
}

/** The wait types with the most time over the range, each explained, for the Waits tab. */
export function WaitTypes({ engine, server, range }: ServerTabProps) {
  const { data } = useServer(engine, server, range);

  if (!data || (data.waits.length === 0 && data.hiddenWaits.count === 0)) {
    return null;
  }

  return (
    <section className="metric-panel" aria-labelledby="wait-types">
      <PanelHead
        id="wait-types"
        name="Wait types"
        help={
          "The 15 wait types with the most waiting time over the range, from " +
          "sys.dm_os_wait_stats, read every 15 seconds, each with what it means and what " +
          "usually causes it. The top one is where the server's time goes; fix its cause " +
          "first."
        }
      >
        <span>the most time over the range, explained</span>
      </PanelHead>
      <ul className="live-findings">
        {data.waits.map((wait) => (
          <li key={wait.term.type} className="live-finding ok">
            <i aria-hidden="true" />
            <div>
              <b>
                {wait.term.type} · {wait.term.category} · {formatSeconds(wait.seconds)}
                {wait.system && <SystemTag />}
              </b>
              <p>
                {wait.term.meaning} Usually: {wait.term.causes}
              </p>
            </div>
          </li>
        ))}
      </ul>
      <SystemHidden hidden={data.hiddenWaits} noun="wait type" />
    </section>
  );
}
