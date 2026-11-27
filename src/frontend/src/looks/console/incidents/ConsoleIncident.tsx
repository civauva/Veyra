import { Link, useParams } from "react-router";

import { ruleGuidance } from "../../../alerts/ruleGuidance";
import { alertTitle, formatMeasure, formatThreshold, ruleName } from "../../../alerts/ruleWords";
import type { IncidentChart, IncidentContext, IncidentDetails } from "../../../api/types";
import { formatBytes } from "../../../home/homeFormat";
import { momentWords, useMayAcknowledge } from "../../../incidents/incidentWords";
import { useIncident } from "../../../incidents/useIncidents";
import { formatClock, formatTime } from "../../../logs/format";
import { levelName } from "../../../logs/levelWords";
import { LineChart } from "../../../metrics/LineChart";
import { HelpTip } from "../../../ui/HelpTip";
import { ScreenBar } from "../../../ui/ScreenBar";
import { SqlFigure, stepWords, timeWords } from "../database/sqlParts";
import "./consoleIncidents.css";

/** One moment of the merged timeline. */
interface Moment {
  at: string;
  title: string;
  detail: string;
  tone: "bad" | "warn" | "ok" | "info";
  tag: string;
}

/**
 * Console's incident (VEY-76, reference screenshot 7, ADR 0026): its state with Acknowledge and the runbook, the
 * figures, the execution path that failed and its exception, one timeline across signals, the charts and logs of the
 * affected blocks, and what to check, from the rules' own guidance.
 */
export function ConsoleIncident() {
  const { id = "" } = useParams();
  const view = useIncident(id);
  const mayAcknowledge = useMayAcknowledge();
  const details = view.details;

  if (view.notFound) {
    return (
      <main className="cn-screen in">
        <ScreenBar trail={[{ label: "Incidents", to: "/incidents" }, { label: "Not found" }]} />
        <p className="cn-empty">This incident does not exist, or it was deleted with its alerts after 90 days.</p>
      </main>
    );
  }

  if (!details) {
    return (
      <main className="cn-screen in">
        <ScreenBar trail={[{ label: "Incidents", to: "/incidents" }, { label: "…" }]} />
        <p className="cn-empty">
          {view.error ? `Could not read the incident: ${view.error}` : "Reading the incident…"}
        </p>
      </main>
    );
  }

  const { incident, figures } = details;
  const leading = details.alerts.find((alert) => alert.id === figures.leading.alertId) ?? details.alerts[0];
  const runbook = details.runbooks.find((one) => one.rule === figures.leading.rule) ?? details.runbooks[0];
  const ongoing = incident.state !== "resolved";

  return (
    <main className="cn-screen in">
      <ScreenBar trail={[{ label: "Incidents", to: "/incidents" }, { label: incident.key }]} />
      <div className="cn-head">
        <div>
          <h1>
            {incident.title}
            <span
              className={!ongoing ? "cn-pill off" : incident.severity === "Warning" ? "cn-pill bad" : "cn-pill warn"}
            >
              {ongoing
                ? `${incident.severity} · ${incident.state === "acknowledged" ? "acknowledged" : "ongoing"}`
                : "Resolved"}
            </span>
          </h1>
          <p>
            <span className="mono">{incident.key}</span> · {incident.alertCount}{" "}
            {incident.alertCount === 1 ? "alert" : "alerts"} on {incident.blocks.map((block) => block.name).join(", ")}{" "}
            · opened {formatClock(incident.openedAt)}
            {incident.acknowledgedBy ? ` · acknowledged by ${incident.acknowledgedBy}` : ""}
          </p>
        </div>
        <div className="cn-head-actions">
          {mayAcknowledge && ongoing && (
            <button
              type="button"
              className="cn-button"
              disabled={view.saving}
              onClick={() => view.setAcknowledged(incident.state !== "acknowledged")}
            >
              {incident.state === "acknowledged" ? "Take back" : "Acknowledge"}
            </button>
          )}
          {runbook ? (
            <a className="cn-button primary" href={runbook.url} target="_blank" rel="noreferrer noopener">
              View runbook →
            </a>
          ) : (
            <a className="cn-button primary" href="#in-investigation">
              How to fix →
            </a>
          )}
        </div>
      </div>
      {view.error && (
        <p className="cn-error" role="alert">
          {view.error}
        </p>
      )}

      <section className="sq-figures in-figures" aria-label="Figures">
        <SqlFigure
          label="First detected"
          value={formatClock(figures.firstDetectedAt)}
          detail={`${timeWords(figures.durationSeconds)} ${ongoing ? "ago" : "long"}`}
          help="When the first alert of the incident opened, and how long the incident has lasted."
        />
        {leading && (
          <SqlFigure
            label={ruleName(figures.leading.rule)}
            value={formatMeasure(figures.leading.rule, figures.leading.value)}
            detail={`threshold ${formatThreshold(figures.leading.rule, figures.leading.threshold)} · peak ${formatMeasure(figures.leading.rule, figures.leading.peak)}`}
            tone={incident.severity === "Warning" && ongoing ? "bad" : ongoing ? "warn" : undefined}
            help="The measure of the incident's leading alert now, against the threshold it crossed, and its worst value."
          />
        )}
        <SqlFigure
          label="Affected"
          value={incident.blocks[0]?.name ?? "—"}
          detail={figures.affectedBlocks > 1 ? `and ${figures.affectedBlocks - 1} more` : incident.blocks[0]?.kind}
          help="The blocks the incident's alerts are about: services, hosts, containers, and database servers."
        />
        <SqlFigure
          label="Alerts"
          value={incident.alertCount}
          detail={`${details.alerts.filter((alert) => alert.resolvedAt === null).length} still open`}
          help="Alerts grouped into this incident because they hit related blocks within minutes of each other."
        />
        <SqlFigure
          label="Duration"
          value={timeWords(figures.durationSeconds)}
          detail={ongoing ? "and counting" : `resolved ${formatClock(incident.resolvedAt ?? incident.openedAt)}`}
          help="From the first alert opening to the last one resolving, or to now."
        />
        <SqlFigure
          label="Notified"
          value={new Set(details.timeline.flatMap((moment) => moment.deliveries.map((one) => one.channelName))).size}
          detail="channels told"
          help="The notification channels told about the incident's alerts. Each delivery shows on the timeline."
        />
      </section>

      <div className="sq-row sq-row-main">
        <PathAndException context={view.context} />
        <Timeline details={details} context={view.context} />
      </div>

      {view.context && view.context.charts.length > 0 && (
        <div className="in-charts">
          {view.context.charts.map((chart, index) => (
            <ChartCard key={index} chart={chart} />
          ))}
        </div>
      )}

      <div className="sq-row sq-row-main">
        <Logs context={view.context} />
        <Investigation details={details} context={view.context} />
      </div>
    </main>
  );
}

