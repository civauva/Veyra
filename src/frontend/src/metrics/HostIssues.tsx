import { Link } from "react-router";

import { api } from "../api/client";
import { defaultFilters, filtersToUrl } from "../logs/logFilters";
import { formatClockTime } from "./metricFormat";
import { useMetricData } from "./useMetricData";

/** Props for {@link HostIssues}. */
export interface HostIssuesProps {
  /** The host, as its host.name says. */
  host: string;
}

/**
 * The host's errors of the last day from its own logs, newest first, such as Windows Event Log errors read by its
 * agent (VEY-35, ADR 0018). Shows nothing when the host sent none, so a host without them keeps its screen as it was.
 *
 * @returns The panel, or null.
 */
export function HostIssues({ host }: HostIssuesProps) {
  const { data } = useMetricData(
    `host-issues-${host}`,
    (signal) => {
      const params = new URLSearchParams({
        service: host,
        severity: "Error",
        from: new Date(Date.now() - 24 * 3600_000).toISOString(),
        limit: "8",
      });

      return api.searchLogs(params, signal);
    },
    { refreshMs: 60_000 },
  );

  if (!data || data.items.length === 0) {
    return null;
  }

  const all = filtersToUrl({ ...defaultFilters, range: "24h", minimumSeverity: "Error", services: [host] });

  return (
    <section className="metric-panel wide" aria-labelledby="host-issues">
      <div className="metric-panel-head">
        <h2 id="host-issues">Recent errors</h2>
        <Link to={`/logs?${all}`}>All of the last day ›</Link>
      </div>
      <ul className="host-issues">
        {data.items.map((issue) => (
          <li key={issue.id}>
            <time dateTime={issue.timestamp}>{formatClockTime(issue.timestamp)}</time>
            <span>{issue.body}</span>
          </li>
        ))}
      </ul>
    </section>
  );
}
