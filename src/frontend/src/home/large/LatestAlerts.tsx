import { Link } from "react-router";

import type { AlertListItem } from "../../api/types";
import { formatDuration } from "../../alerts/alertFormat";
import { alertTitle, formatMeasure, ruleName } from "../../alerts/ruleWords";
import { formatClock } from "../../logs/format";
import { WeatherIcon } from "../WeatherIcon";

/** Props for {@link LatestAlerts}. */
interface LatestAlertsProps {
  /** The newest alerts of the last 30 days, open and resolved; null before they arrive. */
  items: AlertListItem[] | null;
  /** Why they could not be read, or null. */
  error: string | null;
  /** The time of the snapshot, for how long open alerts have lasted. */
  now: Date;
}

/** The most alerts the panel lists; the Alerts screen has the rest. */
const maxListed = 16;

/**
 * The newest alerts, open and resolved (VEY-24): what each said, its rule and when, and its figure: the latest while
 * open, the worst once resolved, as on the Alerts screen. Each leads to the alert's details.
 */
export function LatestAlerts({ items, error, now }: LatestAlertsProps) {
  return (
    <section className="glass lg-panel lg-latest" aria-label="Latest alerts">
      <div className="lg-head">
        <h2 className="cap">Latest alerts</h2>
        <Link className="lg-more" to="/alerts">
          All alerts
          <span aria-hidden="true"> ›</span>
        </Link>
      </div>
      {error && (
        <p className="fine" role="alert">
          Could not read the alerts: {error}
        </p>
      )}
      {items === null ? (
        !error && <p className="fine">Loading the alerts…</p>
      ) : items.length === 0 ? (
        <p className="fine">No alert in the last 30 days.</p>
      ) : (
        <ul className="lg-alerts">
          {items.slice(0, maxListed).map((item) => {
            const open = item.resolvedAt === null;
            const shown = open ? item.value : item.peak;

            return (
              <li key={item.id}>
                <Link
                  className={open ? `lg-alert open ${item.severity === "Warning" ? "w" : "c"}` : "lg-alert"}
                  to={`/alerts/${encodeURIComponent(item.id)}`}
                >
                  <WeatherIcon kind={item.worst === "Warning" ? "storm" : "shower"} size={24} />
                  <span className="lg-alert-what">
                    <b>{alertTitle(item.rule, item.block, shown, !open)}</b>
                    <small>
                      {ruleName(item.rule)} · {when(item, now)}
                    </small>
                  </span>
                  <span className="lg-alert-value">{formatMeasure(item.rule, shown)}</span>
                </Link>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}

/** For example `open 36 min`, `03:10–03:24`, or `Nov 8, 21:40–22:05`. */
function when(item: AlertListItem, now: Date): string {
  if (item.resolvedAt === null) {
    return `open ${formatDuration(now.getTime() - Date.parse(item.openedAt))}`;
  }

  const span = `${formatClock(item.openedAt)}–${formatClock(item.resolvedAt)}`;

  if (item.openedAt.slice(0, 10) === now.toISOString().slice(0, 10)) {
    return span;
  }

  const day = new Date(item.openedAt).toLocaleDateString("en-US", { month: "short", day: "numeric", timeZone: "UTC" });

  return `${day}, ${span}`;
}