/** The execution path that failed, and the exception behind it. */
function PathAndException({ context }: { context: IncidentContext | null }) {
  const path = context?.executionPath ?? null;
  const exception = context?.exception ?? null;

  return (
    <section className="cn-card" aria-labelledby="in-path">
      <header>
        <h2 id="in-path">Execution path</h2>
        <HelpTip label="Execution path">
          The steps of a failing trace&apos;s critical path, or of the failing job run, in the order they ran, with how
          long each took and how it ended. The exception below is what the failing step threw.
        </HelpTip>
        {path?.traceId && (
          <Link className="cn-meta" to={`/traces/${path.traceId}`}>
            Open the trace →
          </Link>
        )}
      </header>
      <div className="sq-pad">
        {!context ? (
          <p className="sq-muted">Reading what surrounds the incident…</p>
        ) : !path || path.steps.length === 0 ? (
          <p className="sq-muted">No trace or job run of the window shows the failing path.</p>
        ) : (
          <ol className="in-path">
            {path.steps.map((step, index) => (
              <li key={index} className={step.status === "error" ? "bad" : step.status === "ok" ? "ok" : undefined}>
                <b>
                  {step.status === "error" ? "× " : step.status === "ok" ? "✓ " : ""}
                  {step.name}
                </b>
                <span>
                  {step.durationMs === null ? "" : stepWords(step.durationMs)}
                  {step.detail ? ` · ${step.detail}` : ""}
                </span>
                {step.service && <small>{step.service}</small>}
              </li>
            ))}
          </ol>
        )}
        {exception && (
          <>
            <h3 className="cn-section">Observed exception</h3>
            <pre className="cn-code">
              {exception.type && <span className="cn-code-bad">{exception.type}</span>}
              {exception.type ? ": " : ""}
              {exception.message}
              {exception.stackTrace ? `\n${exception.stackTrace}` : ""}
            </pre>
          </>
        )}
      </div>
    </section>
  );
}

