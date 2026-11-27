import { Link, useParams } from "react-router";

import { alertFigures, alertState, blockLink } from "../../../alerts/alertFigures";
import { deliveryTag, eventTitle, formatDuration, headline, isErrorRate } from "../../../alerts/alertFormat";
import { DiskChart } from "../../../alerts/DiskChart";
import { ErrorRateChart } from "../../../alerts/ErrorRateChart";
import { LatencyChart } from "../../../alerts/LatencyChart";
import { ruleGuidance } from "../../../alerts/ruleGuidance";
import {
  formatDays,
  formatDiskShare,
  formatMeasure,
  formatThreshold,
  ruleName,
  subjectLabel,
} from "../../../alerts/ruleWords";
import { useAlertContext } from "../../../alerts/useAlertContext";
import { useAlertDetails } from "../../../alerts/useAlertDetails";
import "../../../alerts/alert.css";
import type { AlertContext, AlertDetails } from "../../../api/types";
import { formatClock, formatCount, formatTime } from "../../../logs/format";
import { levelName } from "../../../logs/levelWords";
import { formatRate, formatSeconds, formatShare } from "../../../metrics/metricFormat";
import { useSession } from "../../../session/useSession";
import { CommandBlock } from "../../../ui/CommandBlock";
import { HelpTip } from "../../../ui/HelpTip";
import { ScreenBar } from "../../../ui/ScreenBar";
import { useViewMode } from "../../../ui/ViewToggle";
import { ConsoleIcon, type ConsoleIconName } from "../ConsoleIcon";
import { SqlFigure } from "../database/sqlParts";
import "../traces/consoleTraces.css";
import "./consoleAlert.css";

/** The picture of each kind of block. */
const blockIcons: Record<AlertDetails["block"]["kind"], ConsoleIconName> = {
  service: "services",
  host: "hosts",
  database: "database",
  container: "container",
};

/** How each kind of block is named before its name. */
const kindWords: Record<AlertDetails["block"]["kind"], string> = {
  service: "Service",
  host: "Host",
  database: "Database",
  container: "Container",
};

/**
 * Console's alert (VEY-79, ADR 0029): the alert's head with its facts and the ways onward, the figures and the chart of
 * its kind, what it means and how to fix it, the logs around it and the blocks it touches (from the alert's context),
 * its timeline, and its rule. Nothing is painted behind the shell: Console has no weather. The timeline and the
 * breakdowns are in the Advanced view, as on every look.
 */
export function ConsoleAlert() {
  const { id = "" } = useParams();
  const { alert, notFound, error } = useAlertDetails(id);

  if (!alert) {
    return (
      <main className="cn-screen sq al">
        <ScreenBar trail={[{ label: "Alerts", to: "/alerts" }, { label: "Alert" }]} view />
        <p className={error ? "cn-error" : "cn-empty"} role={error ? "alert" : undefined}>
          {notFound
            ? "There is no such alert. Resolved alerts are kept for 90 days, so it may have been older than that."
            : error
              ? `The alert could not be read: ${error}`
              : "Reading the alert…"}
        </p>
      </main>
    );
  }

  return <AlertView alert={alert} error={error} />;
}

