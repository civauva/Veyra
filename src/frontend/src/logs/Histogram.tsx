import type { LogSummary } from "../api/types";
import { formatBucket, formatClock, formatCount } from "./format";

/** Props for {@link Histogram}. */
interface HistogramProps {
  /** The counts to draw; null while they load or when they failed. */
  summary: LogSummary | null;
  /** The start of the window, for the axis. */
  from: Date;
  /** The end of the window, for the axis. */
  to: Date;
}

/**
 * Records per time bucket, with the share at Error or worse in red. Screen readers get the totals as the image's
 * name; each bar has a tooltip with its own counts.
 */
export function Histogram({ summary, from, to }: HistogramProps) {
  const buckets = summary?.buckets ?? [];
  const total = buckets.reduce((sum, bucket) => sum + bucket.total, 0);
  const errors = buckets.reduce((sum, bucket) => sum + bucket.errors, 0);
  const tallest = Math.max(1, ...buckets.map((bucket) => bucket.total));
  const caption = summary ? `Records per ${formatBucket(summary.bucketSeconds)} · errors in red` : "Records over time";

  return (
    <figure className="histogram">
      <figcaption className="section-title">{caption}</figcaption>
      <div
        className="histogram-bars"
        role="img"
        aria-label={`${formatCount(total)} records, ${formatCount(errors)} errors, ${formatClock(from)} to ${formatClock(to)} UTC`}
      >
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