/** The incident's own moments and those of other signals, in one timeline. */
function Timeline({ details, context }: { details: IncidentDetails; context: IncidentContext | null }) {
  const moments: Moment[] = [
    ...details.timeline.flatMap((moment): Moment[] => {
      const title = momentWords(moment);
      const subject =
        moment.rule && moment.block
          ? alertTitle(moment.rule, moment.block, moment.value ?? 0, moment.kind === "resolved")
          : "";
      const own: Moment = {
        at: moment.at,
        title,
        detail: moment.actor ? `by ${moment.actor}` : subject,
        tone:
          moment.kind === "resolved" || moment.kind === "acknowledged"
            ? "ok"
            : moment.severity === "Warning"
              ? "bad"
              : moment.kind === "unacknowledged"
                ? "info"
                : "warn",
        tag: moment.kind === "acknowledged" ? "Ack" : (moment.severity ?? "Info"),
      };
      const told = moment.deliveries.map((delivery): Moment => ({
        at: delivery.sentAt ?? delivery.lastAttemptAt ?? moment.at,
        title: delivery.state === "Sent" ? "Notification sent" : "Notification not sent yet",
        detail: delivery.channelName,
        tone: delivery.state === "Failed" ? "bad" : "info",
        tag: delivery.state,
      }));

      return [own, ...told];
    }),
    ...(context?.timeline ?? []).map((signal): Moment => ({
      at: signal.at,
      title: signal.title,
      detail: [signal.block.name, signal.detail].filter(Boolean).join(" · "),
      tone: signal.kind === "config-change" ? "info" : "warn",
      tag:
        signal.kind === "job-failed"
          ? "Job"
          : signal.kind === "config-change"
            ? "Change"
            : signal.kind === "deadlock"
              ? "Deadlock"
              : "Error",
    })),
  ].sort((a, b) => Date.parse(a.at) - Date.parse(b.at));

  return (
    <section className="cn-card" aria-labelledby="in-timeline">
      <header>
        <h2 id="in-timeline">Incident timeline</h2>
        <HelpTip label="Incident timeline">
          The alerts of the incident opening, worsening, and resolving, the notifications they sent, who acknowledged
          it, and what else happened on the affected blocks: failed jobs, configuration changes, deadlocks, and the
          first error of each service.
        </HelpTip>
      </header>
      <ol className="in-timeline">
        {moments.map((moment, index) => (
          <li key={index}>
            <span className="mono">{formatClock(moment.at)}</span>
            <i className={moment.tone} aria-hidden="true" />
            <div>
              <b>{moment.title}</b>
              {moment.detail && <small>{moment.detail}</small>}
            </div>
            <span className={`cn-pill ${moment.tone === "info" ? "info" : moment.tone}`}>{moment.tag}</span>
          </li>
        ))}
      </ol>
    </section>
  );
}

/** One chart of an affected block. */
function ChartCard({ chart }: { chart: IncidentChart }) {
  const format = (value: number) =>
    chart.unit === "ratio"
      ? `${(value * 100).toFixed(1)}%`
      : chart.unit === "percent"
        ? `${Math.round(value)}%`
        : chart.unit === "ms"
          ? `${Math.round(value)} ms`
          : chart.unit === "bytes"
            ? formatBytes(value)
            : Math.round(value).toLocaleString("en");
  const colors = ["var(--accent)", "var(--info-fg)", "var(--warning-fg)", "var(--error-fg)"];

  return (
    <section className="cn-card" aria-label={`${chart.title}, ${chart.block.name}`}>
      <header>
        <h2>{chart.title}</h2>
        <HelpTip label={chart.title}>
          {chart.title} of {chart.block.name} around the incident, from 30 minutes before it opened.
        </HelpTip>
        <span className="cn-meta">{chart.block.name}</span>
      </header>
      <div className="sq-chart">
        <LineChart
          title={`${chart.title}, ${chart.block.name}`}
          format={format}
          height={170}
          series={chart.lines.map((line, index) => ({
            line,
            color: colors[index] ?? "var(--faint)",
            filled: index === 0,
          }))}
        />
      </div>
    </section>
  );
}