/** The alert once it arrived. */
function AlertView({ alert, error }: { alert: AlertDetails; error: string | null }) {
  const [mode] = useViewMode();
  const advanced = mode === "advanced";
  const { state } = useSession();
  const admin = state.status === "signedIn" && state.session.role === "Admin";
  const context = useAlertContext(alert.id, alert.resolvedAt !== null);
  const pill = alertState(alert);
  const block = blockLink(alert.block);
  const told = alert.told.map((channel) => channel.name);
  const opened = Date.parse(alert.openedAt);
  const open = alert.resolvedAt === null;

  return (
    <main className="cn-screen sq al">
      <ScreenBar
        trail={[{ label: "Alerts", to: "/alerts" }, { label: `${alert.block.name} · ${ruleName(alert.rule)}` }]}
        view
      />
      <div className="sq-head">
        <span className={`sq-icon tr-icon al-icon ${pill.tone}`} aria-hidden="true">
          <ConsoleIcon name={blockIcons[alert.block.kind]} size={22} />
        </span>
        <div className="tr-head-text">
          <h1>
            {headline(alert)}
            <span className={`cn-pill ${pill.tone}`}>{pill.words}</span>
          </h1>
          <p className="sq-facts tr-facts">
            <span>Rule: {ruleName(alert.rule)}</span>
            <span>
              Opened {formatClock(alert.openedAt)},{" "}
              {open
                ? `${formatDuration(Date.parse(alert.updatedAt) - opened)} ago`
                : `resolved ${formatClock(alert.resolvedAt ?? alert.updatedAt)}`}
            </span>
            <span>
              {alert.block.kind === "database"
                ? subjectLabel(alert.block)
                : `${kindWords[alert.block.kind]} ${subjectLabel(alert.block)}`}
            </span>
            <span>{told.length === 0 ? "No channel told" : `Told ${told.join(", ")}`}</span>
          </p>
        </div>
        <div className="cn-head-actions tr-actions">
          <Link className="cn-button" to={block.to}>
            <ConsoleIcon name={blockIcons[alert.block.kind]} />
            {block.label}
          </Link>
          {alert.incidentId && (
            <Link className="cn-button" to={`/incidents/${encodeURIComponent(alert.incidentId)}`}>
              <ConsoleIcon name="incidents" />
              {alert.incidentKey ? `Incident ${alert.incidentKey}` : "Its incident"}
            </Link>
          )}
          {admin && (
            <Link className="cn-button" to="/settings?section=Rules">
              <ConsoleIcon name="settings" />
              Rule settings
            </Link>
          )}
        </div>
      </div>

      {error && (
        <p className="cn-error" role="alert">
          Could not refresh: {error}. Showing the alert as of {formatClock(alert.updatedAt)}.
        </p>
      )}

      <section className="sq-figures tr-figures" aria-label="Figures">
        {alertFigures(alert).map((figure) => (
          <SqlFigure
            key={figure.label}
            label={figure.label}
            value={figure.value}
            detail={figure.detail}
            tone={figure.tone}
            help={figure.help}
          />
        ))}
      </section>

      <div className="sq-row tr-main">
        <div className="tr-side">
          <Chart alert={alert} />
          <Guidance alert={alert} />
          {alert.serverErrors && alert.serverErrors.length > 0 && <ServerErrorList alert={alert} />}
          <Logs alert={alert} context={context.data} error={context.error} />
          {advanced && <Breakdowns alert={alert} />}
        </div>
        <div className="tr-side">
          {advanced ? (
            <Timeline alert={alert} />
          ) : (
            <p className="cn-empty al-note">
              The timeline is in the Advanced view, with the errors, routes, or disks behind the alert.
            </p>
          )}
          <Rule alert={alert} told={told} />
          <Affected context={context.data} />
        </div>
      </div>
    </main>
  );
}

