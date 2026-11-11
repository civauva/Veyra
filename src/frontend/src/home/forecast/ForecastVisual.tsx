import type { ReactNode } from "react";

import type { HistogramBucket, HomeAlert, HomeDisk, HomeSnapshot } from "../../api/types";
import { subjectLabel } from "../../alerts/ruleWords";
import { formatClock } from "../../logs/format";
import { formatSeconds, formatShare } from "../../metrics/metricFormat";
import { conditionTitle, formatBytes, formatLoad, formatRatio } from "../homeFormat";
import { narrative } from "../narrative";
import { observedIcon } from "../hourWeather";
import { conditionIcon, WeatherIcon } from "../WeatherIcon";
import "./forecast.css";

/** Props for {@link ForecastVisual}. */
interface ForecastVisualProps {
  /** The latest snapshot. */
  snapshot: HomeSnapshot;
}

/** The error ratio at which the gauges reach their right end. */
const gaugeTop = 0.1;

/** The latency, in seconds, at which its gauge reaches the right end. */
const latencyTop = 1;

/**
 * The Forecast home: the environment read like a weather report. The hero says the condition and the error ratio,
 * the tiles give the key numbers, the strip shows the past hours and the expected ones, and the outlook dates what
 * is coming. Everything not measured yet says what it needs instead of showing a made-up number.
 */
export function ForecastVisual({ snapshot }: ForecastVisualProps) {
  return (
    <div className="forecast">
      <ForecastHero snapshot={snapshot} />
      <KeyMetrics snapshot={snapshot} />
      <HourlyStrip snapshot={snapshot} />
      <Outlook snapshot={snapshot} />
    </div>
  );
}

/**
 * The hero: the weather picture, the error ratio drawn large, the condition, and a sentence about it.
 *
 * @param props - The snapshot.
 */
export function ForecastHero({ snapshot }: ForecastVisualProps) {
  const { condition } = snapshot;
  const place = snapshot.environment ?? "All environments";

  return (
    <section className="fc-hero" aria-labelledby="fc-title">
      <WeatherIcon kind={conditionIcon(condition.kind)} size={112} className="fc-hero-icon" />
      {condition.errorRatio === null ? (
        // Without records there is no ratio to show large; a dash that size reads as a broken bar.
        <div className="fc-big fc-big-empty" data-testid="hero-ratio">
          No records
          <small>in the last 5 minutes</small>
        </div>
      ) : (
        <div className="fc-big" data-testid="hero-ratio">
          {heroNumber(condition.errorRatio)}
          <span>%</span>
        </div>
      )}
      <div className="fc-hero-text">
        <p className="cap">{place} · now · errors</p>
        <h1 id="fc-title">{conditionTitle(condition.kind, condition.over)}</h1>
        <p className="fc-say">{narrative(snapshot)}</p>
      </div>
    </section>
  );
}

/** The six key metrics. Latency waits for HTTP metrics and the fullest disk for host metrics, and say so. */
export function KeyMetrics({ snapshot }: ForecastVisualProps) {
  const { metrics, storage, alerts, condition } = snapshot;
  const warnings = alerts.filter((alert) => alert.severity === "Warning").length;
  const cautions = alerts.length - warnings;
  const ingest = metrics.ingestPerSecond;
  const fullest = snapshot.disks.reduce<HomeDisk | null>(
    (most, disk) => (most === null || disk.ratio > most.ratio ? disk : most),
    null,
  );

  return (
    <section className="fc-metrics" aria-label="Key metrics">
      <Tile label="Errors" value={formatRatio(metrics.errorRatio)} detail={condition.kind}>
        {metrics.errorRatio !== null && <Gauge position={metrics.errorRatio / gaugeTop} />}
      </Tile>
      {metrics.latency ? (
        <Tile
          label="Latency p95"
          value={formatSeconds(metrics.latency.p95Seconds)}
          detail={
            metrics.latency.services > 1
              ? `slowest ${metrics.latency.slowestService} ${formatSeconds(metrics.latency.slowestP95Seconds)}`
              : metrics.latency.slowestService
          }
        >
          <Gauge position={metrics.latency.p95Seconds / latencyTop} />
        </Tile>
      ) : (
        <Tile label="Latency p95" value="Not measured" detail="Send HTTP server metrics" waiting />
      )}
      <Tile
        label="Ingest"
        value={ingest < 10 ? String(Number(ingest.toFixed(1))) : String(Math.round(ingest))}
        detail="logs per second"
      />
      {storage.budgetBytes ? (
        <Tile
          label="Log storage"
          value={`${Math.round((storage.usedBytes / storage.budgetBytes) * 100)}%`}
          detail={`${formatBytes(storage.usedBytes)} of ${formatBytes(storage.budgetBytes)}`}
        >
          <Gauge position={storage.usedBytes / storage.budgetBytes} />
        </Tile>
      ) : (
        <Tile label="Log storage" value={formatBytes(storage.usedBytes)} detail="no budget set" />
      )}
      {fullest ? (
        <Tile
          label="Fullest disk"
          value={formatShare(fullest.ratio)}
          detail={`${fullest.mountpoint} on ${fullest.host}`}
        >
          <Gauge position={fullest.ratio} />
        </Tile>
      ) : (
        <Tile label="Backups" value="Not watched yet" detail="Needs a backup job to report" waiting />
      )}
      <Tile
        label="Alerts"
        value={String(alerts.length)}
        detail={alerts.length === 0 ? "none open" : `${warnings} warning, ${cautions} caution`}
      />
    </section>
  );
}

