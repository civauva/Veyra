import { useEffect, type ReactNode } from "react";
import { Link, useParams } from "react-router";

import type { AlertDetails } from "../api/types";
import { ScreenBar } from "../ui/ScreenBar";
import { useShell } from "../app/shellContext";
import { formatBytes, severityWord } from "../home/homeFormat";
import { WeatherIcon } from "../home/WeatherIcon";
import { formatClock, formatCount } from "../logs/format";
import { defaultFilters, filtersToUrl, type TimeRange } from "../logs/logFilters";
import { HelpTip } from "../ui/HelpTip";
import { alertScene, alertStory, atMoment, formatDuration, headline, isErrorRate, stateLine } from "./alertFormat";
import { AlertGuidance } from "./AlertGuidance";
import { AlertTimeline } from "./AlertTimeline";
import { DiskChart } from "./DiskChart";
import { ErrorRateChart } from "./ErrorRateChart";
import { HostDisks } from "./HostDisks";
import { LatencyChart } from "./LatencyChart";
import {
  containerPath,
  databasePath,
  formatDays,
  formatDiskShare,
  formatMeasure,
  formatThreshold,
  ruleMeasures,
  ruleName,
  ruleSentence,
  serverName,
} from "./ruleWords";
import { ServerErrors } from "./ServerErrors";
import { SlowRoutes } from "./SlowRoutes";
import { TopErrors } from "./TopErrors";
import { useAlertDetails } from "./useAlertDetails";
import { localDay, shortDate, zoneLabel } from "../time/zone";
import { defaultTraceFilters, filtersToSearch, type TraceShow } from "../traces/traceFilters";
import "../home/home.css";
import "./alert.css";
import { useViewMode } from "../ui/ViewToggle";

/** Where the rest of an alert is in the Simple view. */
const advancedNote =
  "Timeline, messages, and the most frequent errors are in the Advanced view, with the host's disks or the " +
  "slowest routes where the rule has them.";

/** Where the rest of a database server's alert is in the Simple view. */
const databaseNote =
  "The timeline and who was told are in the Advanced view; the server's queries, databases, and jobs are on its " +
  "screen.";

/**
 * One alert, reached from the Alerts screen, from Needs you on the home, and from the link in every notification:
 * what it says, how its measure moved, who was told, and where to look next. An error-rate alert shows its errors, a
 * disk alert the disk's week and forecast, a latency alert the request durations and the slowest routes. It paints
 * the alert's weather behind the shell, and clear skies once it resolved.
 */
export function AlertScreen() {
  const [mode] = useViewMode();
  const { id = "" } = useParams();
  const { setScene } = useShell();
  const { alert, notFound, error } = useAlertDetails(id);
  const scene = alert ? alertScene(alert) : null;

  useEffect(() => {
    setScene(scene);
  }, [scene, setScene]);

  useEffect(() => () => setScene(null), [setScene]);

  if (!alert) {
    return (
      <main className="home alert-screen">
        <ScreenBar trail={[{ label: "Alerts", to: "/alerts" }, { label: "Alert" }]} view />
        <div className="alert-page">
          {notFound ? (
            <p className="home-message">
              There is no such alert. Resolved alerts are kept for 90 days, so it may have been older than that.
            </p>
          ) : error ? (
            <p className="home-message" role="alert">
              The alert could not be loaded: {error}
            </p>
          ) : (
            <p className="home-message">Loading the alert…</p>
          )}
        </div>
      </main>
    );
  }

  return (
    <main className="home alert-screen">
      <ScreenBar trail={[{ label: "Alerts", to: "/alerts" }, { label: ruleName(alert.rule) }]} view />
      <div className="alert-page">
        {error && (
          <p className="home-stale" role="alert">
            Could not refresh: {error}. Showing the alert as of {formatClock(alert.updatedAt)}{" "}
            {zoneLabel(new Date(alert.updatedAt))}.
          </p>
        )}
        <AlertHero alert={alert} />
        <AlertFacts alert={alert} />
        <AlertGuidance alert={alert} />
        {alert.serverErrors && <ServerErrors alert={alert} errors={alert.serverErrors} />}
        <AlertChart alert={alert} />
        {mode === "advanced" ? (
          <div className="alert-lower">
            <AlertTimeline alert={alert} />
            {isErrorRate(alert) && <TopErrors alert={alert} />}
            {alert.disk && <HostDisks alert={alert} disk={alert.disk} />}
            {alert.latency && <SlowRoutes latency={alert.latency} />}
          </div>
        ) : (
          <p className="fine alert-advanced-note">{alert.block.kind === "database" ? databaseNote : advancedNote}</p>
        )}
      </div>
    </main>
  );
}