/** The chart of the alert's measure, chosen by its kind: a disk's use and forecast, request durations, or errors. */
function Chart({ alert }: { alert: AlertDetails }) {
  if (alert.disk) {
    return (
      <section className="cn-card" aria-labelledby="al-chart">
        <header>
          <h2 id="al-chart">Used space and forecast · {alert.disk.mountpoint}</h2>
          <HelpTip label="Used space and forecast">
            The disk&apos;s share in use, from a week before the alert opened to a day after it resolved, or to now. The
            forecast runs on from now at the pace of the last day, to the day the disk is full.
          </HelpTip>
        </header>
        <div className="sq-chart">
          <DiskChart alert={alert} disk={alert.disk} />
          <ul className="alert-legend al-legend" aria-hidden="true">
            <li className="legend-disk">Used</li>
            <li className="legend-forecast">Forecast</li>
            <li className="legend-warning">Full</li>
          </ul>
        </div>
      </section>
    );
  }

  if (alert.latency) {
    return (
      <section className="cn-card" aria-labelledby="al-chart">
        <header>
          <h2 id="al-chart">Request duration · {alert.block.name}</h2>
          <HelpTip label="Request duration">
            How long the service took to answer HTTP requests, the median and the 95th percentile, with its requests
            below, from 30 minutes before the alert opened to 10 minutes after it resolved, or to now. The rule judges
            the p95 against the dashed lines.
          </HelpTip>
        </header>
        <div className="sq-chart">
          <LatencyChart alert={alert} latency={alert.latency} />
          <ul className="alert-legend al-legend" aria-hidden="true">
            <li className="legend-p95">p95</li>
            <li className="legend-p50">p50</li>
            <li className="legend-warning">warning {formatMeasure(alert.rule, alert.warningThreshold)}</li>
            <li className="legend-caution">caution {formatMeasure(alert.rule, alert.cautionThreshold)}</li>
            <li className="legend-records">requests</li>
          </ul>
        </div>
      </section>
    );
  }

  if (!isErrorRate(alert)) {
    return null;
  }

  return (
    <section className="cn-card" aria-labelledby="al-chart">
      <header>
        <h2 id="al-chart">Error rate · {Math.round(alert.windowSeconds / 60)}-minute window</h2>
        <HelpTip label="Error rate">
          The share of the service&apos;s log records at Error or worse over the rule&apos;s window, as the rule
          measured it, with its records below. It runs from 30 minutes before the alert opened to 10 minutes after it
          resolved, or to now; the dashed lines are the thresholds.
        </HelpTip>
      </header>
      <div className="sq-chart">
        <ErrorRateChart alert={alert} />
        <ul className="alert-legend al-legend" aria-hidden="true">
          <li className="legend-rate">error rate</li>
          <li className="legend-warning">warning {formatMeasure(alert.rule, alert.warningThreshold)}</li>
          <li className="legend-caution">caution {formatMeasure(alert.rule, alert.cautionThreshold)}</li>
          <li className="legend-records">records</li>
        </ul>
      </div>
    </section>
  );
}

/** What the alert means and how to fix it, in the rule's own words, with scripts to copy. */
function Guidance({ alert }: { alert: AlertDetails }) {
  const guidance = ruleGuidance(alert);

  if (!guidance) {
    return null;
  }

  return (
    <div className="al-guide">
      <section className="cn-card" aria-labelledby="al-means">
        <header>
          <h2 id="al-means">What it means</h2>
          <HelpTip label="What it means">
            Why this alert matters: what the rule saw and what happens if it goes on. The words come from the rule.
          </HelpTip>
        </header>
        <p className="sq-pad al-meaning">{guidance.meaning}</p>
      </section>
      <section className="cn-card" aria-labelledby="al-fix">
        <header>
          <h2 id="al-fix">How to fix</h2>
          <HelpTip label="How to fix">
            The steps to take, in order, with scripts ready to copy. Once the measure is back under the threshold, the
            alert resolves by itself.
          </HelpTip>
        </header>
        <ol className="sq-pad al-steps">
          {guidance.steps.map((step) => (
            <li key={step.text}>
              {step.text}
              {step.script && <CommandBlock label={step.text} text={step.script} />}
            </li>
          ))}
        </ol>
      </section>
    </div>
  );
}