/** Props for {@link Tile}. */
interface TileProps {
  /** The caption. */
  label: string;
  /** The big value. */
  value: string;
  /** The line under it. */
  detail: string;
  /** Drawn dashed: the value is not measured yet. */
  waiting?: boolean;
  /** An optional gauge. */
  children?: ReactNode;
}

function Tile({ label, value, detail, waiting = false, children }: TileProps) {
  return (
    <div className={waiting ? "glass fc-tile waiting" : "glass fc-tile"}>
      <p className="cap">{label}</p>
      <span className="fc-tile-value">{value}</span>
      <span className="fc-tile-detail">{detail}</span>
      {children}
    </div>
  );
}

/** A green-to-red track with a marker, from 0 to 1. */
function Gauge({ position }: { position: number }) {
  const left = `${Math.min(Math.max(position, 0), 1) * 100}%`;

  return (
    <span className="fc-gauge" aria-hidden="true">
      <i style={{ left }} />
    </span>
  );
}

/** The hours the strip shows before the expected ones: the past six and now. */
const stripHours = 7;

/**
 * The past six hours and now, observed, then the next six, expected from the same hours on earlier days. The snapshot
 * carries a full day (VEY-24); the strip keeps the latest hours and the wall charts them all.
 */
export function HourlyStrip({ snapshot }: ForecastVisualProps) {
  const { expected, daysOfHistory } = snapshot.hourly;
  const observed = snapshot.hourly.observed.slice(-stripHours);
  const happenings = [...snapshot.alerts, ...snapshot.resolved];
  const busiest = Math.max(...expected.map((hour) => hour.load ?? 0));
  const learned =
    daysOfHistory === 0
      ? "Expected · still learning"
      : `Expected · load from ${daysOfHistory} ${daysOfHistory === 1 ? "day" : "days"} so far`;

  return (
    <section className="glass fc-hours" aria-label="Hourly">
      <div className="fc-hours-head">
        <p className="cap">Observed · errors</p>
        <p className="cap">{learned}</p>
      </div>
      <ol className="fc-hour-row" aria-label="Hours">
        {observed.map((bucket, index) => {
          const now = index === observed.length - 1;
          const ratio = bucket.total === 0 ? null : bucket.errors / bucket.total;
          const note = now ? "" : firstHappening(happenings, bucket);

          return (
            <li key={bucket.start} className={now ? "fc-hour now" : "fc-hour"}>
              <span className="fc-hour-time">{now ? "Now" : formatClock(bucket.start).slice(0, 2)}</span>
              <WeatherIcon
                kind={now ? conditionIcon(snapshot.condition.kind) : observedIcon(bucket, ratio)}
                className="fc-hour-icon"
              />
              <span className="fc-hour-value">{formatRatio(ratio)}</span>
              <span className="fc-hour-note">{note}</span>
            </li>
          );
        })}
        {expected.map((hour) => (
          <li key={hour.start} className={hour.load === null ? "fc-hour learning" : "fc-hour expected"}>
            <span className="fc-hour-time">{formatClock(hour.start).slice(0, 2)}</span>
            <WeatherIcon kind={hour.load === null ? "cloud" : "wind"} className="fc-hour-icon" />
            <span className="fc-hour-value">{formatLoad(hour.load)}</span>
            <span className="fc-hour-note">
              {hour.load !== null && hour.load === busiest && busiest >= 1.2 ? "busiest" : ""}
            </span>
          </li>
        ))}
      </ol>
    </section>
  );
}