/** The weather, the state, the title, the story, and the ways onward. */
function AlertHero({ alert }: { alert: AlertDetails }) {
  const open = alert.resolvedAt === null;
  const scene = alertScene(alert);
  // Ages are counted to the last measurement, which is at most one evaluation old, so the render stays pure.
  const measured = new Date(alert.updatedAt);

  return (
    <section className="alert-hero" aria-labelledby="alert-title">
      <WeatherIcon kind={scene === "storm" ? "storm" : scene === "showers" ? "shower" : "sun"} size={104} />
      <div className="alert-hero-text">
        <span className={open ? `state-chip open ${alert.severity.toLowerCase()}` : "state-chip resolved"}>
          <i aria-hidden="true" />
          {stateLine(alert, measured)}
        </span>
        <h1 id="alert-title">{headline(alert)}</h1>
        <p className="say">{alertStory(alert)}</p>
      </div>
      <div className="alert-actions">
        <Onward alert={alert} />
      </div>
    </section>
  );
}

/**
 * Where to look next: a service's error logs and failed traces, a slow service's metrics and slow traces, a host's
 * disks, a database server's queries, databases, and jobs.
 */
function Onward({ alert }: { alert: AlertDetails }) {
  const name = alert.block.name;

  if (alert.block.kind === "database") {
    return (
      <Link className="step-button primary" to={databasePath(name)}>
        Open {serverName(name)}
      </Link>
    );
  }

  if (alert.block.kind === "container") {
    const around = filtersToUrl({ ...defaultFilters, services: [name], around: alert.openedAt });

    return (
      <>
        <Link className="step-button primary" to={containerPath(alert.block)}>
          Open {alert.block.part ? `${alert.block.part}'s containers` : "Metrics"}
        </Link>
        <Link className="step-button" to={`/logs?${around}`}>
          Its logs ± 1 minute around {formatClock(alert.openedAt)}
        </Link>
      </>
    );
  }

  if (alert.block.kind === "host") {
    return (
      <Link className="step-button primary" to={`/metrics/hosts/${encodeURIComponent(name)}`}>
        Open {name}&apos;s metrics
      </Link>
    );
  }

  const age = new Date(alert.updatedAt).getTime() - new Date(alert.openedAt).getTime();
  const range: TimeRange = age < 50 * 60_000 ? "1h" : age < 5.5 * 3_600_000 ? "6h" : "24h";
  const around = filtersToUrl({ ...defaultFilters, services: [name], around: alert.openedAt });

  if (alert.rule === "latency-p95") {
    return (
      <>
        <Link className="step-button primary" to={`/metrics/services/${encodeURIComponent(name)}`}>
          Open {name}&apos;s metrics
        </Link>
        <Link className="step-button" to={tracesOf(alert, "slow")}>
          Its slow traces
        </Link>
        <Link className="step-button" to={`/logs?${around}`}>
          Its logs ± 1 minute around {formatClock(alert.openedAt)}
        </Link>
      </>
    );
  }

  const logs = filtersToUrl({ ...defaultFilters, range, minimumSeverity: "Error", services: [name] });

  return (
    <>
      <Link className="step-button primary" to={`/logs?${logs}`}>
        Open its error logs
      </Link>
      <Link className="step-button" to={tracesOf(alert, "failed")}>
        Its failed traces
      </Link>
      <Link className="step-button" to={`/logs?${around}`}>
        ± 1 minute around {formatClock(alert.openedAt)}
      </Link>
    </>
  );
}

/**
 * The service's failed or slow traces over the alert's window, from ten minutes before it opened, so the start of
 * the trouble is in view, to five minutes after its last check or its end (ADR 0014).
 */
function tracesOf(alert: AlertDetails, show: TraceShow): string {
  const from = new Date(new Date(alert.openedAt).getTime() - 10 * 60_000);
  const to = new Date(new Date(alert.resolvedAt ?? alert.updatedAt).getTime() + 5 * 60_000);
  const search = filtersToSearch({
    ...defaultTraceFilters,
    show,
    services: [alert.block.name],
    from: from.toISOString(),
    to: to.toISOString(),
  });

  return `/traces?${search}`;
}

