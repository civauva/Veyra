import { useId } from "react";
import { Link } from "react-router";

import {
  containerPath,
  databasePath,
  formatMeasure,
  ruleSentence,
  ruleSummary,
  serverName,
  subjectLabel,
} from "../alerts/ruleWords";
import type { HomeAlert, HomeSnapshot } from "../api/types";
import { HelpTip } from "../ui/HelpTip";
import { formatClock } from "../logs/format";
import { defaultFilters, filtersToUrl } from "../logs/logFilters";
import { formatAge } from "./homeFormat";

/** The most alerts listed; the rest are counted, as the shell design asks. */
const maxListed = 9;

/** Props for {@link NeedsYou}. */
interface NeedsYouProps {
  /** The latest snapshot. */
  snapshot: HomeSnapshot;
  /** The ID of the alert whose steps are open. */
  selectedId: string | null;
  /**
   * Called when the person opens another alert.
   *
   * @param id - The alert ID.
   */
  onSelect(id: string): void;
  /** The current time, for the age of each alert. */
  now: Date;
}

/**
 * The Needs you pane: the one place that tells the open alerts (VEY-26), numbered warnings first, about services and
 * hosts, with the steps of the chosen one. The numbers match the badges the dashboard draws. What resolved is told on
 * the Alerts screen, which the foot leads to; the services and hosts are in the estate.
 */
export function NeedsYou({ snapshot, selectedId, onSelect, now }: NeedsYouProps) {
  const headingId = useId();
  const { alerts } = snapshot;
  const listed = alerts.slice(0, maxListed);
  const selected = alerts.find((alert) => alert.id === selectedId) ?? alerts[0] ?? null;
  const resolved = snapshot.resolved.length;

  return (
    <section className="glass needs-you" aria-labelledby={headingId}>
      <div className="needs-head">
        <h2 id={headingId} className="cap">
          Needs you
        </h2>
        <HelpTip label="Needs you">
          Every open alert, warnings first and then the oldest, each with its latest figure and how long it has been
          open. Choose one for the steps that help with it. Resolved alerts are in the alert history.
        </HelpTip>
      </div>

      {alerts.length === 0 ? (
        <div className="needs-none">
          <span className="needs-none-mark" aria-hidden="true" />
          <div>
            <b>Nothing needs you</b>
            <span>
              No alert is open{snapshot.environment ? ` in ${snapshot.environment}` : ""}. Veyra checks every service
              and host every 30 seconds.
            </span>
          </div>
        </div>
      ) : (
        <ol className="alert-list">
          {listed.map((alert) => (
            <li key={alert.id}>
              <button
                type="button"
                className={alert.id === selected?.id ? `alert-row ${tone(alert)} open` : `alert-row ${tone(alert)}`}
                aria-expanded={alert.id === selected?.id}
                onClick={() => onSelect(alert.id)}
              >
                <span className={`badge ${tone(alert)}`}>{alert.n}</span>
                <span className="alert-what">
                  <b>
                    <span className="kind-tag">{alert.kind}</span>
                    {subjectLabel(alert)}
                  </b>
                  <small>{ruleSummary(alert.rule, alert.threshold, alert.windowSeconds)}</small>
                </span>
                <span className="alert-value">
                  <b>{formatMeasure(alert.rule, alert.value)}</b>
                  <small>{formatAge(alert.since, now)}</small>
                </span>
              </button>
              {alert.id === selected?.id && <AlertSteps alert={alert} />}
            </li>
          ))}
          {alerts.length > maxListed && <li className="alert-more">+{alerts.length - maxListed} more</li>}
        </ol>
      )}

      <div className="needs-foot">
        <span>{resolved === 0 ? "Nothing resolved since yesterday" : `${resolved} resolved since yesterday`}</span>
        <Link className="resolved-link" to="/alerts">
          Alert history<span aria-hidden="true"> ›</span>
        </Link>
      </div>
    </section>
  );
}

/**
 * The steps of one alert. A service's errors lead into its logs; a slow service and a host lead into their metrics;
 * a container leads to its row on its host's screen and to its logs. Every alert leads to its details.
 */
function AlertSteps({ alert }: { alert: HomeAlert }) {
  const since = formatClock(alert.since);

  return (
    <div className={`alert-steps ${tone(alert)}`}>
      <ol>
        {alert.rule === "error-rate" && (
          <>
            <li>
              <span>See its errors in the Log Explorer</span>
              <Link
                className="step-button primary"
                to={`/logs?${filtersToUrl({ ...defaultFilters, minimumSeverity: "Error", services: [alert.name] })}`}
              >
                Open logs
              </Link>
            </li>
            <li>
              <span>What else it logged when it started, {since}</span>
              <Link
                className="step-button"
                to={`/logs?${filtersToUrl({ ...defaultFilters, services: [alert.name], around: new Date(alert.since).toISOString() })}`}
              >
                ± 1 minute
              </Link>
            </li>
          </>
        )}
        {alert.rule === "latency-p95" && (
          <li>
            <span>Its request durations and routes in Metrics</span>
            <Link className="step-button primary" to={`/metrics/services/${encodeURIComponent(alert.name)}`}>
              Open metrics
            </Link>
          </li>
        )}
        {alert.kind === "database" && (
          <li>
            <span>See {serverName(alert.name)} in Metrics: its queries, databases, and jobs</span>
            <Link className="step-button primary" to={databasePath(alert.name)}>
              Open metrics
            </Link>
          </li>
        )}
        {alert.kind === "container" && (
          <>
            <li>
              <span>See {alert.name} beside the other containers of its host</span>
              <Link className="step-button primary" to={containerPath(alert)}>
                Open metrics
              </Link>
            </li>
            <li>
              <span>What it logged when it started, {since}</span>
              <Link
                className="step-button"
                to={`/logs?${filtersToUrl({ ...defaultFilters, services: [alert.name], around: new Date(alert.since).toISOString() })}`}
              >
                ± 1 minute
              </Link>
            </li>
          </>
        )}
        {alert.kind === "host" && (
          <li>
            <span>See the disks of {alert.name} in Metrics</span>
            <Link className="step-button primary" to={`/metrics/hosts/${encodeURIComponent(alert.name)}`}>
              Open metrics
            </Link>
          </li>
        )}
        <li>
          <span>Its chart, timeline, and who was told</span>
          <Link className="step-button" to={`/alerts/${encodeURIComponent(alert.id)}`}>
            Details
          </Link>
        </li>
      </ol>
      <p>{ruleSentence(alert.rule, alert.severity, alert.threshold, alert.windowSeconds)}</p>
    </div>
  );
}

function tone(alert: HomeAlert): string {
  return alert.severity === "Warning" ? "warning" : "caution";
}
