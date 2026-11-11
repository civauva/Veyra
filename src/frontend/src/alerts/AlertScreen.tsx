import { useEffect } from "react";
import { Link, useParams } from "react-router";

import type { AlertDetails } from "../api/types";
import { useShell } from "../app/shellContext";
import { formatBytes, severityWord } from "../home/homeFormat";
import { WeatherIcon } from "../home/WeatherIcon";
import { formatClock, formatCount } from "../logs/format";
import { defaultFilters, filtersToUrl, type TimeRange } from "../logs/logFilters";
import { alertScene, alertStory, formatDuration, headline, isErrorRate, stateLine } from "./alertFormat";
import { AlertTimeline } from "./AlertTimeline";
import { DiskChart } from "./DiskChart";
import { ErrorRateChart } from "./ErrorRateChart";
import { HostDisks } from "./HostDisks";
import { LatencyChart } from "./LatencyChart";
import { formatDays, formatDiskShare, formatMeasure, formatThreshold } from "./ruleWords";
import { SlowRoutes } from "./SlowRoutes";
import { TopErrors } from "./TopErrors";
import { useAlertDetails } from "./useAlertDetails";
import "../home/home.css";
import "./alert.css";

/**
 * One alert, reached from the Alerts screen, from Needs you on the home, and from the link in every notification:
 * what it says, how its measure moved, who was told, and where to look next. An error-rate alert shows its errors, a
 * disk alert the disk's week and forecast, a latency alert the request durations and the slowest routes. It paints
 * the alert's weather behind the shell, and clear skies once it resolved.
 */
export function AlertScreen() {
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
        <div className="alert-page">
          <Link className="alert-back" to="/alerts">
            ← Alerts
          </Link>
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
      <div className="alert-page">
        <Link className="alert-back" to="/alerts">
          ← Alerts
        </Link>
        {error && (
          <p className="home-stale" role="alert">
            Could not refresh: {error}. Showing the alert as of {formatClock(alert.updatedAt)} UTC.
          </p>
        )}
        <AlertHero alert={alert} />
        <AlertFacts alert={alert} />
        <AlertChart alert={alert} />
        <div className="alert-lower">
          <AlertTimeline alert={alert} />
          {isErrorRate(alert) && <TopErrors alert={alert} />}
          {alert.disk && <HostDisks alert={alert} disk={alert.disk} />}
          {alert.latency && <SlowRoutes latency={alert.latency} />}
        </div>
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

/** Where to look next: a service's error logs, a slow service's metrics, a host's disks. */
function Onward({ alert }: { alert: AlertDetails }) {
  const name = alert.block.name;

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
      <Link className="step-button" to={`/logs?${around}`}>
        ± 1 minute around {formatClock(alert.openedAt)}
      </Link>
    </>
  );
}

/** Five numbers: now, the rule's own figure, the threshold, how long, and who was told. */
function AlertFacts({ alert }: { alert: AlertDetails }) {
  const told = [
    ...new Map(
      alert.events.flatMap((event) => event.deliveries).map((delivery) => [delivery.channelId, delivery.channelName]),
    ).values(),
  ];
  const other = alert.severity === "Warning" ? "caution" : "warning";
  const otherThreshold = alert.severity === "Warning" ? alert.cautionThreshold : alert.warningThreshold;

  return (
    <dl className="alert-facts">
      <FirstFacts alert={alert} />
      <div className="glass fact">
        <dt className="cap">Threshold</dt>
        <dd className="fact-value">
          {formatMeasure(alert.rule, alert.threshold)} <small>{severityWord(alert.severity)}</small>
        </dd>
        <dd className="fact-note">
          {other} {alert.rule === "disk-filling" ? "" : "from "}
          {formatThreshold(alert.rule, otherThreshold)}
        </dd>
      </div>
      <div className="glass fact">
        <dt className="cap">{alert.resolvedAt ? "Lasted" : "Since"}</dt>
        <dd className="fact-value">
          {alert.resolvedAt
            ? formatDuration(new Date(alert.resolvedAt).getTime() - new Date(alert.openedAt).getTime())
            : formatClock(alert.openedAt)}
        </dd>
        <dd className="fact-note">
          {alert.resolvedAt
            ? `${formatClock(alert.openedAt)}–${formatClock(alert.resolvedAt)} UTC`
            : `${new Date(alert.openedAt).toISOString().slice(0, 10)} · UTC`}
        </dd>
      </div>
      <div className="glass fact">
        <dt className="cap">Notified</dt>
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
      <dt className="cap">Peak</dt>
      <dd className="fact-value">{formatMeasure(alert.rule, alert.peak)}</dd>
      <dd className="fact-note">{alert.peakAt ? `at ${formatClock(alert.peakAt)}` : "time not recorded"}</dd>
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
          <dt className="cap">Full in</dt>
          <dd className="fact-value">{disk.daysUntilFull === null ? "not filling" : formatDays(disk.daysUntilFull)}</dd>
          <dd className="fact-note">
            {disk.daysUntilFull === null
              ? "at the pace of the last day"
              : `on ${fullDay(alert.updatedAt, disk.daysUntilFull)} at this pace`}
          </dd>
        </div>
        <div className="glass fact">
          <dt className="cap">In use</dt>
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
          <dt className="cap">{alert.resolvedAt ? "At the end" : "Now"}</dt>
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
        <dt className="cap">{alert.resolvedAt ? "At the end" : "Now"}</dt>
        <dd className="fact-value">{formatMeasure(alert.rule, alert.value)}</dd>
        <dd className="fact-note">
          {last && last.windowTotal > 0
            ? `${formatCount(last.windowErrors)} of ${formatCount(last.windowTotal)} records, last ${minutes} min`
            : `Over the last ${minutes} min`}
        </dd>
      </div>
      {peak}
    </>
  );
}

/** The chart of the rule's measure, with its caption and legend. */
function AlertChart({ alert }: { alert: AlertDetails }) {
  if (alert.disk) {
    return (
      <section className="glass alert-chart" aria-labelledby="alert-chart">
        <div className="alert-chart-head">
          <h2 id="alert-chart" className="cap">
            Disk {alert.disk.mountpoint} in use · the week before and the forecast
          </h2>
          <ul className="chart-legend" aria-hidden="true">
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
          <h2 id="alert-chart" className="cap">
            {alert.block.name} · request duration, per{" "}
            {alert.latency.bucketSeconds === 60 ? "minute" : `${alert.latency.bucketSeconds / 60} min`}
          </h2>
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
        <h2 id="alert-chart" className="cap">
          Error rate · {Math.round(alert.windowSeconds / 60)}-minute window, per{" "}
          {alert.series.bucketSeconds === 60 ? "minute" : `${alert.series.bucketSeconds / 60} min`}
        </h2>
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

/** The day a disk is full, counted from its last measurement: `Nov 19`. */
function fullDay(measuredAt: string, days: number): string {
  return new Date(new Date(measuredAt).getTime() + days * 86_400_000).toLocaleDateString("en-US", {
    month: "short",
    day: "numeric",
    timeZone: "UTC",
  });
}