/** Five numbers: now, the rule's own figure, the threshold, how long, and who was told. */
function AlertFacts({ alert }: { alert: AlertDetails }) {
  // The server lists each channel told once (VEY-68).
  const told = alert.told.map((channel) => channel.name);
  const other = alert.severity === "Warning" ? "caution" : "warning";
  const otherThreshold = alert.severity === "Warning" ? alert.cautionThreshold : alert.warningThreshold;
  const clears = `${alert.lowerIsWorse ? "rises back above" : "falls back below"} ${formatMeasure(alert.rule, alert.clearThreshold)}`;

  return (
    <dl className="alert-facts">
      <FirstFacts alert={alert} />
      <div className="glass fact">
        <FactName name="Threshold">
          {ruleSentence(alert.rule, alert.severity, alert.threshold, alert.windowSeconds)} Admins change the levels on
          the Settings screen.
        </FactName>
        <dd className="fact-value">
          {formatMeasure(alert.rule, alert.threshold)} <small>{severityWord(alert.severity)}</small>
        </dd>
        <dd className="fact-note">
          {other} {alert.rule === "disk-filling" ? "" : "from "}
          {formatThreshold(alert.rule, otherThreshold)}
        </dd>
      </div>
      <div className="glass fact">
        {alert.resolvedAt ? (
          <FactName name="Lasted">How long the alert was open, from when it opened to when it resolved.</FactName>
        ) : (
          <FactName name="Since">
            When the alert opened, on the day under it, in your time zone. It resolves by itself once the measure{" "}
            {clears}.
          </FactName>
        )}
        <dd className="fact-value">
          {alert.resolvedAt
            ? formatDuration(new Date(alert.resolvedAt).getTime() - new Date(alert.openedAt).getTime())
            : formatClock(alert.openedAt)}
        </dd>
        <dd className="fact-note">
          {alert.resolvedAt
            ? `${formatClock(alert.openedAt)}–${formatClock(alert.resolvedAt)} ${zoneLabel(new Date(alert.openedAt))}`
            : `${localDay(alert.openedAt)} · ${zoneLabel(new Date(alert.openedAt))}`}
        </dd>
      </div>
      <div className="glass fact">
        <FactName name="Notified">
          How many notification channels were sent this alert, and which. Admins add channels, such as email or a
          webhook, on the Notifications screen.
        </FactName>
        <dd className="fact-value">{told.length}</dd>
        <dd className="fact-note">{told.length === 0 ? "no channel" : told.join(", ")}</dd>
      </div>
    </dl>
  );
}

/** The first two numbers, which differ per rule. */
function FirstFacts({ alert }: { alert: AlertDetails }) {
  const minutes = Math.round(alert.windowSeconds / 60);
  const peak = (
    <div className="glass fact">
      <FactName name="Peak">
        The worst the measure has been while the alert was open{alert.lowerIsWorse ? ", here its lowest" : ""}, and when
        it was measured.
      </FactName>
      <dd className="fact-value">{formatMeasure(alert.rule, alert.peak)}</dd>
      <dd className="fact-note">{alert.peakAt ? atMoment(alert.peakAt, alert.updatedAt) : "time not recorded"}</dd>
    </div>
  );

  if (alert.disk) {
    const { disk } = alert;
    const growth =
      disk.ratio !== null && disk.daysUntilFull !== null && disk.daysUntilFull > 0
        ? ((1 - disk.ratio) / disk.daysUntilFull) * 100
        : null;

    return (
      <>
        <div className="glass fact">
          <FactName name="Full in">When the disk is full if it keeps filling at the pace of its last day.</FactName>
          <dd className="fact-value">{disk.daysUntilFull === null ? "not filling" : formatDays(disk.daysUntilFull)}</dd>
          <dd className="fact-note">
            {disk.daysUntilFull === null
              ? "at the pace of the last day"
              : `on ${fullDay(alert.updatedAt, disk.daysUntilFull)} at this pace`}
          </dd>
        </div>
        <div className="glass fact">
          <FactName name="In use">
            How much of the disk is in use now, of its size, and by how many percentage points a day it grows.
          </FactName>
          <dd className="fact-value">{disk.ratio === null ? "—" : formatDiskShare(disk.ratio)}</dd>
          <dd className="fact-note">
            {disk.usedBytes !== null && disk.totalBytes !== null
              ? `${formatBytes(disk.usedBytes)} of ${formatBytes(disk.totalBytes)}`
              : "not reported now"}
            {growth !== null ? ` · ${Number(growth.toFixed(1))} pt a day` : ""}
          </dd>
        </div>
      </>
    );
  }

  if (alert.latency) {
    const last = alert.latency.points.findLast((point) => point.requests > 0);

    return (
      <>
        <div className="glass fact">
          <NowName alert={alert} />
          <dd className="fact-value">{formatMeasure(alert.rule, alert.value)}</dd>
          <dd className="fact-note">
            p95 over the last {minutes} min{last ? ` · ${formatCount(last.requests)} requests a minute` : ""}
          </dd>
        </div>
        {peak}
      </>
    );
  }

  const last = alert.series?.points.at(-1);

  return (
    <>
      <div className="glass fact">
        <NowName alert={alert} />
        <dd className="fact-value">{formatMeasure(alert.rule, alert.value)}</dd>
        <dd className="fact-note">
          {last && last.windowTotal > 0
            ? `${formatCount(last.windowErrors)} of ${formatCount(last.windowTotal)} records, last ${minutes} min`
            : minutes > 0
              ? `Over the last ${minutes} min`
              : `last checked ${atMoment(alert.updatedAt, alert.updatedAt)}`}
        </dd>
      </div>
      {peak}
    </>
  );
}