/** The errors a database server's error alert counted, newest first. */
function ServerErrorList({ alert }: { alert: AlertDetails }) {
  const errors = alert.serverErrors ?? [];

  return (
    <section className="cn-card" aria-labelledby="al-server-errors">
      <header>
        <h2 id="al-server-errors">Errors counted</h2>
        <HelpTip label="Errors counted">
          The errors the rule counted on the server, newest first: SQL Server&apos;s error number, its severity, and
          what it said.
        </HelpTip>
      </header>
      <div className="cn-table-scroll">
        <table className="cn-table">
          <thead>
            <tr>
              <th scope="col">Time</th>
              <th scope="col" className="num">
                Error
              </th>
              <th scope="col" className="num wide">
                Severity
              </th>
              <th scope="col">Message</th>
            </tr>
          </thead>
          <tbody>
            {errors.map((one, index) => (
              <tr key={`${one.at}-${index}`}>
                <td className="mono">{formatClock(one.at)}</td>
                <td className="num">{one.error}</td>
                <td className="num wide">{one.severity}</td>
                <td>{one.message}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

/** Props for {@link Logs}. */
interface LogsProps {
  /** The alert. */
  alert: AlertDetails;
  /** What surrounds it, or null before it arrives. */
  context: AlertContext | null;
  /** Why the context could not be read, or null. */
  error: string | null;
}

/** The warnings and worse of the blocks the alert touches, around it, as the server picked them. */
function Logs({ alert, context, error }: LogsProps) {
  const logs = context?.logs;

  return (
    <section className="cn-card" aria-labelledby="al-logs">
      <header>
        <h2 id="al-logs">Logs around this</h2>
        <HelpTip label="Logs around this">
          Warnings and worse from the alert&apos;s block and the blocks related to it, from an hour before the alert
          opened until it closed or now, newest first. The server picks them, at most 50.
        </HelpTip>
        <span className="cn-meta">{alert.block.name} · from an hour before</span>
      </header>
      {!logs ? (
        <p className={error ? "cn-error sq-pad" : "cn-empty"}>
          {error ? `Could not read the logs: ${error}` : "Reading the logs…"}
        </p>
      ) : logs.items.length === 0 ? (
        <p className="cn-empty">No warning or error from these blocks around the alert.</p>
      ) : (
        <div className="cn-table-scroll">
          <table className="cn-table">
            <thead>
              <tr>
                <th scope="col">Time</th>
                <th scope="col">Level</th>
                <th scope="col" className="wide">
                  Source
                </th>
                <th scope="col">Message</th>
              </tr>
            </thead>
            <tbody>
              {logs.items.map((record) => (
                <tr key={record.id}>
                  <td className="mono">{formatTime(record.timestamp)}</td>
                  <td>
                    <span className={`cn-level ${record.severity.toLowerCase()}`}>{levelName[record.severity]}</span>
                  </td>
                  <td className="wide">{record.serviceName}</td>
                  <td className="al-message">{record.body}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {logs && context && (
        <footer>
          <span>
            {formatCount(logs.items.length)}
            {logs.total !== null ? ` of ${formatCount(logs.total)}` : ""}
          </span>
          {logs.query && (
            <Link
              className="link-button"
              to={`/logs?${new URLSearchParams({ q: logs.query, from: context.from, to: context.to }).toString()}`}
            >
              Open in Logs →
            </Link>
          )}
        </footer>
      )}
    </section>
  );
}

/** What is behind the alert, by kind: the most frequent errors, the slowest routes, or the host's disks. */
function Breakdowns({ alert }: { alert: AlertDetails }) {
  return (
    <>
      {alert.topErrors.length > 0 && (
        <section className="cn-card" aria-labelledby="al-top-errors">
          <header>
            <h2 id="al-top-errors">Most frequent errors</h2>
            <HelpTip label="Most frequent errors">
              The error messages the service logged most while the alert was open, with how many records have each and
              when the last one came.
            </HelpTip>
          </header>
          <div className="cn-table-scroll">
            <table className="cn-table">
              <thead>
                <tr>
                  <th scope="col">Message</th>
                  <th scope="col" className="num">
                    Records
                  </th>
                  <th scope="col" className="num wide">
                    Last
                  </th>
                </tr>
              </thead>
              <tbody>
                {alert.topErrors.map((group) => (
                  <tr key={group.message}>
                    <td className="al-message">{group.message}</td>
                    <td className="num">{formatCount(group.count)}</td>
                    <td className="num wide">{formatClock(group.last)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}
      {alert.latency && alert.latency.routes.length > 0 && (
        <section className="cn-card" aria-labelledby="al-routes">
          <header>
            <h2 id="al-routes">Slowest routes</h2>
            <HelpTip label="Slowest routes">
              The service&apos;s routes while the alert was open, slowest first as the server ranked them: requests per
              second, their 95th percentile, and the share answered with a 5xx status.
            </HelpTip>
          </header>
          <div className="cn-table-scroll">
            <table className="cn-table">
              <thead>
                <tr>
                  <th scope="col">Route</th>
                  <th scope="col" className="num wide">
                    Requests/s
                  </th>
                  <th scope="col" className="num">
                    p95
                  </th>
                  <th scope="col" className="num">
                    5xx
                  </th>
                </tr>
              </thead>
              <tbody>
                {alert.latency.routes.map((route) => (
                  <tr key={`${route.method ?? ""} ${route.route}`}>
                    <td className="mono">{[route.method, route.route].filter(Boolean).join(" ")}</td>
                    <td className="num wide">{formatRate(route.requestsPerSecond)}</td>
                    <td className="num">{formatSeconds(route.p95Seconds)}</td>
                    <td className="num">{formatShare(route.serverErrorRatio)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}
      {alert.disk && alert.disk.disks.length > 0 && (
        <section className="cn-card" aria-labelledby="al-disks">
          <header>
            <h2 id="al-disks">The host&apos;s disks</h2>
            <HelpTip label="The host's disks">
              Every disk of the host now, fullest first as the server sent them, and when each is full at the pace of
              its last day.
            </HelpTip>
          </header>
          <div className="cn-table-scroll">
            <table className="cn-table">
              <thead>
                <tr>
                  <th scope="col">Disk</th>
                  <th scope="col" className="num">
                    Used
                  </th>
                  <th scope="col" className="num">
                    Full in
                  </th>
                </tr>
              </thead>
              <tbody>
                {alert.disk.disks.map((disk) => (
                  <tr key={disk.mountpoint}>
                    <td className="mono">{disk.mountpoint}</td>
                    <td className="num">{formatDiskShare(disk.ratio)}</td>
                    <td className="num">
                      {disk.daysUntilFull === null ? "not filling" : formatDays(disk.daysUntilFull)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}
    </>
  );
}

/** Every change of the alert, oldest first as the server sent them, and the messages each sent. */
function Timeline({ alert }: { alert: AlertDetails }) {
  return (
    <section className="cn-card" aria-labelledby="al-timeline">
      <header>
        <h2 id="al-timeline">Timeline</h2>
        <HelpTip label="Timeline">
          Every change of the alert, oldest first: when it opened, rose to warning, eased to caution, and resolved, and
          under each the messages sent to channels and whether they went through.
        </HelpTip>
      </header>
      <ol className="in-timeline">
        {alert.events.flatMap((event) => [
          <li key={`${event.kind}-${event.at}`}>
            <span className="mono">{formatClock(event.at)}</span>
            <i
              className={event.kind === "Resolved" ? "ok" : event.severity === "Warning" ? "bad" : "warn"}
              aria-hidden="true"
            />
            <div>
              <b>{eventTitle(event)}</b>
              <small>{formatMeasure(alert.rule, event.value)}</small>
            </div>
            <span
              className={
                event.kind === "Resolved" ? "cn-pill ok" : event.severity === "Warning" ? "cn-pill bad" : "cn-pill warn"
              }
            >
              {event.kind === "Resolved" ? "Resolved" : event.severity}
            </span>
          </li>,
          ...event.deliveries.map((delivery) => {
            const tag = deliveryTag(delivery);

            return (
              <li key={`${event.kind}-${event.at}-${delivery.channelId}`}>
                <span className="mono">{formatClock(delivery.sentAt ?? delivery.lastAttemptAt ?? event.at)}</span>
                <i className={tag.tone === "bad" ? "bad" : "info"} aria-hidden="true" />
                <div>
                  <b>
                    {delivery.channelKind === "Email" ? "Email" : "Webhook"} to {delivery.channelName}
                  </b>
                  {delivery.lastError && <small>{delivery.lastError}</small>}
                </div>
                <span className={tag.tone === "bad" ? "cn-pill bad" : tag.tone === "ok" ? "cn-pill ok" : "cn-pill off"}>
                  {tag.text}
                </span>
              </li>
            );
          }),
        ])}
      </ol>
    </section>
  );
}

/** The rule behind the alert, as this installation sets it. */
function Rule({ alert, told }: { alert: AlertDetails; told: string[] }) {
  return (
    <section className="cn-card" aria-labelledby="al-rule">
      <header>
        <h2 id="al-rule">Rule</h2>
        <HelpTip label="Rule">
          The rule that raised the alert, with its caution and warning levels as this installation sets them, how far
          back it looks, and the channels it told.
        </HelpTip>
      </header>
      <dl className="sq-pad tr-kv">
        <dt>Rule</dt>
        <dd>{ruleName(alert.rule)}</dd>
        <dt>Caution when</dt>
        <dd>{formatThreshold(alert.rule, alert.cautionThreshold)}</dd>
        <dt>Warning when</dt>
        <dd>{formatThreshold(alert.rule, alert.warningThreshold)}</dd>
        <dt>Looks back</dt>
        <dd>{alert.windowSeconds > 0 ? formatDuration(alert.windowSeconds * 1000) : "its last reading"}</dd>
        <dt>Applies to</dt>
        <dd>every {alert.block.kind}</dd>
        <dt>Notifies</dt>
        <dd>{told.length === 0 ? "no channel" : told.join(", ")}</dd>
      </dl>
      <footer>
        <span>Admins change it in Settings → Alert rules</span>
      </footer>
    </section>
  );
}

/** The blocks the alert touches, with their state now, from the alert's context. */
function Affected({ context }: { context: AlertContext | null }) {
  return (
    <section className="cn-card" aria-labelledby="al-affected">
      <header>
        <h2 id="al-affected">Affected</h2>
        <HelpTip label="Affected">
          The alert&apos;s block and the blocks related to it, such as the host it runs on or the database it calls,
          each with its state now, from the open alerts.
        </HelpTip>
      </header>
      {!context ? (
        <p className="cn-empty">Reading…</p>
      ) : (
        <div className="cn-table-scroll">
          <table className="cn-table">
            <thead>
              <tr>
                <th scope="col">Block</th>
                <th scope="col">State</th>
              </tr>
            </thead>
            <tbody>
              {context.components.map((component) => {
                const link = blockLink(component.block);

                return (
                  <tr key={component.block.id}>
                    <td>
                      <Link className="cn-table-name" to={link.to}>
                        {component.block.name}
                      </Link>
                      <span className="cn-table-sub">
                        {component.block.kind}
                        {component.relation === "host"
                          ? " · its host"
                          : component.relation === "runs-on-host"
                            ? " · runs on the host"
                            : ""}
                      </span>
                    </td>
                    <td>
                      <span
                        className={
                          component.health === "warning"
                            ? "cn-pill bad"
                            : component.health === "caution"
                              ? "cn-pill warn"
                              : component.health === "silent"
                                ? "cn-pill off"
                                : "cn-pill ok"
                        }
                      >
                        {component.health === "warning"
                          ? "Warning"
                          : component.health === "caution"
                            ? "Caution"
                            : component.health === "silent"
                              ? "Silent"
                              : "Healthy"}
                      </span>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
