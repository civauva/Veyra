import { Link } from "react-router";

import type { ContainerCard, DatabaseCard, HostCard, MetricRange, ServiceCard } from "../api/types";
import { containerAnchor, engineName } from "../alerts/ruleWords";
import {
  containerIsHot,
  formatAgo,
  formatAmount,
  formatCpuPercent,
  formatFull,
  formatRate,
  formatSeconds,
  formatShare,
  formatSpan,
  shareTone,
} from "./metricFormat";
import { connectionsRatio, connectionsTone, isPostgres, wraparoundTone } from "./database/postgres";
import { Ring } from "./Ring";
import { Sparkline } from "./Sparkline";

/** The icon of a host: two stacked servers. */
export function HostIcon({ size = 20 }: { size?: number }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      aria-hidden="true"
    >
      <rect x="3.5" y="4" width="17" height="6.5" rx="1.6" />
      <rect x="3.5" y="13.5" width="17" height="6.5" rx="1.6" />
      <path d="M7 7.25h.01M7 16.75h.01" strokeWidth="2.4" strokeLinecap="round" />
    </svg>
  );
}

/** The icon of a service: a box. */
export function ServiceIcon({ size = 20 }: { size?: number }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      aria-hidden="true"
    >
      <path d="M12 3.5 20 8v8l-8 4.5L4 16V8z M4 8l8 4.5L20 8 M12 12.5v8" strokeLinejoin="round" />
    </svg>
  );
}

/** The icon of a database server: a cylinder. */
export function DatabaseIcon({ size = 20 }: { size?: number }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      aria-hidden="true"
    >
      <ellipse cx="12" cy="6" rx="7.5" ry="2.8" />
      <path d="M4.5 6v12c0 1.6 3.4 2.8 7.5 2.8s7.5-1.2 7.5-2.8V6 M4.5 12c0 1.6 3.4 2.8 7.5 2.8s7.5-1.2 7.5-2.8" />
    </svg>
  );
}

/** The icon of a Docker container: a shipping container seen from the side. */
export function ContainerIcon({ size = 20 }: { size?: number }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      aria-hidden="true"
    >
      <rect x="3" y="6.5" width="18" height="11" rx="1.2" />
      <path d="M7 9.5v5M10.3 9.5v5M13.7 9.5v5M17 9.5v5" strokeLinecap="round" />
    </svg>
  );
}

/** Props for {@link ContainerCardView}. */
interface ContainerCardViewProps {
  /** The container's numbers. */
  container: ContainerCard;
  /** The range, carried into its host's page. */
  range: MetricRange;
  /** The current time, for how long ago it sent. */
  now: Date;
}

/**
 * One Docker container on the overview (ADR 0018): CPU, memory of its limit, and restarts, with CPU over the range
 * and how long it has been up. The whole card leads to its row on its host's screen.
 */
export function ContainerCardView({ container, range, now }: ContainerCardViewProps) {
  const id = `container-${container.host ?? ""}-${container.name}`;
  const facts = [container.image, container.host ? `on ${container.host}` : null, container.environment]
    .filter(Boolean)
    .join(" · ");
  const restarting = (container.restarts ?? 0) > 0;
  const body = (
    <>
      <div className="metric-card-head">
        <span className="metric-icon container">
          <ContainerIcon />
        </span>
        <div>
          <h3 id={id}>{container.name}</h3>
          <small>{facts}</small>
        </div>
        <span className="metric-when">{formatAgo(container.lastSeen, now)}</span>
      </div>
      <dl className="metric-numbers">
        <div>
          <dt>CPU</dt>
          <dd>{formatCpuPercent(container.cpuPercent)}</dd>
        </div>
        <div>
          <dt>Memory</dt>
          <dd className={shareTone(container.memoryRatio) === "ok" ? undefined : shareTone(container.memoryRatio)}>
            {formatShare(container.memoryRatio)}
            {container.memoryLimitBytes !== null && <small> of {formatAmount(container.memoryLimitBytes)}</small>}
          </dd>
        </div>
        <div>
          <dt>Restarts</dt>
          <dd className={restarting ? "warn" : undefined}>{container.restarts === null ? "—" : container.restarts}</dd>
        </div>
      </dl>
      <Sparkline line={container.cpu} color="var(--metric-pink)" />
      <div className="metric-card-foot">
        <span>CPU, last {range}</span>
        {restarting ? (
          <b className="warn">
            Restarted {container.restarts === 1 ? "once" : `${container.restarts} times`} in {range}
          </b>
        ) : (
          <span>{container.uptimeSeconds === null ? "Running" : `Up ${formatSpan(container.uptimeSeconds)}`}</span>
        )}
      </div>
    </>
  );

  return (
    <article className={containerIsHot(container) ? "metric-card hot" : "metric-card"} aria-labelledby={id}>
      {container.host ? (
        <Link
          className="metric-card-link"
          to={`/metrics/hosts/${encodeURIComponent(container.host)}?range=${range}#${containerAnchor(container.name)}`}
        >
          {body}
        </Link>
      ) : (
        <div className="metric-card-link">{body}</div>
      )}
    </article>
  );
}