/** The name of a fact, with a question mark that explains it (VEY-60). */
function FactName({ name, children }: { name: string; children: ReactNode }) {
  return (
    <dt className="fact-name">
      <span className="cap">{name}</span>
      <HelpTip label={name}>{children}</HelpTip>
    </dt>
  );
}

/** The name of the first fact, the rule's measure now or at the end, and what that measure is. */
function NowName({ alert }: { alert: AlertDetails }) {
  const measures = ruleMeasures(alert.rule, alert.windowSeconds);

  return (
    <FactName name={alert.resolvedAt ? "At the end" : "Now"}>
      {alert.resolvedAt
        ? "The last value the rule measured before the alert resolved."
        : "The value the rule measured at its last check."}{" "}
      It measures: {measures.endsWith(".") ? measures : `${measures}.`}
    </FactName>
  );
}

/** The chart of the rule's measure, with its caption and legend. */
function AlertChart({ alert }: { alert: AlertDetails }) {
  if (alert.disk) {
    return (
      <section className="glass alert-chart" aria-labelledby="alert-chart">
        <div className="alert-chart-head">
          <div className="alert-name">
            <h2 id="alert-chart" className="cap">
              Disk {alert.disk.mountpoint} in use · the week before and the forecast
            </h2>
            <HelpTip label="Disk in use">
              The disk&apos;s share in use, from a week before the alert opened to a day after it resolved, or to now.
              The forecast runs on from now at the pace of the last day, to the day the disk is full.
            </HelpTip>
          </div>
          <ul className="alert-legend" aria-hidden="true">
            <li className="legend-disk">in use</li>
            <li className="legend-forecast">forecast</li>
            <li className="legend-warning">full</li>
          </ul>
        </div>
        <DiskChart alert={alert} disk={alert.disk} />
      </section>
    );
  }

  if (alert.latency) {
    return (
      <section className="glass alert-chart" aria-labelledby="alert-chart">
        <div className="alert-chart-head">
          <div className="alert-name">
            <h2 id="alert-chart" className="cap">
              {alert.block.name} · request duration, per{" "}
              {alert.latency.bucketSeconds === 60 ? "minute" : `${alert.latency.bucketSeconds / 60} min`}
            </h2>
            <HelpTip label="Request duration">
              How long the service took to answer HTTP requests: the median (p50) and the 95th percentile (p95), with
              its requests below, from 30 minutes before the alert opened to 10 minutes after it resolved, or to now.
              The rule judges the p95 against the dashed caution and warning lines.
            </HelpTip>
          </div>
          <ul className="chart-legend" aria-hidden="true">
            <li className="legend-p95">p95</li>
            <li className="legend-p50">p50</li>
            <li className="legend-warning">warning {formatMeasure(alert.rule, alert.warningThreshold)}</li>
            <li className="legend-caution">caution {formatMeasure(alert.rule, alert.cautionThreshold)}</li>
            <li className="legend-records">requests</li>
          </ul>
        </div>
        <LatencyChart alert={alert} latency={alert.latency} />
      </section>
    );
  }

  if (!isErrorRate(alert)) {
    return null;
  }

  return (
    <section className="glass alert-chart" aria-labelledby="alert-chart">
      <div className="alert-chart-head">
        <div className="alert-name">
          <h2 id="alert-chart" className="cap">
            Error rate · {Math.round(alert.windowSeconds / 60)}-minute window, per{" "}
            {alert.series.bucketSeconds === 60 ? "minute" : `${alert.series.bucketSeconds / 60} min`}
          </h2>
          <HelpTip label="Error rate">
            The share of the service&apos;s log records at Error or worse over the rule&apos;s{" "}
            {Math.round(alert.windowSeconds / 60)}-minute window, as the rule measured it, with its records below. It
            runs from 30 minutes before the alert opened to 10 minutes after it resolved, or to now; the dashed lines
            are the caution and warning thresholds.
          </HelpTip>
        </div>
        <ul className="chart-legend" aria-hidden="true">
          <li className="legend-rate">error rate</li>
          <li className="legend-warning">warning {formatMeasure(alert.rule, alert.warningThreshold)}</li>
          <li className="legend-caution">caution {formatMeasure(alert.rule, alert.cautionThreshold)}</li>
          <li className="legend-records">records</li>
        </ul>
      </div>
      <ErrorRateChart alert={alert} />
    </section>
  );
}

/** The day a disk is full, counted from its last measurement, on the person's calendar: `Nov 19`. */
function fullDay(measuredAt: string, days: number): string {
  return shortDate(new Date(measuredAt).getTime() + days * 86_400_000);
}
