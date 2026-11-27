import type { ReactNode } from "react";
import { Link } from "react-router";

import type { HomeDiskOutlook, HomeSnapshot } from "../../api/types";
import { HelpTip } from "../../ui/HelpTip";
import { formatClock } from "../../logs/format";
import { formatSeconds, formatShare } from "../../metrics/metricFormat";
import { conditionTitle, formatBytes, formatLoad, formatRatio } from "../homeFormat";
import { narrative } from "../narrative";
import { observedIcon } from "../hourWeather";
import { BlocksPanel } from "../large/BlocksPanel";
import { conditionIcon, WeatherIcon } from "../WeatherIcon";
import { useSession } from "../../session/useSession";
import "./forecast.css";

/** Props for the parts of the Forecast visual. */
interface ForecastVisualProps {
  /** The latest snapshot. */
  snapshot: HomeSnapshot;
}

/** The most services the estate draws beside the outlook, so the standard home fits a laptop screen. */
const estateLimit = 9;

/** Props for {@link ForecastVisual}. */
interface ForecastProps extends ForecastVisualProps {
  /** Lists the services and hosts beside the outlook; the wide layout gives them a column of their own instead. */
  estate?: boolean;
}

/** The error ratio at which the gauges reach their right end. */
const gaugeTop = 0.1;

/** The latency, in seconds, at which its gauge reaches the right end. */
const latencyTop = 1;

/**
 * The Forecast home: the environment read like a weather report, with one place for every question (VEY-26). The hero
 * says whether all is well, the four figures give the key numbers, the strip shows the past hours and the expected
 * ones, the estate lists what runs, and the outlook dates what is coming but is not an alert yet. The alerts
 * themselves are told only in Needs you. Everything not measured yet says what it needs instead of showing a
 * made-up number.
 */
