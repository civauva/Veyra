import { Link } from "react-router";

import type { AlertDetails } from "../api/types";
import { formatClock, formatCount } from "../logs/format";
import { defaultFilters, filtersToUrl } from "../logs/logFilters";

/** Props for {@link TopErrors}. */
interface TopErrorsProps {
  /** The alert, with its most frequent errors. */
  alert: AlertDetails;
}

/**
 * The error messages the service logged most while the alert was open, counted by exact message, each with a link to
 * its records in the Log Explorer.
 */
export function TopErrors({ alert }: TopErrorsProps) {
  const most = Math.max(1, ...alert.topErrors.map((group) => group.count));
  const opened = new Date(new Date(alert.openedAt).getTime() - alert.windowSeconds * 1000);

  return (
    <section className="glass alert-panel" aria-labelledby="alert-top-errors">
      <h2 id="alert-top-errors" className="cap">
        Most frequent errors since {formatClock(opened)}
      </h2>
      {alert.topErrors.length === 0 ? (
        <p className="fine">No error records in this period.</p>
      ) : (
        <ol className="top-errors">
          {alert.topErrors.map((group) => (
            <li key={group.message}>
              <span className="top-error-what">
                <code title={group.message}>{group.message}</code>
                <small>
                  first {formatClock(group.first)} · last {formatClock(group.last)}
                </small>
                <span className="top-error-share" style={{ width: `${(group.count / most) * 100}%` }} />
              </span>
              <b className="top-error-count">{formatCount(group.count)}</b>
              <Link
                className="step-button"
                to={`/logs?${filtersToUrl({
                  ...defaultFilters,
                  range: "24h",
                  minimumSeverity: "Error",
                  services: [alert.block.name],
                  text: group.message,
                })}`}
                aria-label={`Logs with “${group.message}”`}
              >
                Logs
              </Link>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
