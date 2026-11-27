import { api } from "../../api/client";
import { formatAmount, formatSpan } from "../metricFormat";
import { useMetricData } from "../useMetricData";
import { PanelHead } from "./helpNames";

/** Props for {@link AvailabilitySection}. */
export interface AvailabilitySectionProps {
  /** The engine. */
  engine: string;
  /** The server. */
  server: string;
  /** Whether the Advanced view is on. */
  advanced: boolean;
}

/**
 * A SQL Server's availability on the Server tab (ADR 0020, area 12). The Simple view says whether it is healthy in one
 * line; the Advanced view lists replicas, each database's queue and lag, log shipping, and the cluster's nodes. Shows
 * nothing for a server without any of them.
 */
export function AvailabilitySection({ engine, server, advanced }: AvailabilitySectionProps) {
  const { data } = useMetricData(`availability-${engine}-${server}`, (signal) =>
    api.databaseAvailability(engine, server, signal),
  );

  if (!data || (data.replicas.length === 0 && data.logShipping.length === 0 && data.nodes.length === 0)) {
    return null;
  }

  // The server says what is not well (VEY-68).
  const { trouble } = data;

  return (
    <section className="metric-panel" aria-labelledby="availability-head">
      <PanelHead
        id="availability-head"
        name="Availability"
        help={
          "Availability Groups, log shipping, and the failover cluster, read every minute " +
          "from the sys.dm_hadr views, msdb's log shipping monitor, and " +
          "sys.dm_os_cluster_nodes. Every replica should be healthy and every database " +
          "synchronizing. A growing queue or lag means a failover now would lose or delay " +
          "data."
        }
      >
        <span>Availability Groups, log shipping, and the cluster</span>
      </PanelHead>
      <p className={trouble.length > 0 ? "metrics-error" : "metrics-empty"}>
        {trouble.length > 0
          ? `Needs a look: ${trouble.join("; ")}.`
          : "Every replica, log-shipped database, and node is healthy."}
      </p>
      {advanced && (
        <ul className="live-plain">
          {data.replicas.map((one) => (
            <li key={`r-${one.group}-${one.replica}`}>
              <span>
                <b>{one.replica}</b>{" "}
                <small className="faint">
                  {one.group} · {one.role.toLowerCase() || "unknown role"}
                </small>
              </span>
              <span className={`live-time ${one.healthy ? "" : "bad"}`}>{one.healthy ? "healthy" : "not healthy"}</span>
            </li>
          ))}
          {data.databases.map((one) => (
            <li key={`d-${one.group}-${one.replica}-${one.database}`}>
              <span>
                <b>{one.database}</b>{" "}
                <small className="faint">
                  {one.replica} · queue {formatAmount(one.queueBytes)} · lag {formatSpan(one.lagSeconds)}
                </small>
              </span>
              <span className={`live-time ${one.synchronizing ? "" : "bad"}`}>
                {one.synchronizing ? "synchronizing" : "not synchronizing"}
              </span>
            </li>
          ))}
          {data.logShipping.map((one) => (
            <li key={`l-${one.database}-${one.side}`}>
              <span>
                <b>{one.database}</b>{" "}
                <small className="faint">
                  log shipping {one.side} · last {one.side === "primary" ? "backup" : "restore"}{" "}
                  {one.delaySeconds === null ? "never" : `${formatSpan(one.delaySeconds)} ago`}
                </small>
              </span>
              <span className={`live-time ${one.behind ? "bad" : ""}`}>{one.behind ? "behind" : "on time"}</span>
            </li>
          ))}
          {data.nodes.map((one) => (
            <li key={`n-${one.node}`}>
              <span>
                <b>{one.node}</b> <small className="faint">cluster node · {one.state.toLowerCase()}</small>
              </span>
              <span className="live-time">{one.owner ? "runs SQL Server" : ""}</span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
