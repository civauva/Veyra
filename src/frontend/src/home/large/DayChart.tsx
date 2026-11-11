import type { HomeSnapshot } from "../../api/types";
import { formatClock } from "../../logs/format";
import { formatLoad, formatRatio } from "../homeFormat";
import { observedIcon } from "../hourWeather";

/** Props for {@link DayChart}. */
interface DayChartProps {
  /** The latest snapshot. */
  snapshot: HomeSnapshot;
}

/** An expected load from which the coming hour is named as the busiest one, as in the hourly strip. */
const busyLoad = 1.2;

/** Every how many hours the time axis is labelled, counted back from now. */
const labelEvery = 3;

/**
 * The last 24 hours as bars of records an hour with their errors in red, then the next six hours as the load
 * expected from the same hours on earlier days (VEY-24). Shown on the wall, where it takes the place of the hourly
 * strip. An hour whose error share reaches a threshold of the built-in rule is tinted like its weather.
 *
 * An expected hour is drawn as its load times the average of the past hours, so both read on one scale.
 */
export function DayChart({ snapshot }: DayChartProps) {
  const { observed, expected, daysOfHistory } = snapshot.hourly;
  const last = observed.length - 1;
  // The current hour is still filling, so the average leaves it out when there is anything else.
  const past = observed.length > 1 ? observed.slice(0, -1) : observed;
  const average = past.reduce((sum, bucket) => sum + bucket.total, 0) / Math.max(past.length, 1);
  const top = niceTop(
    Math.max(1, ...observed.map((bucket) => bucket.total), ...expected.map((hour) => (hour.load ?? 0) * average)),
  );
  const ticks = [1, 0.75, 0.5, 0.25];

  return (
    <section className="glass lg-panel lg-day" aria-label="Last 24 hours">
      <div className="lg-head">
        <h2 className="cap">Last 24 hours · records and errors</h2>
        <span className="lg-legend" aria-hidden="true">
          <i className="records" />
          records an hour
          <i className="errors" />
          errors
          <i className="expected" />
          expected
        </span>
      </div>
      <div className="lg-chart">
        <div className="lg-grid" aria-hidden="true">
          {ticks.map((share) => (
            <span key={share} style={{ bottom: `${share * 100}%` }}>
              <em>{formatCompact(share * top)}</em>
            </span>
          ))}
        </div>
        <ol className="lg-bars" style={{ gridTemplateColumns: `repeat(${observed.length + expected.length}, 1fr)` }}>
          {observed.map((bucket, index) => {
            const now = index === last;
            const ratio = bucket.total === 0 ? null : bucket.errors / bucket.total;
            const hour = formatClock(bucket.start).slice(0, 2);
            const words =
              bucket.total === 0
                ? "no records"
                : `${bucket.total.toLocaleString("en-US")} records, ${formatRatio(ratio)} errors`;

            return (
              <li
                key={bucket.start}
                className={`lg-bar ${observedIcon(bucket, ratio)}${now ? " now" : ""}`}
                aria-label={`${now ? "Now" : `${hour}:00`}: ${words}`}
                title={`${now ? "Now" : `${hour}:00`}: ${words}`}
              >
                <i style={{ height: `${(bucket.total / top) * 100}%` }}>
                  {bucket.errors > 0 && <b style={{ height: `${(bucket.errors / bucket.total) * 100}%` }} />}
                </i>
                <span className="lg-bar-time" aria-hidden="true">
                  {now ? "Now" : (last - index) % labelEvery === 0 ? hour : ""}
                </span>
              </li>
            );
          })}
          {expected.map((hour, index) => {
            const time = formatClock(hour.start).slice(0, 2);
            const words = hour.load === null ? "still learning" : `${formatLoad(hour.load)} an average hour`;

            return (
              <li
                key={hour.start}
                className={hour.load === null ? "lg-bar learning" : "lg-bar expected"}
                aria-label={`${time}:00 expected: ${words}`}
                title={`${time}:00 expected: ${words}`}
              >
                <i style={{ height: hour.load === null ? "0" : `${((hour.load * average) / top) * 100}%` }} />
                <span className="lg-bar-time" aria-hidden="true">
                  {(index + 1) % labelEvery === 0 ? time : ""}
                </span>
              </li>
            );
          })}
        </ol>
      </div>
      <p className="lg-note">{sentence(snapshot, daysOfHistory)}</p>
    </section>
  );
}

/** What the day says: its records and errors, then what the coming hours hold. */
function sentence(snapshot: HomeSnapshot, daysOfHistory: number): string {
  const { observed, expected } = snapshot.hourly;
  const records = observed.reduce((sum, bucket) => sum + bucket.total, 0);
  const errors = observed.reduce((sum, bucket) => sum + bucket.errors, 0);
  const day =
    records === 0
      ? `No records in the last ${observed.length} hours`
      : `${records.toLocaleString("en-US")} records in the last ${observed.length} hours, ${formatRatio(errors / records)} of them errors`;
  const busiest = expected.reduce<(typeof expected)[number] | null>(
    (most, hour) => (hour.load !== null && (most === null || hour.load > (most.load ?? 0)) ? hour : most),
    null,
  );
  const ahead =
    daysOfHistory === 0 || busiest === null
      ? "the coming hours are still being learned"
      : (busiest.load ?? 0) >= busyLoad
        ? `the busiest hour ahead is ${formatClock(busiest.start)}, ${(busiest.load ?? 0).toFixed(1)} times an average hour`
        : "the coming hours look like an average hour";

  return `${day}; ${ahead}.`;
}

/** The smallest round number at or above the value, so the grid lines fall on readable counts. */
function niceTop(value: number): number {
  const magnitude = 10 ** Math.floor(Math.log10(value));

  for (const step of [1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10]) {
    if (step * magnitude >= value) {
      return step * magnitude;
    }
  }

  return 10 * magnitude;
}

/** A count for an axis: `800`, `12k`, or `1.5M`. */
function formatCompact(value: number): string {
  if (value >= 1_000_000) {
    return `${Number((value / 1_000_000).toFixed(1))}M`;
  }

  if (value >= 1000) {
    return `${Number((value / 1000).toFixed(1))}k`;
  }

  return String(Math.round(value));
}