/** Warnings and errors from the affected blocks around the incident. */
function Logs({ context }: { context: IncidentContext | null }) {
  const logs = context?.logs;

  return (
    <section className="cn-card" aria-labelledby="in-logs">
      <header>
        <h2 id="in-logs">Correlated logs</h2>
        <HelpTip label="Correlated logs">
          Warnings and errors from the affected blocks, from 30 minutes before the incident opened until it resolved,
          newest first. The server picks them.
        </HelpTip>
        {logs && (
          <span className="cn-meta">
            {logs.items.length}
            {logs.total !== null ? ` of ${logs.total.toLocaleString("en")}` : ""} warnings and errors
          </span>
        )}
      </header>
      {!logs ? (
        <p className="cn-empty">Reading the logs…</p>
      ) : logs.items.length === 0 ? (
        <p className="cn-empty">No warning or error from the affected blocks in the window.</p>
      ) : (
        <div className="cn-table-scroll">
          <table className="cn-table">
            <thead>
              <tr>
                <th scope="col">Time</th>
                <th scope="col">Level</th>
                <th scope="col">Service</th>
                <th scope="col">Message</th>
                <th scope="col" className="wide">
                  Trace
                </th>
              </tr>
            </thead>
            <tbody>
              {logs.items.map((record) => (
                <tr key={record.id}>
                  <td className="mono">{formatTime(record.timestamp)}</td>
                  <td>
                    <span className={`cn-level ${record.severity.toLowerCase()}`}>{levelName[record.severity]}</span>
                  </td>
                  <td>{record.serviceName}</td>
                  <td className="in-message">{record.body}</td>
                  <td className="wide mono">
                    {record.traceId ? <Link to={`/traces/${record.traceId}`}>{record.traceId.slice(0, 8)}</Link> : "—"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {logs && logs.query && context && (
        <footer>
          <span>From the affected blocks</span>
          <Link
            to={`/logs?${new URLSearchParams({ q: logs.query, from: context.from, to: context.to }).toString()}`}
            className="link-button"
          >
            Open in Logs →
          </Link>
        </footer>
      )}
    </section>
  );
}

/** What to check, from the rules' guidance, and the affected components with their state. */
function Investigation({ details, context }: { details: IncidentDetails; context: IncidentContext | null }) {
  const seen = new Set<string>();
  const guided = details.alerts.filter((alert) => {
    const key = `${alert.rule}#${alert.block.id}`;

    if (seen.has(key)) {
      return false;
    }

    seen.add(key);

    return true;
  });

  return (
    <section className="cn-card" id="in-investigation" aria-labelledby="in-investigation-title">
      <header>
        <h2 id="in-investigation-title">Investigation context</h2>
        <HelpTip label="Investigation context">
          What each alert of the incident usually means and what to check, from the rule&apos;s own guidance. They are
          possible causes, not findings: Veyra has not confirmed them.
        </HelpTip>
      </header>
      <div className="sq-pad">
        <p className="sq-muted sq-small">Possible causes · not confirmed</p>
        {guided.map((alert) => {
          const guidance = ruleGuidance(alert);

          return guidance ? (
            <div key={alert.id} className="in-guidance">
              <b>{ruleName(alert.rule)}</b>
              <small>{guidance.meaning}</small>
              <ol>
                {guidance.steps.slice(0, 4).map((step, index) => (
                  <li key={index}>
                    {step.text}
                    {step.script && <pre className="cn-code">{step.script}</pre>}
                  </li>
                ))}
              </ol>
            </div>
          ) : null;
        })}
        <h3 className="cn-section">Affected components</h3>
        {!context ? (
          <p className="sq-muted">Reading…</p>
        ) : (
          <dl className="in-components">
            {context.components.map((component) => (
              <div key={component.block.id}>
                <dt>
                  {component.block.name}
                  <small>
                    {component.block.kind}
                    {component.relation === "host"
                      ? " · its host"
                      : component.relation === "runs-on-host"
                        ? " · runs on the host"
                        : ""}
                  </small>
                </dt>
                <dd>
                  <span
                    className={
                      component.inIncident && component.health === "warning"
                        ? "cn-pill bad"
                        : component.inIncident || component.health === "caution"
                          ? "cn-pill warn"
                          : component.health === "silent"
                            ? "cn-pill off"
                            : "cn-pill ok"
                    }
                  >
                    {component.inIncident
                      ? component.health === "warning"
                        ? "Failing"
                        : "Investigate"
                      : component.health === "silent"
                        ? "Silent"
                        : component.health === "ok"
                          ? "Online"
                          : "Caution"}
                  </span>
                </dd>
              </div>
            ))}
          </dl>
        )}
      </div>
    </section>
  );
}