/** The outlook for the next 30 days. Only log storage can be projected from logs; the rest says what it needs. */
export function Outlook({ snapshot }: ForecastVisualProps) {
  const { storage } = snapshot;
  const budget = storage.budgetBytes;
  let lead = "—";
  let text: string;
  let position: number | null = null;
  let tone = "fc-calm";

  if (budget === null) {
    text = `No storage budget is set; logs are kept for ${storage.retentionDays} days`;
  } else if (storage.reachesBudget === null) {
    text = `Learning how fast logs grow; the ${formatBytes(budget)} budget is projected after the first full day`;
  } else if (storage.reachesBudget && storage.daysUntilBudget !== null) {
    lead = storage.daysUntilBudget === 1 ? "1 day" : `${storage.daysUntilBudget} days`;
    text = `Log storage reaches its ${formatBytes(budget)} budget; oldest days are dropped from then on`;
    position = storage.daysUntilBudget / 30;
    tone = storage.daysUntilBudget <= 7 ? "fc-hot" : "fc-warm";
  } else {
    text = `Log storage stays under its ${formatBytes(budget)} budget with ${storage.retentionDays} days kept`;
  }

  return (
    <section className="glass fc-outlook" aria-label="Outlook">
      <div className="fc-outlook-head">
        <p className="cap">Outlook · next 30 days</p>
        <div className="fc-ticks" aria-hidden="true">
          <span>today</span>
          <span>1 wk</span>
          <span>2 wk</span>
          <span>30 d</span>
        </div>
      </div>
      <div className={`fc-outlook-row ${tone}`}>
        <b>{lead}</b>
        <WeatherIcon kind="cloud" className="fc-outlook-icon" />
        <span>{text}</span>
        <span className="fc-track" aria-hidden="true">
          {position !== null && <i style={{ left: `${Math.min(position, 1) * 100}%` }} />}
        </span>
      </div>
      <DiskOutlook disks={snapshot.disks} />
      <div className="fc-outlook-row waiting">
        <b>—</b>
        <WeatherIcon kind="cloud" className="fc-outlook-icon" />
        <span>Certificate and error-budget outlooks come with traces and SLOs</span>
        <span />
      </div>
    </section>
  );
}

/** The most disks the outlook lists. */
const maxDiskRows = 2;

/**
 * The disks that fill within 30 days, from host metrics. When none fills, one calm row says so; without host metrics,
 * a waiting row says what is needed.
 */
function DiskOutlook({ disks }: { disks: HomeDisk[] }) {
  const filling = disks.filter((disk) => disk.daysUntilFull !== null && disk.daysUntilFull <= 30).slice(0, maxDiskRows);

  if (disks.length === 0) {
    return (
      <div className="fc-outlook-row waiting">
        <b>—</b>
        <WeatherIcon kind="cloud" className="fc-outlook-icon" />
        <span>Disk outlooks come with host metrics from the OpenTelemetry Collector</span>
        <span />
      </div>
    );
  }

  if (filling.length === 0) {
    const hosts = new Set(disks.map((disk) => disk.host)).size;

    return (
      <div className="fc-outlook-row fc-calm">
        <b>—</b>
        <WeatherIcon kind="sun" className="fc-outlook-icon" />
        <span>No disk fills up within 30 days on {hosts === 1 ? "the watched host" : `${hosts} watched hosts`}</span>
        <span className="fc-track" aria-hidden="true" />
      </div>
    );
  }

  return (
    <>
      {filling.map((disk) => {
        const days = Math.max(1, Math.round(disk.daysUntilFull ?? 0));

        return (
          <div
            key={`${disk.host}${disk.mountpoint}`}
            className={days <= 7 ? "fc-outlook-row fc-hot" : "fc-outlook-row fc-warm"}
          >
            <b>{days === 1 ? "1 day" : `${days} days`}</b>
            <WeatherIcon kind="cloud" className="fc-outlook-icon" />
            <span>
              Disk {disk.mountpoint} on {disk.host} is full at this pace ({formatShare(disk.ratio)} now)
            </span>
            <span className="fc-track" aria-hidden="true">
              <i style={{ left: `${Math.min(days / 30, 1) * 100}%` }} />
            </span>
          </div>
        );
      })}
    </>
  );
}

/** The hero number without its percent sign, which is drawn smaller beside it. */
function heroNumber(ratio: number): string {
  return formatRatio(ratio).replace("%", "");
}

/** The first alert that started within the hour, as a short note under it. */
function firstHappening(alerts: HomeAlert[], bucket: HistogramBucket): string {
  const start = new Date(bucket.start).getTime();
  const first = alerts
    .filter((alert) => {
      const since = new Date(alert.since).getTime();

      return since >= start && since < start + 3_600_000;
    })
    .sort((a, b) => a.since.localeCompare(b.since))[0];

  return first ? `${formatClock(first.since)} ${subjectLabel(first)}` : "";
}
