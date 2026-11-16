import type { LogSummary } from "../api/types";
import { zoneLabel } from "../time/zone";
import { formatBucket, formatClock, formatCount } from "./format";

/** Props for {@link Histogram}. */
interface HistogramProps {
  /** The counts to draw; null while they load or when they failed. */
  summary: LogSummary | null;
  /** Whether newer counts are on their way; with no counts yet, the histogram says it is counting. */
  counting?: boolean;
  /** The start of the window, for the axis. */
  from: Date;
  /** The end of the window, for the axis. */
  to: Date;
}

/**
 * Records per time bucket, with the share at Error or worse in red. Screen readers get the totals as the image's
 * name; each bar has a tooltip with its own counts.
 */
export function Histogram({ summary, counting = false, from, to }: HistogramProps) {
  const buckets = summary?.buckets ?? [];
  const total = buckets.reduce((sum, bucket) => sum + bucket.total, 0);
  const errors = buckets.reduce((sum, bucket) => sum + bucket.errors, 0);
  const tallest = Math.max(1, ...buckets.map((bucket) => bucket.total));
  const caption = summary ? `Records per ${formatBucket(summary.bucketSeconds)} · errors in red` : "Records over time";
  const window = `${formatClock(from)} to ${formatClock(to)} ${zoneLabel(new Date(to))}`;
  const label =
    !summary && counting
      ? `Counting records, ${window}`
      : `${formatCount(total)} records, ${formatCount(errors)} errors, ${window}`;

  return (
    <figure className="histogram">
      <figcaption className="section-title">{caption}</figcaption>
      <div className="histogram-bars" role="img" aria-label={label} aria-busy={counting}>
        {buckets.map((bucket) => (
          <span
            key={bucket.start}
            className="histogram-bar"
            style={{ height: `${(bucket.total / tallest) * 100}%` }}
            title={`${formatClock(bucket.start)} · ${formatCount(bucket.total)} records, ${formatCount(bucket.errors)} errors`}
          >
            {bucket.errors > 0 && (
              <span className="histogram-errors" style={{ height: `${(bucket.errors / bucket.total) * 100}%` }} />
            )}
          </span>
        ))}
      </div>
      <div className="histogram-axis" aria-hidden="true">
        <span>{formatClock(from)}</span>
        <span>{formatClock(to)}</span>
      </div>
    </figure>
  );
}