/** Props for {@link DatabaseCardView}. */
interface DatabaseCardViewProps {
  /** The server's numbers. */
  database: DatabaseCard;
  /** The range, carried into the server's page. */
  range: MetricRange;
  /** The current time, for how long ago it sent. */
  now: Date;
}

/**
 * One database server on the overview (ADR 0018): batch requests, blocked sessions, and the longest query now, batch
 * requests over the range, and what needs a look: failed SQL Agent jobs or the oldest backup. A PostgreSQL server
 * shows its connections of the maximum, transactions over the range, and how far it is to wraparound instead
 * (VEY-33). The whole card leads to the server's page.
 */
export function DatabaseCardView({ database, range, now }: DatabaseCardViewProps) {
  const id = `database-${database.engine}-${database.server}`;
  const facts = [
    engineName(database.engine),
    database.environment,
    database.databases === 1 ? "1 database" : `${database.databases} databases`,
  ]
    .filter(Boolean)
    .join(" · ");
  const postgres = isPostgres(database.engine);
  const blocked = database.blocked !== null && database.blocked > 0;
  const longQuery = database.longestQuerySeconds !== null && database.longestQuerySeconds >= 300;
  const lateBackup = database.oldestBackupSeconds !== null && database.oldestBackupSeconds >= 26 * 3600;
  const connections = connectionsRatio(database.connections, database.maxConnections);
  const crowded = connectionsTone(connections) !== "ok";
  const wrapping = wraparoundTone(database.wraparoundRatio) !== "ok";
  const hot = blocked || longQuery || lateBackup || database.failedJobs > 0 || crowded || wrapping;

  return (
    <article className={hot ? "metric-card hot" : "metric-card"} aria-labelledby={id}>
      <Link
        className="metric-card-link"
        to={`/metrics/databases/${encodeURIComponent(database.engine)}/${encodeURIComponent(database.server)}?range=${range}`}
      >
        <div className="metric-card-head">
          <span className="metric-icon database">
            <DatabaseIcon />
          </span>
          <div>
            <h3 id={id}>{database.server}</h3>
            <small>{facts}</small>
          </div>
          <span className="metric-when">{formatAgo(database.lastSeen, now)}</span>
        </div>
        <dl className="metric-numbers">
          {postgres ? (
            <div>
              <dt>Connections</dt>
              <dd className={crowded ? "warn" : undefined}>
                {database.connections === null ? "–" : Math.round(database.connections)}
                {database.maxConnections !== null && <small> of {Math.round(database.maxConnections)}</small>}
              </dd>
            </div>
          ) : (
            <div>
              <dt>Requests</dt>
              <dd>
                {formatRate(database.batchRequests)}
                <small>/s</small>
              </dd>
            </div>
          )}
          <div>
            <dt>Blocked</dt>
            <dd className={blocked ? "warn" : undefined}>{database.blocked === null ? "–" : database.blocked}</dd>
          </div>
          <div>
            <dt>Longest query</dt>
            <dd className={longQuery ? "warn" : undefined}>
              {database.longestQuerySeconds === null ? "–" : formatSpan(database.longestQuerySeconds)}
            </dd>
          </div>
        </dl>
        <Sparkline line={database.requests} color="var(--metric-teal)" />
        <div className="metric-card-foot">
          <span>
            {postgres ? "Transactions" : "Batch requests"}, last {range}
          </span>
          {postgres ? (
            <Wraparound ratio={database.wraparoundRatio} />
          ) : database.failedJobs > 0 ? (
            <b className="warn">{database.failedJobs === 1 ? "1 job failed" : `${database.failedJobs} jobs failed`}</b>
          ) : lateBackup && database.oldestBackupSeconds !== null ? (
            <b className="warn">Oldest backup {formatSpan(database.oldestBackupSeconds)}</b>
          ) : (
            <span>Backups and jobs fine</span>
          )}
        </div>
      </Link>
    </article>
  );
}

/**
 * How far a PostgreSQL server's oldest transaction ID is to wraparound, at the foot of its card.
 *
 * @param props - The component's props.
 * @param props.ratio - The highest transaction ID age of any database, of the limit; null when not reported.
 */
function Wraparound({ ratio }: { ratio: number | null }) {
  if (ratio === null) {
    return <span>Wraparound not reported</span>;
  }

  const text = `Wraparound ${formatShare(ratio)}`;

  return wraparoundTone(ratio) === "ok" ? <span>{text}</span> : <b className="warn">{text}</b>;
}

/** Props for {@link HostCardView}. */
interface HostCardViewProps {
  /** The host's numbers. */
  host: HostCard;
  /** The range, carried into the host's page. */
  range: MetricRange;
  /** The current time, for how long ago it sent. */
  now: Date;
}

