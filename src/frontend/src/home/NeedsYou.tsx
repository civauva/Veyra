import { useId } from "react";
import { Link } from "react-router";

import { formatMeasure, ruleName, ruleSentence, ruleSummary, subjectLabel } from "../alerts/ruleWords";
import type { HomeAlert, HomeSnapshot } from "../api/types";
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
 * The Needs you pane: open alerts numbered warnings first, about services and hosts, the steps of the chosen one,
 * what resolved since yesterday, and one dot per service and per host. The numbers match the badges the dashboard
 * draws.
 */
export function NeedsYou({ snapshot, selectedId, onSelect, now }: NeedsYouProps) {
  const headingId = useId();
  const { alerts, blocks } = snapshot;
  const listed = alerts.slice(0, maxListed);
  const selected = alerts.find((alert) => alert.id === selectedId) ?? alerts[0] ?? null;
  const warnings = alerts.filter((alert) => alert.severity === "Warning").length;
  const cautions = alerts.length - warnings;
  const services = blocks.filter((block) => block.kind === "service");
  const hosts = blocks.filter((block) => block.kind === "host");
  const budget = snapshot.storage.budgetBytes;

  return (
    <section className="glass needs-you" aria-labelledby={headingId}>
      <div className="needs-head">
        <h2 id={headingId} className="cap">
          Needs you
        </h2>
        {warnings > 0 && <span className="chip warning">{warnings} warning</span>}
        {cautions > 0 && <span className="chip caution">{cautions} caution</span>}
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

      {snapshot.resolved.length > 0 && (
        <div className="resolved">
          <p className="cap" id={`${headingId}-resolved`}>
            Resolved since yesterday
          </p>
          <ul aria-labelledby={`${headingId}-resolved`}>
            {snapshot.resolved.map((alert) => (
              <li key={alert.id}>
                <i aria-hidden="true" />
                <Link className="resolved-link" to={`/alerts/${encodeURIComponent(alert.id)}`}>
                  {subjectLabel(alert)} · {ruleName(alert.rule).toLowerCase()} {formatMeasure(alert.rule, alert.peak)}
                </Link>
                <small>
                  {formatClock(alert.since)}–{formatClock(alert.resolvedAt ?? alert.since)}
                </small>
              </li>
            ))}
          </ul>
        </div>
      )}

      <div className="needs-foot">
        <p className="cap" id={`${headingId}-services`}>
          {hosts.length === 0
            ? `All ${services.length} services`
            : `${services.length} services · ${hosts.length} ${hosts.length === 1 ? "host" : "hosts"}`}
        </p>
        <BlockRow label="Services" blocks={services} labelledBy={`${headingId}-services`} />
        {hosts.length > 0 && <BlockRow label="Hosts" blocks={hosts} labelledBy={`${headingId}-services`} />}
        <p className="fine">
          Ingest {Math.round(snapshot.metrics.ingestPerSecond)}/s
          {budget ? ` · storage ${Math.round((snapshot.storage.usedBytes / budget) * 100)}% of budget` : ""}
        </p>
        <p className="fine">
          <Link className="resolved-link" to="/alerts">
            All alerts, last 90 days ›
          </Link>
        </p>
      </div>
    </section>
  );
}

/** Props for {@link BlockRow}. */
interface BlockRowProps {
  /** `Services` or `Hosts`. */
  label: string;
  /** The blocks, one dot each. */
  blocks: HomeSnapshot["blocks"];
  /** The ID of the heading the dots belong to. */
  labelledBy: string;
}

/** One dot per block, coloured by its health, and how many are dry. */
function BlockRow({ label, blocks, labelledBy }: BlockRowProps) {
  const dry = blocks.filter((block) => block.health === "Ok").length;

  return (
    <div className="service-row">
      <span>{label}</span>
      <ul className="dots" aria-labelledby={labelledBy}>
        {blocks.map((block) => (
          <li
            key={block.id}
            className={`dot ${block.health.toLowerCase()}`}
            aria-label={`${block.name}: ${block.health.toLowerCase()}`}
            title={`${block.name}: ${block.health.toLowerCase()}`}
          />
        ))}
      </ul>
      <span className="dry">
        {dry}/{blocks.length}
      </span>
    </div>
  );
}

/**
 * The steps of one alert. A service's errors lead into its logs; a slow service and a host lead into their metrics.
 * Every alert leads to its details.
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