export function ForecastVisual({ snapshot, estate = true }: ForecastProps) {
  return (
    <div className="forecast">
      <ForecastHero snapshot={snapshot} />
      <KeyMetrics snapshot={snapshot} />
      <HourlyStrip snapshot={snapshot} />
      {estate ? (
        <div className="fc-row">
          <BlocksPanel snapshot={snapshot} limit={estateLimit} />
          <Outlook snapshot={snapshot} />
        </div>
      ) : (
        <Outlook snapshot={snapshot} />
      )}
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

/**
 * The four key figures (VEY-26): errors, latency, ingest, and log storage. Disks are drawn on their hosts in the
 * estate and alerts are told in Needs you, so neither has a tile. Latency waits for HTTP metrics and says so.
 */
export function KeyMetrics({ snapshot }: ForecastVisualProps) {
  const { state } = useSession();
  const admin = state.status === "signedIn" && state.session.role === "Admin";
  const { metrics, storage } = snapshot;
  const ingest = metrics.ingestPerSecond;
  const minutes = Math.round(metrics.windowSeconds / 60);
  const latencyHelp = (
    <>
      95 of every 100 HTTP requests of the last {minutes} minutes were answered faster than this, across every service
      that sends HTTP server metrics through OpenTelemetry. The line under it names the slowest service. A service whose
      own p95 stays high gets a latency alert.
    </>
  );
  const storageHelp = storage.budgetBytes ? (
    <>
      How much of the log storage budget the stored logs take, indexes included. Once logs reach the budget, the oldest
      days are dropped; the outlook says when that will be.
    </>
  ) : (
    <>
      The space the stored logs take, indexes included. With no budget set, logs stay {storage.retentionDays} days
      whatever their size; Admins set a budget on the Settings screen.
    </>
  );

  return (
    <section className="fc-metrics" aria-label="Key metrics">
      <Tile
        label="Errors"
        help={
          <>
            The share of all log records of the last {minutes} minutes that are at Error or worse, across every service
            shown. Each service&apos;s own share is judged by the error-rate rule, which opens an alert when it is too
            high.
          </>
        }
        value={formatRatio(metrics.errorRatio)}
        detail={
          metrics.records === 0
            ? `no records in ${minutes} min`
            : `of ${metrics.records.toLocaleString("en-US")} records, ${minutes} min`
        }
      >
        {metrics.errorRatio !== null && <Gauge position={metrics.errorRatio / gaugeTop} />}
      </Tile>
      {metrics.latency ? (
        <Tile
          label="Latency p95"
          help={latencyHelp}
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
        <Tile label="Latency p95" help={latencyHelp} value="Not measured" detail="Send HTTP server metrics" waiting />
      )}
      <Tile
        label="Ingest"
        help={
          <>
            Log records received per second, averaged over the last {minutes} minutes. A sudden drop can mean a service
            stopped sending; a jump, that one logs far more than it did.
          </>
        }
        value={ingest < 10 ? String(Number(ingest.toFixed(1))) : String(Math.round(ingest))}
        detail="logs per second"
      />
      {storage.budgetBytes ? (
        <Tile
          label="Log storage"
          help={storageHelp}
          value={`${Math.round((storage.usedBytes / storage.budgetBytes) * 100)}%`}
          detail={`${formatBytes(storage.usedBytes)} of ${formatBytes(storage.budgetBytes)}`}
        >
          <Gauge position={storage.usedBytes / storage.budgetBytes} />
        </Tile>
      ) : (
        <Tile
          label="Log storage"
          help={storageHelp}
          value={formatBytes(storage.usedBytes)}
          detail={
            admin ? (
              <>
                no budget · <Link to="/settings?section=Data">Set a budget</Link>
              </>
            ) : (
              "no budget set"
            )
          }
        />
      )}
    </section>
  );
}

/** Props for {@link Tile}. */
interface TileProps {
  /** The caption. */
  label: string;
  /** What the tile means, behind the question mark beside its caption (VEY-60). */
  help: ReactNode;
  /** The big value. */
  value: string;
  /** The line under it. */
  detail: ReactNode;
  /** Drawn dashed: the value is not measured yet. */
  waiting?: boolean;
  /** An optional gauge. */
  children?: ReactNode;
}

function Tile({ label, help, value, detail, waiting = false, children }: TileProps) {
  return (
    <div className={waiting ? "glass fc-tile waiting" : "glass fc-tile"}>
      <div className="home-name">
        <span className="cap">{label}</span>
        <HelpTip label={label}>{help}</HelpTip>
      </div>
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
 * carries a full day (VEY-24); the strip keeps the latest hours and the wall charts them all. Each hour shows only its
 * weather: when an alert started is told in its details and on the Alerts screen (VEY-26).
 */
export function HourlyStrip({ snapshot }: ForecastVisualProps) {
  const { expected, daysOfHistory } = snapshot.hourly;
  const observed = snapshot.hourly.observed.slice(-stripHours);
  const learned =
    daysOfHistory === 0
      ? "Expected · still learning"
      : `Expected · load from ${daysOfHistory} ${daysOfHistory === 1 ? "day" : "days"} so far`;

  return (
    <section className="glass fc-hours" aria-label="Hourly">
      <div className="fc-hours-head">
        <div className="home-name">
          <p className="cap">Observed · errors</p>
          <HelpTip label="Observed">
            The share of log records at Error or worse in each of the last six hours, and in this hour so far, in your
            time zone. A past hour shows showers from 1% and a storm from 5%; Now shows the weather of the open alerts.
          </HelpTip>
        </div>
        <div className="home-name">
          <p className="cap">{learned}</p>
          <HelpTip label="Expected">
            How busy each of the next six hours usually is, from the same hours on up to seven earlier days: 1.0× is an
            average hour. An hour without that history yet says learning.
          </HelpTip>
        </div>
      </div>
      <ol className="fc-hour-row" aria-label="Hours">
        {observed.map((bucket, index) => {
          const now = index === observed.length - 1;
          const ratio = bucket.total === 0 ? null : bucket.errors / bucket.total;

          return (
            <li key={bucket.start} className={now ? "fc-hour now" : "fc-hour"}>
              <span className="fc-hour-time">{now ? "Now" : formatClock(bucket.start).slice(0, 2)}</span>
              <WeatherIcon
                kind={now ? conditionIcon(snapshot.condition.kind) : observedIcon(bucket, ratio)}
                className="fc-hour-icon"
              />
              <span className="fc-hour-value">{formatRatio(ratio)}</span>
            </li>
          );
        })}
        {expected.map((hour) => (
          <li key={hour.start} className={hour.load === null ? "fc-hour learning" : "fc-hour expected"}>
            <span className="fc-hour-time">{formatClock(hour.start).slice(0, 2)}</span>
            <WeatherIcon kind={hour.load === null ? "cloud" : "wind"} className="fc-hour-icon" />
            <span className="fc-hour-value">{formatLoad(hour.load)}</span>
          </li>
        ))}
      </ol>
    </section>
  );
}

/**
 * The outlook for the next 30 days: what is coming but is not an alert yet (VEY-26). A disk with an open alert is told
 * in Needs you, so it is left out here. What cannot be projected yet says what it needs.
 */
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
        <div className="home-name">
          <p className="cap">Outlook · not alerts yet</p>
          <HelpTip label="Outlook">
            What is coming in the next 30 days that is not an alert yet: when log storage reaches its budget, and disks
            that fill up at the pace of their last day. A disk with an open alert is in Needs you instead.
          </HelpTip>
        </div>
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
      <DiskOutlook watched={snapshot.disks.length > 0} outlook={snapshot.estate.diskOutlook} />
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

/** Props for {@link DiskOutlook}. */
interface DiskOutlookProps {
  /** Whether any host reports disks. */
  watched: boolean;
  /** The disks that fill within 30 days without an open alert, as the server picks them (VEY-68). */
  outlook: HomeDiskOutlook;
}

/**
 * The disks that fill within 30 days and have no open alert, from host metrics. When none fills, one calm row says
 * so; without host metrics, a waiting row says what is needed.
 */
function DiskOutlook({ watched, outlook }: DiskOutlookProps) {
  // The outlook has room for a few rows; the server has left out the disks Needs you tells.
  const filling = outlook.filling.slice(0, maxDiskRows);

  if (!watched) {
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
    const { hosts } = outlook;

    // Every disk that fills soon already has an alert, which Needs you tells.
    if (outlook.allAlerted) {
      return null;
    }

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