/**
 * One host on the overview: CPU, memory, and fullest disk as rings, CPU over the range, and the disk that fills
 * soonest. The whole card leads to the host's page.
 */
export function HostCardView({ host, range, now }: HostCardViewProps) {
  const disk = host.fullestDisk;
  const facts = [
    host.os,
    host.cpus === null ? null : `${host.cpus} CPUs`,
    host.memoryBytes === null ? null : formatAmount(host.memoryBytes),
  ]
    .filter(Boolean)
    .join(" · ");

  return (
    <article className={host.fillingDisk ? "metric-card hot" : "metric-card"} aria-labelledby={`host-${host.name}`}>
      <Link className="metric-card-link" to={`/metrics/hosts/${encodeURIComponent(host.name)}?range=${range}`}>
        <div className="metric-card-head">
          <span className="metric-icon">
            <HostIcon />
          </span>
          <div>
            <h3 id={`host-${host.name}`}>{host.name}</h3>
            <small>{facts || "host metrics"}</small>
          </div>
          <span className="metric-when">{formatAgo(host.lastSeen, now)}</span>
        </div>
        <div className="rings">
          <Ring share={host.cpuRatio} label="CPU" detail={host.load === null ? "" : `load ${formatRate(host.load)}`} />
          <Ring
            share={host.memoryRatio}
            label="Memory"
            detail={
              host.memoryRatio !== null && host.memoryBytes !== null
                ? `${formatAmount(host.memoryRatio * host.memoryBytes)} of ${formatAmount(host.memoryBytes)}`
                : ""
            }
          />
          <Ring
            share={disk?.ratio ?? null}
            label={disk ? `Disk ${disk.mountpoint}` : "Disk"}
            detail={disk ? `${formatAmount(disk.usedBytes)} of ${formatAmount(disk.totalBytes)}` : "not reported"}
          />
        </div>
        <Sparkline line={host.cpu} color="var(--accent)" max={1} />
        <div className="metric-card-foot">
          <span>CPU, last {range}</span>
          {host.fillingDisk ? (
            <b className="warn">
              Disk {host.fillingDisk.mountpoint} {formatFull(host.fillingDisk.daysUntilFull)}
            </b>
          ) : (
            <span>Disks steady</span>
          )}
        </div>
      </Link>
    </article>
  );
}

/** Props for {@link ServiceCardView}. */
interface ServiceCardViewProps {
  /** The service's numbers. */
  service: ServiceCard;
  /** The range, carried into the service's page. */
  range: MetricRange;
  /** The current time, for how long ago it sent. */
  now: Date;
}

/**
 * One service on the overview: requests per second, p95 latency, and the 5xx share, with p95 over the range. The
 * whole card leads to the service's page.
 */
export function ServiceCardView({ service, range, now }: ServiceCardViewProps) {
  const params = new URLSearchParams({ range });

  if (service.environment) {
    params.set("environment", service.environment);
  }

  const facts = [
    service.environment,
    service.runtime,
    service.instances === 1 ? "1 instance" : `${service.instances} instances`,
  ]
    .filter(Boolean)
    .join(" · ");
  const errors = service.serverErrorRatio;
  const slow = service.p95Seconds !== null && service.p95Seconds >= 0.4;

  return (
    <article className="metric-card" aria-labelledby={`service-${service.name}-${service.environment ?? ""}`}>
      <Link
        className="metric-card-link"
        to={`/metrics/services/${encodeURIComponent(service.name)}?${params.toString()}`}
      >
        <div className="metric-card-head">
          <span className="metric-icon service">
            <ServiceIcon />
          </span>
          <div>
            <h3 id={`service-${service.name}-${service.environment ?? ""}`}>{service.name}</h3>
            <small>{facts}</small>
          </div>
          <span className="metric-when">{formatAgo(service.lastSeen, now)}</span>
        </div>
        <dl className="metric-numbers">
          <div>
            <dt>Requests</dt>
            <dd>
              {formatRate(service.requestsPerSecond)}
              <small>/s</small>
            </dd>
          </div>
          <div>
            <dt>p95</dt>
            <dd className={slow ? "warn" : undefined}>{formatSeconds(service.p95Seconds)}</dd>
          </div>
          <div>
            <dt>5xx</dt>
            <dd className={errors !== null && errors >= 0.01 ? "bad" : undefined}>{formatShare(errors)}</dd>
          </div>
        </dl>
        <Sparkline line={service.latency} color="var(--metric-violet)" />
        <div className="metric-card-foot">
          <span>p95 latency, last {range}</span>
          <span>
            {[
              service.cpuRatio === null ? null : `CPU ${formatShare(service.cpuRatio)}`,
              service.memoryBytes === null ? null : formatAmount(service.memoryBytes),
            ]
              .filter(Boolean)
              .join(" · ")}
          </span>
        </div>
      </Link>
    </article>
  );
}
