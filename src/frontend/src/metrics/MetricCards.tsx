import { Link } from "react-router";

import type { HostCard, MetricRange, ServiceCard } from "../api/types";
import { formatAgo, formatAmount, formatFull, formatRate, formatSeconds, formatShare } from "./metricFormat";
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
